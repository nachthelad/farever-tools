using FareverTranslate.Core.Configuration;
using FareverTranslate.Core.Diagnostics;
using FareverTranslate.Core.Memory;
using FareverTranslate.Core.Models;

if (!TryGetArgument(args, "--pid", out string? pidValue)
    || !int.TryParse(pidValue, out int processId)
    || !TryGetArgument(args, "--game-dir", out string? gameDirectory)
    || string.IsNullOrWhiteSpace(gameDirectory)
    || !Path.IsPathFullyQualified(gameDirectory))
{
    Console.Error.WriteLine(
        "Uso: FareverTranslate.Diagnostics --pid <pid> --game-dir <ruta-absoluta>");
    return 2;
}

string diagnosticRoot = Path.Combine(
    Path.GetTempPath(),
    "FareverTranslate",
    "diagnostics");
var logger = new AppLogger(new AppPaths(diagnosticRoot));
using var reader = new HashLinkChatReader(processId, gameDirectory, logger);
bool initialized = reader.Initialize();
Console.WriteLine($"initialized={initialized}");
Console.WriteLine($"status={reader.Status}");
Console.WriteLine($"diagnostic={reader.Diagnostic}");
Console.WriteLine(reader.DescribeLayout());

if (!initialized)
{
    return 1;
}

for (int sample = 0; sample < 10; sample++)
{
    IReadOnlyList<ChatMessageSnapshot> messages = reader.ReadRecent(6);
    Console.WriteLine(
        $"sample={sample} status={reader.Status} count={messages.Count} " +
        $"shapes=[{string.Join(',', messages.Select(
            message => $"{message.Sender.Length}/{message.Channel.Length}/{message.Text.Length}"))}]");
    await Task.Delay(100);
}

return 0;

static bool TryGetArgument(
    IReadOnlyList<string> arguments,
    string name,
    out string? value)
{
    for (int index = 0; index + 1 < arguments.Count; index++)
    {
        if (string.Equals(
                arguments[index],
                name,
                StringComparison.OrdinalIgnoreCase))
        {
            value = arguments[index + 1];
            return true;
        }
    }

    value = null;
    return false;
}
