using SysBot.Base;
using System.Globalization;

namespace HomeLive.DeviceExecutor;

public enum RoutineType
{
    None,
    ReadWrite,
}

public class DeviceState : BotState<RoutineType, SwitchConnectionConfig>
{
    public override void IterateNextRoutine() => CurrentRoutineType = NextRoutineType;
    public override void Initialize() => Resume();
    public override void Pause() => NextRoutineType = RoutineType.None;
    public override void Resume() => NextRoutineType = InitialRoutine;
}

public class DeviceExecutor<T>(DeviceState cfg) : SwitchRoutineExecutor<T>(cfg) where T : DeviceState
{
    private const decimal BotbaseVersion = 2.4m;
    private static ulong BoxStartAddress = 0;

    // ===== PKHeXKR: HOME 새 버전 대응 (테스트) =====
    /// <summary>지원 버전(4.1.0)이 아니어도 연결 (데이터 위치는 OffsetOverride 또는 자동 찾기로).</summary>
    public static bool AllowAnyVersion { get; set; }
    /// <summary>heap 기준 박스 시작 위치 덮어쓰기 (0이면 기본값 HomeDataOffsets.BoxStartOffset).</summary>
    public static ulong OffsetOverride { get; set; }
    /// <summary>연결한 HOME 버전 (예: "4.1.0").</summary>
    public string HomeVersion { get; private set; } = "";

    public override string GetSummary()
    {
        var current = Config.CurrentRoutineType;
        var initial = Config.InitialRoutine;
        if (current == initial)
            return $"{Connection.Label} - {initial}";
        return $"{Connection.Label} - {initial} ({current})";
    }

    public override void SoftStop() => Config.Pause();
    public override Task HardStop() => Task.CompletedTask;

    public override async Task MainLoop(CancellationToken token)
    {
        try
        {
            var botbase = await VerifyBotbaseVersion(token).ConfigureAwait(false);
            Log($"Valid botbase version: {botbase}");
            (var title, var version) = await IsRunningHome(token).ConfigureAwait(false);
            Log($"Valid Title ID ({title})");
            Log($"Valid Home Version ({version})");
            Log("Connection Test OK.");

            if (Config.Connection.Protocol is SwitchProtocol.WiFi)
                SwitchConnection.MaximumTransferSize = HomeDataOffsets.HomeSlotSize * HomeDataOffsets.HomeSlotCount;

            Config.IterateNextRoutine();
        }
        catch (Exception ex)
        {
            Disconnect();
            throw new InvalidOperationException($"{ex.Message}");
        }
    }

    public async Task Connect(CancellationToken token)
    {
        Connection.Connect();
        Log("Initializing connection with console...");
        await InitialStartup(token).ConfigureAwait(false);
    }

    public void Disconnect()
    {
        HardStop();
        BoxStartAddress = 0;
        if(Connection.Connected)
            Connection.Disconnect();
    }

    private async Task<(string, string)> IsRunningHome(CancellationToken token)
    {
        if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
            throw new InvalidOperationException("No remote connection");

        var title = await SwitchConnection.GetTitleID(token).ConfigureAwait(false);
        var version = await SwitchConnection.GetGameInfo("version", token).ConfigureAwait(false);
        HomeVersion = version;
        if (title.Equals(HomeDataOffsets.HomeTitleID) && (HomeDataOffsets.IsKnownVersion(version) || AllowAnyVersion))
            return (title, version);
        else if (!title.Equals(HomeDataOffsets.HomeTitleID)) 
            throw new InvalidOperationException($"Invalid Title ID: {title}. The Pokémon HOME application is not running.");
        else 
            throw new InvalidOperationException($"Invalid Home Version: {version}. The Pokémon HOME application is on a non-supported version.");
    }

    //Thanks Anubis
    //https://github.com/kwsch/SysBot.NET/blob/b26c8c957364efe316573bec4b82e8c5c5a1a60f/SysBot.Pokemon/Actions/PokeRoutineExecutor.cs#L88
    //AGPL v3 License
    public async Task<string> VerifyBotbaseVersion(CancellationToken token)
    {
        if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
            throw new InvalidOperationException("No remote connection");

        var data = await SwitchConnection.GetBotbaseVersion(token).ConfigureAwait(false);
        var version = decimal.TryParse(data, CultureInfo.InvariantCulture, out var v) ? v : 0;
        if (version < BotbaseVersion)
        {
            var protocol = Config.Connection.Protocol;
            var msg = protocol is SwitchProtocol.WiFi ? "sys-botbase" : "usb-botbase";
            msg += $" version is not supported. Expected version {BotbaseVersion} or greater, and current version is {version}. Please download the latest version from: ";
            if (protocol is SwitchProtocol.WiFi)
                msg += "https://github.com/olliz0r/sys-botbase/releases/latest";
            else
                msg += "https://github.com/Koi-3088/usb-botbase/releases/latest";
            throw new Exception(msg);
        }
        return data;
    }

    public async Task<ulong> GetBoxStartOffset(CancellationToken token)
    {
        if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
            throw new InvalidOperationException("No remote connection");

        var start = await SwitchConnection.GetHeapBaseAsync(token).ConfigureAwait(false);

        if (BoxStartAddress == 0)
            BoxStartAddress = start + (OffsetOverride != 0 ? OffsetOverride : HomeDataOffsets.OffsetFor(HomeVersion));

        return BoxStartAddress;
    }

    public async Task<byte[]> ReadBoxData(int box, CancellationToken token)
    {
        if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
            throw new InvalidOperationException("No remote connection");

        Log("Getting Box offset...");
        var boxOffset = HomeDataOffsets.GetBoxOffset(await GetBoxStartOffset(token).ConfigureAwait(false), box);
        Log($"Found offset 0x{boxOffset:X8}");
        Log($"Reading Box {box}...");
        var size = HomeDataOffsets.HomeSlotSize * HomeDataOffsets.HomeSlotCount;
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(boxOffset, size, token).ConfigureAwait(false);
        Log("Done.");
        return data;
    }

    public async Task<byte[]> ReadSlotData(int box, int slot, CancellationToken token)
    {
        if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
            throw new InvalidOperationException("No remote connection");

        Log("Getting Slot offset...");
        var slotOffset = HomeDataOffsets.GetSlotOffset(await GetBoxStartOffset(token).ConfigureAwait(false), box, slot);
        Log($"Found offset 0x{slotOffset:X8}");
        Log($"Reading Box {box}, Slot {slot}...");
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(slotOffset, HomeDataOffsets.HomeSlotSize, token).ConfigureAwait(false);
        Log("Done.");
        return data;
    }

    public async Task<byte[]> ReadRange((int box, int slot) start, (int box, int slot) end, CancellationToken token)
    {
        if (Config.Connection.Protocol is SwitchProtocol.WiFi && !Connection.Connected)
            throw new InvalidOperationException("No remote connection");

        Log($"Reading from Box {start.box}, Slot {start.slot} to Box {end.box}, Slot {end.slot}...");
        var startOffset = HomeDataOffsets.GetSlotOffset(await GetBoxStartOffset(token).ConfigureAwait(false), start.box, start.slot);
        var endOffset = HomeDataOffsets.GetSlotOffset(await GetBoxStartOffset(token).ConfigureAwait(false), end.box, end.slot);
        var size = endOffset - startOffset;
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(startOffset, (int)size, token).ConfigureAwait(false);
        Log("Done.");
        return data;
    }

    /// <summary>
    /// PKHeXKR: HOME 박스 데이터 위치 자동 찾기 (읽기만 함).
    /// 칸 머리(형식 번호 1~4·시드·크기)와 HOME 체크섬(SlotValidator)이 맞는 칸을 찾고,
    /// 0x330 간격으로 이어지는 칸들 중 가장 앞(1번 박스 1번 칸)을 박스 시작으로 봄 → heap 기준 위치를 돌려줌.
    /// 최신 지원 버전 위치 앞뒤부터 1MB씩 넓혀 가며 찾음. 1번 박스 1번 칸에 포켓몬이 있어야 정확함.
    /// </summary>
    public async Task<ulong> ScanBoxStart(ulong radius, IProgress<string> progress, CancellationToken token)
    {
        const int Chunk = 0x100000; int slot = HomeDataOffsets.HomeSlotSize;
        var heap = await SwitchConnection.GetHeapBaseAsync(token).ConfigureAwait(false);
        ulong center = HomeDataOffsets.BoxStartOffset & ~0xFFFFFUL;
        var order = new List<ulong> { center };
        for (ulong d = Chunk; d <= radius; d += Chunk) { order.Add(center + d); if (center >= d) order.Add(center - d); }
        int done = 0;
        foreach (var rel in order)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"찾는 중… heap+0x{rel:X} ({++done}/{order.Count}MB)");
            byte[] buf;
            try { buf = await SwitchConnection.ReadBytesAbsoluteAsync(heap + rel, Chunk + slot, token).ConfigureAwait(false); } catch { continue; }
            for (int i = 0; i + slot <= buf.Length; i += 4)
            {
                if (!IsValidSlot(buf.AsSpan(i, slot))) continue;
                var hit = rel + (ulong)i;
                progress?.Report($"칸 발견: heap+0x{hit:X} · 박스 시작 계산 중…");
                return await FindChainStart(heap, hit, token).ConfigureAwait(false);
            }
        }
        return 0;
    }

    /// <summary>찾은 칸에서 0x330씩 앞으로 가며 (포켓몬 있음/빈 칸)이 이어지는 동안 가장 앞의 '포켓몬 있는 칸'.</summary>
    private async Task<ulong> FindChainStart(ulong heap, ulong hit, CancellationToken token)
    {
        int slot = HomeDataOffsets.HomeSlotSize;
        int maxBack = HomeDataOffsets.HomeSlotCount * HomeDataOffsets.HomeBoxCount;   // 9000칸
        ulong best = hit; ulong cur = hit;
        const int Batch = 300;   // 300칸씩 읽기
        for (int n = 0; n < maxBack; n += Batch)
        {
            int count = (int)Math.Min((ulong)Batch, cur / (ulong)slot);
            if (count <= 0) break;
            ulong from = cur - (ulong)(count * slot);
            byte[] buf;
            try { buf = await SwitchConnection.ReadBytesAbsoluteAsync(heap + from, count * slot, token).ConfigureAwait(false); } catch { break; }
            for (int k = count - 1; k >= 0; k--)
            {
                var span = buf.AsSpan(k * slot, slot);
                if (IsValidSlot(span)) best = from + (ulong)(k * slot);
                else if (!IsEmptySlot(span)) return best;
            }
            cur = from;
        }
        return best;
    }

    public static bool IsValidSlot(ReadOnlySpan<byte> s)
    {
        var ver = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(s);
        if (ver is < 1 or > 4) return false;
        if (System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(s[2..]) == 0) return false;
        int size = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(s[0xE..]);
        if (size == 0 || size % 16 != 0 || HomeDataOffsets.HeaderLength + size > s.Length) return false;
        return SlotValidator?.Invoke(s[..(HomeDataOffsets.HeaderLength + size)].ToArray()) ?? true;
    }
    /// <summary>칸 데이터 확인 (앱에서 HOME 체크섬 검사를 넣어 줌).</summary>
    public static Func<byte[], bool>? SlotValidator { get; set; }
    /// <summary>빈 칸: 시드가 0 (HOME Live가 빈 칸으로 보는 기준).</summary>
    public static bool IsEmptySlot(ReadOnlySpan<byte> s) => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(s[2..]) == 0
        && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(s) <= 4;

    /// <summary>찾은 위치를 이 연결에 바로 적용.</summary>
    public static void UseOffset(ulong rel) { OffsetOverride = rel; BoxStartAddress = 0; }
}