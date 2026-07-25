using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private readonly ForegroundKeyToggle panelToggle = new();
    private readonly Dictionary<string, MessageRowViewModel> knownRows = new();
    private readonly DismissedChatMessages dismissedMessages = new();
    private AppSettings settings;
    private bool calibrationMode;
    private bool followingGame = true;
    private bool readerInitializationStarted;
    private bool readerInitializationComplete;
    private bool readerSampleRunning;
    private bool closing;
    private bool initialCalibrationPending;
    private IntPtr gameWindowHandle;

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
        Width = settings.PanelWidth;
        Height = settings.PanelHeight;
        initialCalibrationPending = firstRun;
        gameWindow = new GameWindowTracker(arguments.ProcessId);
        DataContext = this;

        timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(100),
            DispatcherPriority.Background,
            Timer_Tick,
            Dispatcher);
        timer.Start();
        ReaderState.Text = reader.Diagnostic;
        Visibility = Visibility.Hidden;
        Loaded += InitializeReader;
    }

    public ObservableCollection<MessageRowViewModel> Messages { get; } = new();

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        NativeWindow.SetNoActivate(this, enabled: true);
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

        bool gameIsForeground = gameWindow.IsForeground;
        if (panelToggle.Update(
                NativeWindow.IsF6Down(),
                gameIsForeground))
        {
            SetPanelVisible(!settings.PanelVisible);
        }

        UpdateGamePosition();
        if (readerInitializationComplete && !readerSampleRunning)
        {
            _ = SampleReaderAsync();
        }
    }

    private async Task SampleReaderAsync()
    {
        readerSampleRunning = true;
        try
        {
            IReadOnlyList<ChatMessageSnapshot> snapshots =
                await Task.Run(
                    () => reader.ReadRecent(settings.MaximumMessages));
            if (closing)
            {
                return;
            }
            ReaderState.Text = reader.Diagnostic;
            ReconcileMessages(snapshots);
            UpdateGamePosition();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or ObjectDisposedException)
        {
            if (!closing)
            {
                logger.Error("reader_sample_failed", exception);
            }
        }
        finally
        {
            readerSampleRunning = false;
        }
    }

    private void UpdateGamePosition()
    {
        if (!gameWindow.TryGetWindow(
                out IntPtr currentGameWindow,
                out WindowRect rectangle,
                out double scale,
                out bool minimized))
        {
            ApplyVisibility(calibrationMode && settings.PanelVisible);
            return;
        }

        if (currentGameWindow != gameWindowHandle)
        {
            gameWindowHandle = currentGameWindow;
            NativeWindow.SetOwner(this, currentGameWindow);
        }

        bool inWorld = readerInitializationComplete
            && reader.Status == ChatReaderStatus.Ready;
        if (initialCalibrationPending && inWorld && !minimized)
        {
            initialCalibrationPending = false;
            SetCalibrationMode(enabled: true);
            return;
        }

        bool shouldShow = settings.PanelVisible
            && !minimized
            && (calibrationMode
                || (inWorld && gameWindow.IsForeground));
        ApplyVisibility(shouldShow);
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
        IReadOnlyList<ChatMessageSnapshot> visibleSnapshots =
            dismissedMessages.Filter(snapshots);
        var current =
            new List<MessageRowViewModel>(visibleSnapshots.Count);
        foreach (ChatMessageSnapshot snapshot in visibleSnapshots)
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

        var activeIdentities = visibleSnapshots
            .Select(item => item.Identity)
            .ToHashSet();
        foreach (string stale in knownRows.Keys
                     .Where(identity => !activeIdentities.Contains(identity))
                     .ToArray())
        {
            knownRows.Remove(stale);
        }
        RefreshEmptyState();
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

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MessageRowViewModel row })
        {
            return;
        }

        dismissedMessages.Dismiss(row.Identity);
        knownRows.Remove(row.Identity);
        Messages.Remove(row);
        RefreshEmptyState();
    }

    private void RefreshEmptyState()
    {
        EmptyState.Visibility = Messages.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
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
            UpdateGamePosition();
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
        settings = settings with
        {
            PanelWidth = ActualWidth,
            PanelHeight = ActualHeight,
        };
        if (!gameWindow.TryGetWindow(
                out _,
                out WindowRect rectangle,
                out double scale,
                out _))
        {
            settingsStore.Save(settings);
            return;
        }
        settings = settings with
        {
            OffsetX = Left - rectangle.Left / scale,
            OffsetFromBottom =
                rectangle.Bottom / scale - (Top + ActualHeight),
            PanelWidth = ActualWidth,
            PanelHeight = ActualHeight,
        };
        settingsStore.Save(settings);
    }

    private void Hide_Click(object sender, RoutedEventArgs e) =>
        SetPanelVisible(false);

    private void SetPanelVisible(bool visible)
    {
        settings = settings with { PanelVisible = visible };
        settingsStore.Save(settings);
        if (!visible)
        {
            ApplyVisibility(false);
        }
        else
        {
            UpdateGamePosition();
        }
    }

    private void ApplyVisibility(bool visible)
    {
        if (visible)
        {
            Visibility = Visibility.Visible;
            Opacity = 1;
        }
        else
        {
            Opacity = 0;
            Visibility = Visibility.Hidden;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        closing = true;
        timer.Stop();
        settings = settings with
        {
            PanelWidth = ActualWidth,
            PanelHeight = ActualHeight,
        };
        settingsStore.Save(settings);
        if (readerInitializationComplete && !readerSampleRunning)
        {
            reader.Dispose();
        }
    }
}
