using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using FareverTranslate.Core.Configuration;
using FareverTranslate.Core.Diagnostics;
using FareverTranslate.Core.Memory;
using FareverTranslate.Core.Models;
using FareverTranslate.Core.Security;
using FareverTranslate.Core.Translation;

namespace FareverTranslate.Host;

public partial class MainWindow : Window
{
    private readonly HostArguments arguments;
    private readonly HashLinkChatReader reader;
    private readonly SettingsStore settingsStore;
    private readonly SecretStore secretStore;
    private readonly TranslationCache cache;
    private readonly GoogleTranslationClient translator;
    private readonly AppLogger logger;
    private readonly GameWindowTracker gameWindow;
    private readonly DispatcherTimer timer;
    private readonly Dictionary<string, MessageRowViewModel> knownRows = new();
    private AppSettings settings;
    private HwndSource? source;
    private bool calibrationMode;
    private bool hotKeyRegistered;
    private bool followingGame = true;
    private bool readerInitializationStarted;
    private bool readerInitializationComplete;

    internal MainWindow(
        HostArguments arguments,
        HashLinkChatReader reader,
        SettingsStore settingsStore,
        SecretStore secretStore,
        TranslationCache cache,
        GoogleTranslationClient translator,
        AppLogger logger)
    {
        InitializeComponent();
        this.arguments = arguments;
        this.reader = reader;
        this.settingsStore = settingsStore;
        this.secretStore = secretStore;
        this.cache = cache;
        this.translator = translator;
        this.logger = logger;
        bool firstRun = !settingsStore.Exists;
        settings = settingsStore.Load();
        gameWindow = new GameWindowTracker(arguments.ProcessId);
        DataContext = this;

        timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            Timer_Tick,
            Dispatcher);
        timer.Start();
        ReaderState.Text = reader.Diagnostic;
        Visibility = settings.PanelVisible
            ? Visibility.Visible
            : Visibility.Hidden;
        if (firstRun)
        {
            Loaded += (_, _) => SetCalibrationMode(enabled: true);
        }
        Loaded += InitializeReader;
    }

    public ObservableCollection<MessageRowViewModel> Messages { get; } = new();

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        NativeWindow.SetNoActivate(this, enabled: true);
        source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source?.AddHook(WindowProcedure);
        if (source is not null)
        {
            hotKeyRegistered = NativeWindow.RegisterHotKey(
                source.Handle,
                NativeWindow.HotKeyToggle,
                NativeWindow.ModControl | NativeWindow.ModShift,
                (uint)KeyInterop.VirtualKeyFromKey(Key.T));
        }
        if (!hotKeyRegistered)
        {
            logger.Warn("global_hotkey_unavailable");
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        try
        {
            Process process = Process.GetProcessById(arguments.ProcessId);
            if (process.HasExited)
            {
                Close();
                return;
            }
        }
        catch (ArgumentException)
        {
            Close();
            return;
        }

        UpdateGamePosition();
        if (readerInitializationComplete)
        {
            IReadOnlyList<ChatMessageSnapshot> snapshots =
                reader.ReadRecent(settings.MaximumMessages);
            ReaderState.Text = reader.Diagnostic;
            ReconcileMessages(snapshots);
        }
    }

    private void UpdateGamePosition()
    {
        if (!gameWindow.TryGetWindow(
                out _,
                out WindowRect rectangle,
                out double scale,
                out bool minimized))
        {
            Visibility = settings.PanelVisible
                ? Visibility.Visible
                : Visibility.Hidden;
            return;
        }

        bool shouldShow = settings.PanelVisible && !minimized;
        Visibility = shouldShow
            ? Visibility.Visible
            : Visibility.Hidden;
        if (!shouldShow || !followingGame || calibrationMode)
        {
            return;
        }

        OverlayPlacement placement = OverlayPlacement.Calculate(
            rectangle.Left,
            rectangle.Bottom,
            scale,
            ActualHeight,
            settings.OffsetX,
            settings.OffsetFromBottom);
        Left = placement.Left;
        Top = placement.Top;
    }

    private void ReconcileMessages(
        IReadOnlyList<ChatMessageSnapshot> snapshots)
    {
        var current = new List<MessageRowViewModel>(snapshots.Count);
        foreach (ChatMessageSnapshot snapshot in snapshots)
        {
            if (!knownRows.TryGetValue(
                    snapshot.Identity,
                    out MessageRowViewModel? row))
            {
                row = new MessageRowViewModel(snapshot);
                knownRows[snapshot.Identity] = row;
            }
            current.Add(row);
        }

        for (int index = Messages.Count - 1; index >= 0; index--)
        {
            if (!current.Contains(Messages[index]))
            {
                Messages.RemoveAt(index);
            }
        }
        for (int index = 0; index < current.Count; index++)
        {
            if (index < Messages.Count && ReferenceEquals(Messages[index], current[index]))
            {
                continue;
            }
            if (Messages.Contains(current[index]))
            {
                Messages.Move(Messages.IndexOf(current[index]), index);
            }
            else
            {
                Messages.Insert(index, current[index]);
            }
        }

        var activeIdentities = snapshots.Select(item => item.Identity).ToHashSet();
        foreach (string stale in knownRows.Keys
                     .Where(identity => !activeIdentities.Contains(identity))
                     .ToArray())
        {
            knownRows.Remove(stale);
        }
        EmptyState.Visibility = Messages.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void InitializeReader(object sender, RoutedEventArgs e)
    {
        if (readerInitializationStarted)
        {
            return;
        }
        readerInitializationStarted = true;
        ReaderState.Text = "Analizando el chat de Farever…";
        try
        {
            await Task.Run(reader.Initialize);
            logger.Info("hashlink_layout", reader.DescribeLayout());
        }
        catch (Exception exception)
        {
            logger.Error("reader_background_failed", exception);
        }
        finally
        {
            readerInitializationComplete = true;
            ReaderState.Text = reader.Diagnostic;
        }
    }

    private async void Translate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MessageRowViewModel row }
            || row.Translating)
        {
            return;
        }

        row.Status = string.Empty;
        if (cache.TryGet(row.Original, out CachedTranslation? cached)
            && cached is not null)
        {
            row.Translation = cached.TranslatedText;
            return;
        }
        if (!secretStore.TryLoadApiKey(out string apiKey))
        {
            row.Status = "Falta configurar la clave.";
            OpenApiKeyWindow();
            return;
        }

        row.Translating = true;
        try
        {
            TranslationOutcome result = await translator.TranslateAsync(
                row.Original,
                settings.TargetLanguage,
                apiKey,
                CancellationToken.None);
            if (result.Success && result.TranslatedText is not null)
            {
                row.Translation = result.TranslatedText;
                row.Status = string.Empty;
                cache.Put(
                    row.Original,
                    result.TranslatedText,
                    result.DetectedLanguage);
            }
            else
            {
                row.Status = result.UserMessage;
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            logger.Error("translation_local_storage_failed", exception);
            row.Status = "No se pudo guardar la traducción.";
        }
        finally
        {
            row.Translating = false;
        }
    }

    private void ApiKey_Click(object sender, RoutedEventArgs e) =>
        OpenApiKeyWindow();

    private void OpenApiKeyWindow()
    {
        var dialog = new ApiKeyWindow(secretStore);
        dialog.ShowDialog();
    }

    private void Calibration_Click(object sender, RoutedEventArgs e)
    {
        SetCalibrationMode(!calibrationMode);
    }

    private void SetCalibrationMode(bool enabled)
    {
        calibrationMode = enabled;
        followingGame = !enabled;
        CalibrationButton.Content = calibrationMode ? "Listo" : "Calibrar";
        CalibrationHint.Visibility = calibrationMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        PanelBorder.BorderBrush = calibrationMode
            ? System.Windows.Media.Brushes.Gold
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
        NativeWindow.SetNoActivate(this, enabled: !calibrationMode);
        if (enabled)
        {
            Activate();
            Focus();
        }
        else
        {
            SaveCalibration();
            followingGame = true;
        }
    }

    private void DragHeader_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (calibrationMode && e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SaveCalibration()
    {
        if (!gameWindow.TryGetWindow(
                out _,
                out WindowRect rectangle,
                out double scale,
                out _))
        {
            return;
        }
        settings = settings with
        {
            OffsetX = Left - rectangle.Left / scale,
            OffsetFromBottom =
                rectangle.Bottom / scale - (Top + ActualHeight),
        };
        settingsStore.Save(settings);
    }

    private void Hide_Click(object sender, RoutedEventArgs e) =>
        SetPanelVisible(false);

    private void SetPanelVisible(bool visible)
    {
        settings = settings with { PanelVisible = visible };
        settingsStore.Save(settings);
        Visibility = visible
            ? Visibility.Visible
            : Visibility.Hidden;
    }

    private IntPtr WindowProcedure(
        IntPtr window,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (message == NativeWindow.WmHotKey
            && wordParameter.ToInt32() == NativeWindow.HotKeyToggle)
        {
            SetPanelVisible(!settings.PanelVisible);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        timer.Stop();
        if (readerInitializationComplete)
        {
            reader.Dispose();
        }
        if (source is not null)
        {
            if (hotKeyRegistered)
            {
                NativeWindow.UnregisterHotKey(
                    source.Handle,
                    NativeWindow.HotKeyToggle);
            }
            source.RemoveHook(WindowProcedure);
        }
    }
}
