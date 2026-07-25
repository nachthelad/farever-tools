using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FareverTranslate.Core.Configuration;
using FareverTranslate.Core.Diagnostics;
using FareverTranslate.Core.Memory;
using FareverTranslate.Core.Models;
using FareverTranslate.Core.Security;
using FareverTranslate.Core.Translation;
using FareverTranslate.Host;
using FareverTranslate.Tests;

if (args.Length == 3 && args[0] == "--proxy-case")
{
    return RunProxyCase(args[1], bool.Parse(args[2]));
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("HashLink sintético", TestSyntheticHashLink),
    ("Watchdog del chat", TestChatRecoveryWatchdog),
    ("Toggle F6 por flanco y foco", TestForegroundKeyToggle),
    ("Build desconocido", TestUnsupportedBuild),
    ("Google success/autodetect", TestGoogleSuccess),
    ("Google errores y reintentos", TestGoogleErrors),
    ("DPAPI credencial y caché", TestProtectedStorage),
    ("Posición DPI", TestOverlayPlacement),
    ("Filtrado de mensajes descartados", TestDismissedMessages),
    ("Fila compacta y altura", TestCompactMessageRow),
    ("Proxy y cinco exports", TestProxy),
};

int failed = 0;
foreach ((string name, Func<Task> run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine(
            $"FAIL {name}: {exception.GetType().Name} {exception.Message}");
    }
}
Console.WriteLine($"TOTAL {tests.Length - failed}/{tests.Length}");
return failed == 0 ? 0 : 1;

static Task TestSyntheticHashLink()
{
    string logRoot = Path.Combine(
        Path.GetTempPath(),
        "FareverTranslate.Tests",
        Guid.NewGuid().ToString("N"));
    var fixture = new SyntheticChatFixture();
    try
    {
        using var reader = new HashLinkChatReader(
            fixture.Memory,
            new AppLogger(new AppPaths(logRoot)));
        Assert(reader.InitializeSynthetic(), reader.Diagnostic);
        IReadOnlyList<ChatMessageSnapshot> first = reader.ReadRecent(6);
        Assert(first.Count == 1, "No filtró duplicados, mensajes propios, sistema o punteros inválidos.");
        Assert(
            first[0].Text == "Vocês vão para a floresta amanhã?"
            && first[0].Sender == "João",
            "No preservó Unicode portugués.");

        fixture.ReconnectWith("Chegamos depois da mudança de zona.");
        int searchesBeforeReconnect = fixture.Memory.PointerSearches;
        IReadOnlyList<ChatMessageSnapshot> afterReconnect = reader.ReadRecent(6);
        Assert(
            afterReconnect.Count == 1
            && afterReconnect[0].Text.Contains("mudança", StringComparison.Ordinal),
            "No siguió el cambio de chatClient.");
        Assert(
            fixture.Memory.PointerSearches == searchesBeforeReconnect,
            "Una reconexión conocida provocó una búsqueda completa innecesaria.");
    }
    finally
    {
        if (Directory.Exists(logRoot))
        {
            Directory.Delete(logRoot, recursive: true);
        }
    }
    return Task.CompletedTask;
}

static Task TestChatRecoveryWatchdog()
{
    var watchdog = new ChatRecoveryWatchdog();
    DateTimeOffset start = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
    Assert(
        watchdog.Observe(start, recognizedMessage: false)
            == ChatRecoveryAction.None,
        "El watchdog actuó antes de iniciar la espera.");
    Assert(
        watchdog.Observe(
            start + TimeSpan.FromSeconds(5),
            recognizedMessage: false)
            == ChatRecoveryAction.RefreshReferences,
        "No refrescó referencias a los cinco segundos.");
    Assert(
        watchdog.Observe(
            start + TimeSpan.FromSeconds(10),
            recognizedMessage: false)
            == ChatRecoveryAction.RefreshReferences,
        "No repitió el refresh liviano.");
    Assert(
        watchdog.Observe(
            start + TimeSpan.FromSeconds(15),
            recognizedMessage: false)
            == ChatRecoveryAction.RelocateObjects,
        "No relocalizó a los quince segundos.");
    Assert(
        watchdog.Observe(
            start + TimeSpan.FromSeconds(45),
            recognizedMessage: false)
            == ChatRecoveryAction.RelocateObjects,
        "No aplicó el backoff de treinta segundos.");
    Assert(
        watchdog.Observe(
            start + TimeSpan.FromSeconds(105),
            recognizedMessage: false)
            == ChatRecoveryAction.RelocateObjects,
        "No limitó el backoff a sesenta segundos.");
    Assert(
        watchdog.Observe(
            start + TimeSpan.FromSeconds(106),
            recognizedMessage: true)
            == ChatRecoveryAction.None,
        "Un mensaje válido no reinició el watchdog.");
    Assert(
        watchdog.Observe(
            start + TimeSpan.FromSeconds(110),
            recognizedMessage: false)
            == ChatRecoveryAction.None,
        "El watchdog no reinició su ventana vacía.");
    return Task.CompletedTask;
}

static Task TestForegroundKeyToggle()
{
    var toggle = new ForegroundKeyToggle();
    Assert(
        !toggle.Update(isDown: true, targetIsForeground: false),
        "F6 actuó fuera de Farever.");
    Assert(
        !toggle.Update(isDown: true, targetIsForeground: true),
        "Mantener F6 al cambiar de foco produjo un toggle.");
    Assert(
        !toggle.Update(isDown: false, targetIsForeground: true),
        "Soltar F6 produjo un toggle.");
    Assert(
        toggle.Update(isDown: true, targetIsForeground: true),
        "F6 no alternó con Farever al frente.");
    Assert(
        !toggle.Update(isDown: true, targetIsForeground: true),
        "Mantener F6 alternó repetidamente.");
    return Task.CompletedTask;
}

static Task TestUnsupportedBuild()
{
    string file = Path.GetTempFileName();
    try
    {
        File.WriteAllText(file, "unknown build");
        Assert(
            !HashLinkChatReader.IsSupportedBuild(file),
            "Un build desconocido no fue rechazado.");
    }
    finally
    {
        File.Delete(file);
    }
    return Task.CompletedTask;
}

static async Task TestGoogleSuccess()
{
    string? requestBody = null;
    var handler = new StubHandler(async request =>
    {
        requestBody = await request.Content!.ReadAsStringAsync();
        return Json(
            HttpStatusCode.OK,
            """
            {"data":{"translations":[{"translatedText":"¿Cómo están?","detectedSourceLanguage":"pt"}]}}
            """);
    });
    using var http = new HttpClient(handler);
    var client = new GoogleTranslationClient(http);
    TranslationOutcome result = await client.TranslateAsync(
        "Como vocês estão?",
        "es",
        "test-key",
        CancellationToken.None);
    Assert(result.Success, result.UserMessage);
    Assert(result.TranslatedText == "¿Cómo están?", "Texto traducido incorrecto.");
    Assert(result.DetectedLanguage == "pt", "No conservó la autodetección.");
    Assert(requestBody is not null && !requestBody.Contains("\"source\""), "Se envió source.");
    Assert(requestBody!.Contains("\"target\":\"es\""), "Destino incorrecto.");
}

static async Task TestGoogleErrors()
{
    foreach ((HttpStatusCode status, TranslationErrorKind kind) in new[]
    {
        (HttpStatusCode.Unauthorized, TranslationErrorKind.InvalidCredential),
        (HttpStatusCode.Forbidden, TranslationErrorKind.InvalidCredential),
        (HttpStatusCode.TooManyRequests, TranslationErrorKind.QuotaExceeded),
    })
    {
        var handler = new StubHandler(_ => Task.FromResult(
            Json(status, "{}")));
        using var http = new HttpClient(handler);
        TranslationOutcome result = await new GoogleTranslationClient(http)
            .TranslateAsync("oi", "es", "key", CancellationToken.None);
        Assert(result.ErrorKind == kind, $"Clasificación incorrecta para {(int)status}.");
        Assert(handler.Calls == 1, $"No debía reintentar {(int)status}.");
    }

    int attempt = 0;
    var retry = new StubHandler(_ => Task.FromResult(
        retryResult()));
    HttpResponseMessage retryResult() =>
        ++attempt == 1
            ? Json(HttpStatusCode.ServiceUnavailable, "{}")
            : Json(
                HttpStatusCode.OK,
                """{"data":{"translations":[{"translatedText":"hola","detectedSourceLanguage":"pt"}]}}""");
    using (var http = new HttpClient(retry))
    {
        TranslationOutcome result = await new GoogleTranslationClient(http)
            .TranslateAsync("oi", "es", "key", CancellationToken.None);
        Assert(result.Success && retry.Calls == 2, "No reintentó una sola vez el 5xx.");
    }

    var timeout = new StubHandler(_ =>
        throw new TaskCanceledException("synthetic timeout"));
    using (var http = new HttpClient(timeout))
    {
        TranslationOutcome result = await new GoogleTranslationClient(http)
            .TranslateAsync("oi", "es", "key", CancellationToken.None);
        Assert(
            result.ErrorKind == TranslationErrorKind.Network
            && timeout.Calls == 2,
            "Timeout no clasificado/reintentado correctamente.");
    }
}

static Task TestProtectedStorage()
{
    string root = Path.Combine(
        Path.GetTempPath(),
        "FareverTranslate.Tests",
        Guid.NewGuid().ToString("N"));
    try
    {
        var paths = new AppPaths(root);
        var secrets = new SecretStore(paths);
        const string apiKey = "super-secret-test-key";
        secrets.SaveApiKey(apiKey);
        Assert(secrets.TryLoadApiKey(out string loaded) && loaded == apiKey, "DPAPI no restauró la clave.");
        Assert(
            !Encoding.UTF8.GetString(File.ReadAllBytes(paths.CredentialFile)).Contains(apiKey),
            "La clave quedó en texto plano.");

        var cache = new TranslationCache(paths);
        cache.Put("olá mundo", "hola mundo", "pt");
        Assert(
            cache.TryGet("olá mundo", out CachedTranslation? translated)
            && translated?.TranslatedText == "hola mundo",
            "El caché no restauró la traducción.");
        Assert(
            !Encoding.UTF8.GetString(File.ReadAllBytes(paths.CacheFile)).Contains("olá mundo"),
            "El caché quedó en texto plano.");

        for (int index = 0; index < 510; index++)
        {
            cache.Put($"mensagem {index}", $"mensaje {index}", "pt");
        }
        byte[] decryptedCache = DpapiProtector.Unprotect(
            File.ReadAllBytes(paths.CacheFile));
        using (JsonDocument document = JsonDocument.Parse(decryptedCache))
        {
            Assert(
                document.RootElement.EnumerateObject().Count() == 500,
                "El caché superó 500 resultados.");
        }

        const string expiredSource = "mensagem antiga";
        string expiredKey = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    expiredSource.Trim().Normalize(NormalizationForm.FormKC))));
        var expired = new Dictionary<string, CachedTranslation>
        {
            [expiredKey] = new(
                "viejo",
                "pt",
                DateTimeOffset.UtcNow - TimeSpan.FromDays(8)),
        };
        File.WriteAllBytes(
            paths.CacheFile,
            DpapiProtector.Protect(
                JsonSerializer.SerializeToUtf8Bytes(expired)));
        Assert(
            !new TranslationCache(paths).TryGet(expiredSource, out _),
            "El caché conservó un resultado vencido.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
    return Task.CompletedTask;
}

static Task TestOverlayPlacement()
{
    foreach (double scale in new[] { 1d, 1.25d, 1.5d })
    {
        OverlayPlacement placement = OverlayPlacement.Calculate(
            gameLeftPixels: (int)(100 * scale),
            gameBottomPixels: (int)(1080 * scale),
            dpiScale: scale,
            panelHeight: 520,
            offsetX: 24,
            offsetFromBottom: 260);
        Assert(
            Math.Abs(placement.Left - 124) < 0.01
            && Math.Abs(placement.Top - 300) < 0.01,
            $"Posición incorrecta a {scale:P0}.");
    }
    return Task.CompletedTask;
}

static Task TestDismissedMessages()
{
    var dismissed = new DismissedChatMessages();
    var first = new ChatMessageSnapshot(
        "entry-1",
        "Titan",
        "All",
        "hola como estas guapo",
        false,
        false);
    var future = first with { Identity = "entry-2" };

    dismissed.Dismiss(first.Identity);
    Assert(
        dismissed.Filter([]).Count == 0
        && dismissed.Filter([first]).Count == 0,
        "Una muestra vacía restauró una aparición descartada.");
    IReadOnlyList<ChatMessageSnapshot> visible =
        dismissed.Filter([first, future]);
    Assert(
        dismissed.Count == 1
        && visible.Count == 1
        && visible[0].Identity == future.Identity,
        "Descartar una aparición ocultó un mensaje futuro o restauró la anterior.");
    return Task.CompletedTask;
}

static Task TestCompactMessageRow()
{
    var snapshot = new ChatMessageSnapshot(
        "entry-1",
        "Titan",
        "All",
        "olá",
        false,
        false);
    var row = new MessageRowViewModel(snapshot);
    Assert(
        row.ButtonText == "T"
        && row.CanTranslate
        && !row.HasTranslation
        && !row.HasStatus,
        "La fila compacta no inició en su estado mínimo.");

    row.Translating = true;
    Assert(
        row.ButtonText == "…" && !row.CanTranslate,
        "La acción compacta no reflejó la traducción en curso.");

    row.Status = "Error temporal.";
    Assert(row.HasStatus, "La fila no expuso su estado de error.");

    string root = Path.Combine(
        Path.GetTempPath(),
        "FareverTranslate.Settings.Tests",
        Guid.NewGuid().ToString("N"));
    try
    {
        var paths = new AppPaths(root);
        paths.EnsureCreated();
        File.WriteAllText(
            paths.SettingsFile,
            """{"PanelHeight":100}""");
        AppSettings settings = new SettingsStore(paths).Load();
        Assert(
            settings.PanelHeight == 150,
            "La configuración no aceptó la nueva altura mínima.");
        Assert(
            new AppSettings().PanelHeight == 200,
            "La altura predeterminada nueva no es compacta.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
    return Task.CompletedTask;
}

static async Task TestProxy()
{
    string proxy = Environment.GetEnvironmentVariable("FAREVER_PROXY_PATH")
        ?? throw new InvalidOperationException("Falta FAREVER_PROXY_PATH.");
    string fake = Environment.GetEnvironmentVariable("FAREVER_FAKE_DINPUT_PATH")
        ?? throw new InvalidOperationException("Falta FAREVER_FAKE_DINPUT_PATH.");

    await RunProxySubprocess(proxy, fake, expectFake: true);
    await RunProxySubprocess(proxy, fake, expectFake: false);
}

static async Task RunProxySubprocess(
    string proxy,
    string fake,
    bool expectFake)
{
    string root = Path.Combine(
        Path.GetTempPath(),
        "FareverTranslate.Proxy.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        string proxyCopy = Path.Combine(root, "dinput8.dll");
        File.Copy(proxy, proxyCopy);
        if (expectFake)
        {
            File.Copy(fake, Path.Combine(root, "farever-minimap.dll"));
        }

        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("No se pudo resolver el ejecutable de pruebas.");
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--proxy-case");
        start.ArgumentList.Add(proxyCopy);
        start.ArgumentList.Add(expectFake.ToString());
        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("No arrancó el subproceso del proxy.");
        await process.WaitForExitAsync();
        string error = await process.StandardError.ReadToEndAsync();
        Assert(process.ExitCode == 0, $"Falló proxy case: {error}");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static int RunProxyCase(string path, bool expectFake)
{
    IntPtr module = NativeLibrary.Load(path);
    try
    {
        string[] names =
        [
            "DirectInput8Create",
            "DllCanUnloadNow",
            "DllGetClassObject",
            "DllRegisterServer",
            "DllUnregisterServer",
        ];
        foreach (string name in names)
        {
            if (!NativeLibrary.TryGetExport(module, name, out _))
            {
                return 10;
            }
        }
        IntPtr function = NativeLibrary.GetExport(module, "DllCanUnloadNow");
        var invoke = Marshal.GetDelegateForFunctionPointer<DllCanUnloadNow>(function);
        int result = invoke();
        bool usedFake = result == unchecked((int)0x12340102);
        return usedFake == expectFake ? 0 : 11;
    }
    finally
    {
        NativeLibrary.Free(module);
    }
}

static HttpResponseMessage Json(HttpStatusCode status, string json) =>
    new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
internal delegate int DllCanUnloadNow();

internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> handler;

    internal StubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        this.handler = handler;
    }

    internal int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Calls++;
        return handler(request);
    }
}
