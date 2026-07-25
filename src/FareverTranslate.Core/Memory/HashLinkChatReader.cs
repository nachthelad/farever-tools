using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FareverTranslate.Core.Diagnostics;
using FareverTranslate.Core.Models;

namespace FareverTranslate.Core.Memory;

public enum ChatReaderStatus
{
    Starting,
    Ready,
    InvalidGame,
    UnsupportedBuild,
    PlayerNotFound,
    ChatNotReady,
}

public sealed class HashLinkChatReader : IDisposable
{
    public const string SupportedHlbootHash =
        "7C3CA4DD765B50972B0EB524F82F9943A1D24D9EA52CBBA97C7F87D5D840E7A0";

    private static readonly string[] TextFields =
        ["text", "message", "msg", "content", "messageText", "msgText"];
    private static readonly string[] SenderFields =
        ["sender", "player", "from", "author", "source"];
    private static readonly string[] NameFields =
        ["name", "playerName", "displayName", "nickname"];
    private static readonly string[] ChannelFields =
        ["channel", "channelId", "type"];
    private static readonly string[] ContainerFields =
        ["array", "content", "items", "data", "values"];
    private static readonly TimeSpan MissingObjectRetryDelay =
        TimeSpan.FromSeconds(2);

    private readonly int processId;
    private readonly string gameDirectory;
    private readonly AppLogger logger;
    private readonly Func<DateTimeOffset> utcNow;
    private readonly ChatRecoveryWatchdog recoveryWatchdog = new();
    private IProcessMemory? memory;
    private HashLinkMetadataResolver? resolver;
    private ResolvedHashLinkType? playerType;
    private ResolvedHashLinkType? chatClientType;
    private long localPlayer;
    private long chatClient;
    private string localPlayerName = string.Empty;
    private DateTimeOffset nextObjectSearch;

    public HashLinkChatReader(
        int processId,
        string gameDirectory,
        AppLogger logger)
    {
        this.processId = processId;
        this.gameDirectory = gameDirectory;
        this.logger = logger;
        utcNow = () => DateTimeOffset.UtcNow;
    }

    internal HashLinkChatReader(
        IProcessMemory memory,
        AppLogger logger,
        Func<DateTimeOffset>? utcNow = null)
    {
        processId = 0;
        gameDirectory = string.Empty;
        this.logger = logger;
        this.memory = memory;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public ChatReaderStatus Status { get; private set; } = ChatReaderStatus.Starting;
    public string Diagnostic { get; private set; } = "Inicializando";

    public bool Initialize()
    {
        try
        {
            Process process = Process.GetProcessById(processId);
            if (process.HasExited
                || !string.Equals(
                    process.ProcessName,
                    "Farever",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Fail(ChatReaderStatus.InvalidGame, "Proceso inválido");
            }
            string? processDirectory = Path.GetDirectoryName(
                process.MainModule?.FileName);
            if (string.IsNullOrWhiteSpace(processDirectory)
                || !string.Equals(
                    Path.GetFullPath(processDirectory)
                        .TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(gameDirectory)
                        .TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Fail(
                    ChatReaderStatus.InvalidGame,
                    "El proceso no pertenece a la carpeta indicada");
            }

            string hlboot = Path.Combine(gameDirectory, "hlboot.dat");
            if (!IsSupportedBuild(hlboot))
            {
                return Fail(
                    ChatReaderStatus.UnsupportedBuild,
                    "Versión de Farever no compatible");
            }

            memory = new ProcessMemory(processId);
            return ResolveMetadata();
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            logger.Error("reader_initialize_failed", exception);
            return Fail(
                ChatReaderStatus.UnsupportedBuild,
                "No se pudo inicializar el lector");
        }
    }

    public IReadOnlyList<ChatMessageSnapshot> ReadRecent(int maximumMessages)
    {
        if (memory is null
            || resolver is null
            || playerType is null
            || chatClientType is null)
        {
            return Array.Empty<ChatMessageSnapshot>();
        }

        try
        {
            DateTimeOffset now = utcNow();
            if (!EnsureObjects(now))
            {
                ApplyRecovery(now, recognizedMessage: false);
                return Array.Empty<ChatMessageSnapshot>();
            }

            ResolvedHashLinkField historyField = chatClientType.Fields["history"];
            if (!memory.TryReadPointer(
                    chatClient + historyField.Offset,
                    out long history)
                || history == 0)
            {
                Status = ChatReaderStatus.ChatNotReady;
                Diagnostic = "El chat todavía no está disponible";
                ApplyRecovery(now, recognizedMessage: false);
                return Array.Empty<ChatMessageSnapshot>();
            }

            IReadOnlyList<long> entries = ReadContainer(
                history,
                historyField.TypeAddress,
                depth: 0);
            if (entries.Count == 0)
            {
                Status = ChatReaderStatus.Ready;
                Diagnostic = "Chat conectado";
                ApplyRecovery(now, recognizedMessage: false);
                return Array.Empty<ChatMessageSnapshot>();
            }

            var messages = new List<ChatMessageSnapshot>();
            bool recognizedMessage = false;
            for (int index = Math.Max(0, entries.Count - maximumMessages * 3);
                 index < entries.Count;
                 index++)
            {
                if (TryReadMessage(entries[index], out ChatMessageSnapshot? message)
                    && message is not null)
                {
                    recognizedMessage = true;
                    if (!message.IsSelf && !message.IsSystem)
                    {
                        messages.Add(message);
                    }
                }
            }

            Status = ChatReaderStatus.Ready;
            Diagnostic = "Chat conectado";
            ApplyRecovery(now, recognizedMessage);
            return messages
                .DistinctBy(message => message.Identity)
                .TakeLast(maximumMessages)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or ArgumentOutOfRangeException
            or OverflowException)
        {
            logger.Error("reader_sample_discarded", exception);
            localPlayer = 0;
            chatClient = 0;
            Status = ChatReaderStatus.ChatNotReady;
            Diagnostic = "Reconectando lector del chat…";
            return Array.Empty<ChatMessageSnapshot>();
        }
    }

    public string DescribeLayout()
    {
        if (playerType is null || chatClientType is null)
        {
            return "metadata=unavailable";
        }
        string playerFields = string.Join(
            ',',
            playerType.Fields.Values.Select(
                field => $"{field.Name}:{field.TypeKind}@{field.Offset}"));
        string chatFields = string.Join(
            ',',
            chatClientType.Fields.Values.Select(
                field => $"{field.Name}:{field.TypeKind}@{field.Offset}"));
        string historyDescription = DescribeHistoryLayout();
        return $"player=[{playerFields}] chat=[{chatFields}] {historyDescription}";
    }

    private string DescribeHistoryLayout()
    {
        if (memory is null
            || resolver is null
            || chatClientType is null
            || chatClient == 0
            || !chatClientType.Fields.TryGetValue(
                "history",
                out ResolvedHashLinkField? historyField)
            || !memory.TryReadPointer(
                chatClient + historyField.Offset,
                out long history)
            || history == 0
            || !memory.TryReadPointer(history, out long historyTypeAddress))
        {
            return "history=unavailable";
        }
        ResolvedHashLinkType? historyType =
            resolver.ResolveByAddress(historyTypeAddress);
        if (historyType is null)
        {
            return $"history=0x{history:X}/type=0x{historyTypeAddress:X}";
        }
        string fields = string.Join(
            ',',
            historyType.Fields.Values.Select(
                field => $"{field.Name}:{field.TypeKind}@{field.Offset}"));
        string arrayDetails = string.Empty;
        if (historyType.Fields.TryGetValue(
                "length",
                out ResolvedHashLinkField? lengthField)
            && historyType.Fields.TryGetValue(
                "array",
                out ResolvedHashLinkField? arrayField)
            && memory.TryReadInt32(
                history + lengthField.Offset,
                out int logicalLength)
            && memory.TryReadPointer(
                history + arrayField.Offset,
                out long array)
            && memory.TryReadBytes(array, 24, out byte[] arrayHeader))
        {
            long elementType = BitConverter.ToInt64(arrayHeader, 8);
            int capacity = BitConverter.ToInt32(arrayHeader, 16);
            arrayDetails =
                $" length={logicalLength} capacity={capacity}" +
                $" element={ReadTypeDisplay(elementType)}";
        }
        IReadOnlyList<long> entries = ReadContainer(
            history,
            historyField.TypeAddress,
            depth: 0);
        string entryTypes = string.Join(
            ',',
            entries.Take(12).Select(entry =>
                memory.TryReadPointer(entry, out long entryType)
                    ? ReadTypeDisplay(entryType)
                    : "unreadable"));
        return $"history={historyType.Name}[{fields}]{arrayDetails}" +
            $" entries={entries.Count}[{entryTypes}]";
    }

    private string ReadTypeDisplay(long typeAddress)
    {
        if (resolver is null)
        {
            return "unknown";
        }
        ResolvedHashLinkType? objectType =
            resolver.ResolveByAddress(typeAddress);
        return objectType?.Name
            ?? resolver.ReadKind(typeAddress).ToString();
    }

    public void Dispose()
    {
        memory?.Dispose();
        memory = null;
    }

    public static bool IsSupportedBuild(string hlbootPath)
    {
        if (!File.Exists(hlbootPath))
        {
            return false;
        }
        using FileStream stream = File.OpenRead(hlbootPath);
        return string.Equals(
            Convert.ToHexString(SHA256.HashData(stream)),
            SupportedHlbootHash,
            StringComparison.OrdinalIgnoreCase);
    }

    private bool LocateObjects()
    {
        if (memory is null || playerType is null || chatClientType is null)
        {
            return false;
        }

        ResolvedHashLinkField isMeField = playerType.Fields["isMe"];
        ResolvedHashLinkField chatField = playerType.Fields["chatClient"];
        long locatedChat = 0;
        if (!memory.TryFindPointer(
                playerType.TypeAddress,
                writableOnly: true,
                largestRegionsFirst: true,
                possiblePlayer =>
                {
                    if (!memory.TryReadByte(
                            possiblePlayer + isMeField.Offset,
                            out byte isMe)
                        || isMe == 0
                        || !memory.TryReadPointer(
                            possiblePlayer + chatField.Offset,
                            out long possibleChat)
                        || possibleChat == 0
                        || !memory.TryReadPointer(
                            possibleChat,
                            out long possibleChatType)
                        || possibleChatType != chatClientType.TypeAddress)
                    {
                        return false;
                    }
                    locatedChat = possibleChat;
                    return true;
                },
                out long locatedPlayer))
        {
            return Fail(
                ChatReaderStatus.PlayerNotFound,
                "Esperando al personaje");
        }

        localPlayer = locatedPlayer;
        chatClient = locatedChat;
        localPlayerName = ReadNestedString(
            localPlayer,
            playerType,
            NameFields,
            depth: 0) ?? string.Empty;
        Status = ChatReaderStatus.Ready;
        Diagnostic = "Chat conectado";
        logger.Info("chat_objects_located");
        return true;
    }

    private bool EnsureObjects(DateTimeOffset now)
    {
        if (ValidateObjects())
        {
            return true;
        }
        if (RefreshKnownReferences(logResult: true))
        {
            return true;
        }
        return RelocateObjects(now, forcedByWatchdog: false);
    }

    private bool RefreshKnownReferences(bool logResult)
    {
        if (memory is null
            || playerType is null
            || chatClientType is null
            || localPlayer == 0
            || !memory.TryReadPointer(
                localPlayer,
                out long currentPlayerType)
            || currentPlayerType != playerType.TypeAddress
            || !memory.TryReadByte(
                localPlayer + playerType.Fields["isMe"].Offset,
                out byte isMe)
            || isMe == 0
            || !memory.TryReadPointer(
                localPlayer + playerType.Fields["chatClient"].Offset,
                out long currentChat)
            || currentChat == 0
            || !memory.TryReadPointer(
                currentChat,
                out long currentChatType)
            || currentChatType != chatClientType.TypeAddress)
        {
            return false;
        }

        bool changed = currentChat != chatClient;
        chatClient = currentChat;
        Status = ChatReaderStatus.Ready;
        Diagnostic = "Chat conectado";
        if (logResult)
        {
            logger.Info(
                changed
                    ? "chat_references_reconnected"
                    : "chat_references_refreshed");
        }
        return true;
    }

    private bool RelocateObjects(
        DateTimeOffset now,
        bool forcedByWatchdog)
    {
        if (!forcedByWatchdog && now < nextObjectSearch)
        {
            Status = ChatReaderStatus.PlayerNotFound;
            Diagnostic = "Reconectando lector del chat…";
            return false;
        }

        nextObjectSearch = now + MissingObjectRetryDelay;
        localPlayer = 0;
        chatClient = 0;
        localPlayerName = string.Empty;
        bool located = LocateObjects();
        if (located)
        {
            nextObjectSearch = default;
        }
        return located;
    }

    private void ApplyRecovery(
        DateTimeOffset now,
        bool recognizedMessage)
    {
        ChatRecoveryAction action = recoveryWatchdog.Observe(
            now,
            recognizedMessage);
        switch (action)
        {
            case ChatRecoveryAction.RefreshReferences:
                logger.Info("chat_watchdog_refresh");
                RefreshKnownReferences(logResult: false);
                break;
            case ChatRecoveryAction.RelocateObjects:
                logger.Info("chat_watchdog_relocate");
                Diagnostic = "Reconectando lector del chat…";
                RelocateObjects(now, forcedByWatchdog: true);
                break;
        }
    }

    private bool ValidateObjects()
    {
        if (memory is null
            || playerType is null
            || chatClientType is null
            || localPlayer == 0
            || chatClient == 0)
        {
            return false;
        }
        return memory.TryReadPointer(localPlayer, out long currentPlayerType)
            && currentPlayerType == playerType.TypeAddress
            && memory.TryReadByte(
                localPlayer + playerType.Fields["isMe"].Offset,
                out byte isMe)
            && isMe != 0
            && memory.TryReadPointer(chatClient, out long currentChatType)
            && currentChatType == chatClientType.TypeAddress
            && memory.TryReadPointer(
                localPlayer + playerType.Fields["chatClient"].Offset,
                out long currentChat)
            && currentChat == chatClient;
    }

    private IReadOnlyList<long> ReadContainer(
        long container,
        long declaredType,
        int depth)
    {
        if (memory is null || resolver is null || depth > 4 || container == 0)
        {
            return Array.Empty<long>();
        }

        HashLinkTypeKind declaredKind = resolver.ReadKind(declaredType);
        if (declaredKind == HashLinkTypeKind.Array
            || LooksLikeArray(container))
        {
            return ReadPointerArray(container);
        }

        if (!memory.TryReadPointer(container, out long runtimeType))
        {
            return Array.Empty<long>();
        }
        ResolvedHashLinkType? objectType = resolver.ResolveByAddress(runtimeType);
        if (objectType is null)
        {
            return Array.Empty<long>();
        }

        if (objectType.Fields.TryGetValue(
                "length",
                out ResolvedHashLinkField? lengthField)
            && objectType.Fields.TryGetValue(
                "array",
                out ResolvedHashLinkField? arrayField)
            && memory.TryReadInt32(
                container + lengthField.Offset,
                out int logicalLength)
            && memory.TryReadPointer(
                container + arrayField.Offset,
                out long backingArray)
            && logicalLength >= 0)
        {
            return ReadPointerArray(backingArray, logicalLength);
        }

        foreach (string fieldName in ContainerFields)
        {
            if (!objectType.Fields.TryGetValue(
                    fieldName,
                    out ResolvedHashLinkField? field)
                || !memory.TryReadPointer(
                    GetFieldValueAddress(container, field),
                    out long nested)
                || nested == 0)
            {
                continue;
            }
            IReadOnlyList<long> values = ReadContainer(
                nested,
                field.TypeAddress,
                depth + 1);
            if (values.Count > 0)
            {
                return values;
            }
        }

        if (objectType.Fields.TryGetValue(
                "h",
                out ResolvedHashLinkField? headField)
            && memory.TryReadPointer(
                GetFieldValueAddress(container, headField),
                out long head)
            && head != 0)
        {
            return ReadLinkedList(head);
        }
        return Array.Empty<long>();
    }

    private bool LooksLikeArray(long value)
    {
        if (memory is null || resolver is null)
        {
            return false;
        }
        return memory.TryReadPointer(value, out long type)
            && resolver.ReadKind(type) == HashLinkTypeKind.Array;
    }

    private IReadOnlyList<long> ReadPointerArray(
        long array,
        int? logicalCount = null)
    {
        if (memory is null
            || resolver is null
            || !memory.TryReadBytes(array, 24, out byte[] header))
        {
            return Array.Empty<long>();
        }

        long elementType = BitConverter.ToInt64(header, 8);
        int capacity = BitConverter.ToInt32(header, 16);
        int size = logicalCount.HasValue
            ? Math.Min(logicalCount.Value, capacity)
            : capacity;
        if (size < 0 || size > 4096)
        {
            return Array.Empty<long>();
        }
        if (size == 0)
        {
            return Array.Empty<long>();
        }

        HashLinkTypeKind elementKind = resolver.ReadKind(elementType);
        const int elementSize = 8;
        if (!memory.TryReadBytes(
                array + 24,
                checked(size * elementSize),
                out byte[] data))
        {
            return Array.Empty<long>();
        }

        var result = new List<long>(size);
        for (int index = 0; index < size; index++)
        {
            int offset = index * elementSize;
            long pointer = BitConverter.ToInt64(data, offset);
            if (elementKind == HashLinkTypeKind.Dynamic)
            {
                if (!ProcessMemory.IsUserAddress(pointer)
                    || !memory.TryReadPointer(pointer, out long valueType))
                {
                    continue;
                }
                HashLinkTypeKind valueKind = resolver.ReadKind(
                    valueType);
                if (valueKind is not (
                    HashLinkTypeKind.Object
                    or HashLinkTypeKind.Array
                    or HashLinkTypeKind.Enum
                    or HashLinkTypeKind.Virtual
                    or HashLinkTypeKind.DynamicObject
                    or HashLinkTypeKind.Struct
                    or HashLinkTypeKind.Null))
                {
                    continue;
                }
            }
            if (ProcessMemory.IsUserAddress(pointer))
            {
                result.Add(pointer);
            }
        }
        return result;
    }

    private IReadOnlyList<long> ReadLinkedList(long head)
    {
        var result = new List<long>();
        var seen = new HashSet<long>();
        long current = head;
        while (current != 0 && result.Count < 4096 && seen.Add(current))
        {
            IReadOnlyList<long> node = ReadPointerArray(current);
            if (node.Count < 2)
            {
                break;
            }
            result.Add(node[0]);
            current = node[1];
        }
        return result;
    }

    private bool TryReadMessage(
        long entry,
        out ChatMessageSnapshot? message)
    {
        message = null;
        if (memory is null
            || resolver is null
            || !memory.TryReadPointer(entry, out long typeAddress))
        {
            return false;
        }
        ResolvedHashLinkType? entryType = resolver.ResolveByAddress(typeAddress);
        if (entryType is null)
        {
            return false;
        }

        string? text = ReadNestedString(entry, entryType, TextFields, depth: 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        long senderPointer = ReadFirstPointer(entry, entryType, SenderFields);
        string sender = senderPointer == localPlayer
            ? localPlayerName
            : ReadNestedString(entry, entryType, SenderFields, depth: 0)
                ?? string.Empty;
        string channel = ReadFieldDisplay(
            entry,
            entryType,
            ChannelFields) ?? string.Empty;

        bool isSelf = senderPointer == localPlayer
            || (!string.IsNullOrEmpty(localPlayerName)
                && string.Equals(
                    sender,
                    localPlayerName,
                    StringComparison.OrdinalIgnoreCase));
        bool isSystem = channel.Contains(
                "System",
                StringComparison.OrdinalIgnoreCase)
            || sender.Contains(
                "System",
                StringComparison.OrdinalIgnoreCase);

        string contentIdentity = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    sender + "\0" + channel + "\0" + text)));
        message = new ChatMessageSnapshot(
            $"{entry:X16}:{contentIdentity[..16]}",
            string.IsNullOrWhiteSpace(sender) ? "Jugador" : sender,
            string.IsNullOrWhiteSpace(channel) ? "Chat" : channel,
            text.Trim(),
            isSelf,
            isSystem);
        return true;
    }

    private string? ReadFieldDisplay(
        long objectAddress,
        ResolvedHashLinkType type,
        IEnumerable<string> aliases)
    {
        if (memory is null || resolver is null)
        {
            return null;
        }
        foreach (string alias in aliases)
        {
            if (!type.Fields.TryGetValue(alias, out ResolvedHashLinkField? field))
            {
                continue;
            }
            if (field.TypeKind == HashLinkTypeKind.Enum
                && memory.TryReadPointer(
                    GetFieldValueAddress(objectAddress, field),
                    out long enumValue))
            {
                string? enumName = resolver.TryReadEnumName(enumValue);
                if (!string.IsNullOrEmpty(enumName))
                {
                    return enumName;
                }
            }
            string? nested = ReadNestedString(
                objectAddress,
                type,
                [alias],
                depth: 0);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
            if (memory.TryReadInt32(
                    GetFieldValueAddress(objectAddress, field),
                    out int numeric))
            {
                return numeric.ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        return null;
    }

    private string? ReadNestedString(
        long objectAddress,
        ResolvedHashLinkType type,
        IEnumerable<string> aliases,
        int depth)
    {
        if (memory is null || resolver is null || depth > 3)
        {
            return null;
        }
        foreach (string alias in aliases)
        {
            if (!type.Fields.TryGetValue(alias, out ResolvedHashLinkField? field)
                || !TryReadFieldPointer(
                    objectAddress,
                    field,
                    out long value)
                || value == 0)
            {
                continue;
            }
            if (memory.TryReadHashLinkString(value, out string direct)
                && direct.Length > 0)
            {
                return direct;
            }
            if (!memory.TryReadPointer(value, out long nestedTypeAddress))
            {
                continue;
            }
            ResolvedHashLinkType? nestedType =
                resolver.ResolveByAddress(nestedTypeAddress);
            if (nestedType is null)
            {
                continue;
            }
            string? nested = ReadNestedString(
                value,
                nestedType,
                TextFields.Concat(NameFields),
                depth + 1);
            if (!string.IsNullOrEmpty(nested))
            {
                return nested;
            }
        }
        return null;
    }

    private long ReadFirstPointer(
        long objectAddress,
        ResolvedHashLinkType type,
        IEnumerable<string> aliases)
    {
        if (memory is null)
        {
            return 0;
        }
        foreach (string alias in aliases)
        {
            if (type.Fields.TryGetValue(alias, out ResolvedHashLinkField? field)
                && TryReadFieldPointer(
                    objectAddress,
                    field,
                    out long value)
                && ProcessMemory.IsUserAddress(value))
            {
                return value;
            }
        }
        return 0;
    }

    private bool TryReadFieldPointer(
        long objectAddress,
        ResolvedHashLinkField field,
        out long value)
    {
        value = 0;
        return memory is not null
            && memory.TryReadPointer(
                GetFieldValueAddress(objectAddress, field),
                out value);
    }

    private long GetFieldValueAddress(
        long objectAddress,
        ResolvedHashLinkField field)
    {
        long fieldAddress = objectAddress + field.Offset;
        if (!field.IsIndirect
            || memory is null
            || !memory.TryReadPointer(fieldAddress, out long storage))
        {
            return field.IsIndirect ? 0 : fieldAddress;
        }
        return storage;
    }

    private bool Fail(ChatReaderStatus status, string diagnostic)
    {
        Status = status;
        Diagnostic = diagnostic;
        logger.Warn("reader_status", status.ToString());
        return false;
    }

    internal bool InitializeSynthetic() => ResolveMetadata();

    private bool ResolveMetadata()
    {
        if (memory is null)
        {
            return Fail(
                ChatReaderStatus.UnsupportedBuild,
                "Memoria no disponible");
        }

        resolver = new HashLinkMetadataResolver(memory);
        IReadOnlyDictionary<string, ResolvedHashLinkType> resolved =
            resolver.ResolveMany(["st.Player", "st.player.ChatClient"]);
        playerType = resolved.GetValueOrDefault("st.Player");
        chatClientType = resolved.GetValueOrDefault("st.player.ChatClient");
        if (playerType is null)
        {
            logger.Warn("hashlink_type_missing", "st.Player");
            return Fail(
                ChatReaderStatus.UnsupportedBuild,
                "No se encontró st.Player");
        }
        if (chatClientType is null)
        {
            logger.Warn("hashlink_type_missing", "st.player.ChatClient");
            return Fail(
                ChatReaderStatus.UnsupportedBuild,
                "No se encontró st.player.ChatClient");
        }

        string[] missingFields =
        [
            .. new[]
            {
                playerType.Fields.ContainsKey("isMe") ? null : "st.Player.isMe",
                playerType.Fields.ContainsKey("chatClient") ? null : "st.Player.chatClient",
                chatClientType.Fields.ContainsKey("history") ? null : "st.player.ChatClient.history",
            }.Where(field => field is not null).Select(field => field!),
        ];
        if (missingFields.Length > 0)
        {
            logger.Warn(
                "hashlink_fields_missing",
                string.Join(',', missingFields));
            return Fail(
                ChatReaderStatus.UnsupportedBuild,
                "Metadatos de chat incompletos");
        }

        logger.Info(
            "hashlink_metadata_ready",
            $"player_fields={playerType.Fields.Count} chat_fields={chatClientType.Fields.Count}");
        return LocateObjects();
    }
}
