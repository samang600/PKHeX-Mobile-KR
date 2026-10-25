namespace HomeLive.DeviceExecutor;

public static class HomeDataOffsets
{
    public const string HomeTitleID = "010015F008C54000";
    public const string HomeSupportedVersion = "4.1.0";
    /// <summary>버전별 박스 시작 위치 (heap 기준). 4.1.0: PKHeXKR 자동 찾기로 확인.</summary>
    public static readonly Dictionary<string, uint> KnownOffsets = new() { ["4.1.0"] = 0x12F80 };   // PKHeXKR: 4.0.0은 지원하지 않음 (4.1.0 이후로 HOME이 4.0.0을 받지 않음)
    /// <summary>같은 버전이라도 기기·실행 상태에 따라 위치가 다른 경우가 있어 함께 시험해 보는 위치들 (원본 HOME Live 4.1.0 값 0x715FC0 포함).</summary>
    public static readonly Dictionary<string, uint[]> AltOffsets = new() { ["4.1.0"] = [0x715FC0] };
    public static bool IsKnownVersion(string version) => KnownOffsets.ContainsKey(version);
    public static uint OffsetFor(string version) => KnownOffsets.TryGetValue(version ?? "", out var o) ? o : KnownOffsets[HomeSupportedVersion];

    public const int HomeSlotSize = 0x330;
    public const int HomeSlotCount = 30;
    public const int HomeBoxCount = 300;   // HOME 4.1.0부터 300박스 (200번 뒤에 이어짐)
    public const int HeaderLength = 0x10;
    public const int SeedOffset = 0x02;
    public const int EncSizeOffset = 0x0E;

    /// <summary>자동 찾기 시작점 (최신 지원 버전 위치).</summary>
    public static uint BoxStartOffset => KnownOffsets[HomeSupportedVersion];

    public static ulong GetBoxOffset(ulong startOffset, int box) => startOffset + (ulong)(HomeSlotSize * HomeSlotCount * box);
    public static ulong GetSlotOffset(ulong startOffset, int box, int slot) => GetBoxOffset(startOffset, box) + (uint)(HomeSlotSize * slot);
}