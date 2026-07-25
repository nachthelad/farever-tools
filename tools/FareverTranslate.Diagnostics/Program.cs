using FareverTranslate.Core.Configuration;
using FareverTranslate.Core.Diagnostics;
using FareverTranslate.Core.Memory;
using FareverTranslate.Core.Models;
using System.Diagnostics;
using System.Text;

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
using (var memoryProbe = new ProcessMemory(processId))
{
    ProcessMemoryScanSummary summary = memoryProbe.GetScanSummary(
        writableOnly: true);
    Console.WriteLine(
        $"scan_regions={summary.RegionCount} " +
        $"scan_megabytes={summary.TotalBytes / (1024 * 1024)} " +
        $"largest_megabytes={summary.LargestRegionBytes / (1024 * 1024)} " +
        $"small={summary.SmallRegionCount}/{summary.SmallRegionBytes / (1024 * 1024)}MB " +
        $"medium={summary.MediumRegionCount}/{summary.MediumRegionBytes / (1024 * 1024)}MB " +
        $"large={summary.LargeRegionCount}/{summary.LargeRegionBytes / (1024 * 1024)}MB");
    bool hasTypes = memoryProbe.TryGetHashLinkTypeTable(
        out long typesAddress,
        out int typeCount);
    Console.WriteLine(
        $"type_table={hasTypes} address=0x{typesAddress:X} count={typeCount}");
}
if (args.Contains("--summary", StringComparer.OrdinalIgnoreCase))
{
    return 0;
}
if (args.Contains("--dump-type-names", StringComparer.OrdinalIgnoreCase))
{
    using var probe = new ProcessMemory(processId);
    if (!probe.TryGetHashLinkTypeTable(
            out long typesAddress,
            out int typeCount)
        || !probe.TryReadBytes(
            typesAddress,
            checked(typeCount * 32),
            out byte[] types))
    {
        Console.WriteLine("types=unavailable");
        return 1;
    }
    int matches = 0;
    for (int index = 0; index < typeCount; index++)
    {
        int offset = index * 32;
        if (BitConverter.ToInt32(types, offset)
            != (int)HashLinkTypeKind.Object)
        {
            continue;
        }
        long objectType = BitConverter.ToInt64(types, offset + 8);
        if (!probe.TryReadPointer(objectType + 16, out long nameAddress))
        {
            continue;
        }
        string? name = probe.ReadNativeString(nameAddress);
        if (name is not null
            && (name.Contains("Player", StringComparison.OrdinalIgnoreCase)
                || name.Contains(
                    "ChatClient",
                    StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"type[{index}]={name}");
            if (name is "st.Player" or "st.player.ChatClient"
                && probe.TryReadBytes(objectType, 88, out byte[] objectHeader))
            {
                long runtime72 = BitConverter.ToInt64(objectHeader, 72);
                long runtime80 = BitConverter.ToInt64(objectHeader, 80);
                string runtimeDetails = "unreadable";
                if (probe.TryReadBytes(
                        runtime72,
                        48,
                        out byte[] runtimeHeader))
                {
                    (int declaredFields, int readableFields) =
                        CountObjectFields(probe, objectType);
                    runtimeDetails =
                        $"nfields={BitConverter.ToInt32(runtimeHeader, 8)} " +
                        $"indexes=0x{BitConverter.ToInt64(runtimeHeader, 40):X} " +
                        $"declared={declaredFields} readable={readableFields}";
                }
                Console.WriteLine(
                    $"  object=0x{objectType:X} " +
                    $"own={BitConverter.ToInt32(objectHeader, 0)} " +
                    $"super=0x{BitConverter.ToInt64(objectHeader, 24):X} " +
                    $"fields=0x{BitConverter.ToInt64(objectHeader, 32):X} " +
                    $"m64=0x{BitConverter.ToInt64(objectHeader, 64):X} " +
                    $"rt72=0x{runtime72:X} rt80=0x{runtime80:X} " +
                    runtimeDetails);
            }
            matches++;
        }
    }
    Console.WriteLine($"matching_types={matches}");
    return 0;
}
if (args.Contains("--probe-patterns", StringComparer.OrdinalIgnoreCase))
{
    using var probe = new ProcessMemory(processId);
    foreach ((string label, byte[] pattern) in new[]
    {
        ("unicode", Encoding.Unicode.GetBytes("st.Player\0")),
        ("utf8", Encoding.UTF8.GetBytes("st.Player\0")),
    })
    {
        var watch = Stopwatch.StartNew();
        IReadOnlyList<long> matches = probe.FindPattern(
            pattern,
            writableOnly: true,
            maximumResults: 1);
        watch.Stop();
        Console.WriteLine(
            $"pattern={label} count={matches.Count} elapsed_ms={watch.ElapsedMilliseconds}");
    }
    return 0;
}
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

static (int Declared, int Readable) CountObjectFields(
    ProcessMemory memory,
    long objectType)
{
    int declared = 0;
    int readable = 0;
    var seen = new HashSet<long>();
    long current = objectType;
    while (current != 0 && seen.Add(current) && seen.Count < 64)
    {
        if (!memory.TryReadInt32(current, out int own)
            || own is < 0 or > 4096
            || !memory.TryReadPointer(current + 32, out long fields))
        {
            break;
        }
        declared += own;
        for (int field = 0; field < own; field++)
        {
            if (memory.TryReadPointer(
                    fields + field * 24L,
                    out long fieldName)
                && !string.IsNullOrEmpty(
                    memory.ReadNativeString(fieldName)))
            {
                readable++;
            }
        }
        if (!memory.TryReadPointer(current + 24, out long super)
            || super == 0
            || !memory.TryReadPointer(super + 8, out current))
        {
            break;
        }
    }
    return (declared, readable);
}
