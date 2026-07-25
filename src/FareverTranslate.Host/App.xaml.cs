using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using FareverTranslate.Core.Configuration;
using FareverTranslate.Core.Diagnostics;
using FareverTranslate.Core.Memory;
using FareverTranslate.Core.Security;
using FareverTranslate.Core.Translation;

namespace FareverTranslate.Host;

public partial class App : Application
{
    private Mutex? instanceMutex;
    private HttpClient? httpClient;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!HostArguments.TryParse(e.Args, out HostArguments? arguments)
            || arguments is null)
        {
            MessageBox.Show(
                "Inicio inválido. Use --pid <pid> --game-dir <ruta-absoluta>.",
                "Farever Translate",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        string mutexName = $"Local\\FareverTranslate.Host.{arguments.ProcessId}";
        instanceMutex = new Mutex(
            initiallyOwned: true,
            mutexName,
            out bool createdNew);
        if (!createdNew)
        {
            instanceMutex.Dispose();
            instanceMutex = null;
            Shutdown(0);
            return;
        }

        var paths = new AppPaths();
        var logger = new AppLogger(paths);
        var reader = new HashLinkChatReader(
            arguments.ProcessId,
            arguments.GameDirectory,
            logger);

        httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8),
        };
        var window = new MainWindow(
            arguments,
            reader,
            new SettingsStore(paths),
            new SecretStore(paths),
            new TranslationCache(paths),
            new GoogleTranslationClient(httpClient),
            logger);
        MainWindow = window;
        window.Closed += (_, _) => Shutdown();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        httpClient?.Dispose();
        if (instanceMutex is not null)
        {
            try
            {
                instanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The process is already shutting down without ownership.
            }
            instanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}

internal sealed record HostArguments(int ProcessId, string GameDirectory)
{
    public static bool TryParse(
        IReadOnlyList<string> arguments,
        out HostArguments? result)
    {
        result = null;
        string? pidValue = Read(arguments, "--pid");
        string? gameDirectory = Read(arguments, "--game-dir");
        if (!int.TryParse(
                pidValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int processId)
            || processId <= 0
            || string.IsNullOrWhiteSpace(gameDirectory)
            || !Path.IsPathFullyQualified(gameDirectory))
        {
            return false;
        }

        string fullDirectory = Path.GetFullPath(gameDirectory);
        if (!File.Exists(Path.Combine(fullDirectory, "Farever.exe"))
            || !File.Exists(Path.Combine(fullDirectory, "hlboot.dat")))
        {
            return false;
        }

        result = new HostArguments(processId, fullDirectory);
        return true;
    }

    private static string? Read(
        IReadOnlyList<string> arguments,
        string name)
    {
        for (int index = 0; index + 1 < arguments.Count; index++)
        {
            if (string.Equals(
                    arguments[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }
        return null;
    }
}
