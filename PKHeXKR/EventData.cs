using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// 배포 데이터(이상한 소포 카드 DB) 자동 업데이트.
/// PKHeX 개발 저장소(kwsch/PKHeX master)의 최신 배포 DB(mgdb/*.pkl)를 받아, 앱에 들어 있는 PKHeX.Core에 없는 카드만
/// 앱 폴더에 낱장 파일로 저장 → PKHeX의 외부 배포 폴더 기능(EncounterEvent.RefreshMGDB)으로 합법성 검사·자동 합법화·인카운터 검색에 반영.
/// 앱 업데이트 없이 새 배포(예: Z-A 신규 배포)를 인식. PKHeX.Core 자체는 지금처럼 수동 업데이트.
/// </summary>
public static class EventData
{
    private const string Base = "https://raw.githubusercontent.com/kwsch/PKHeX/master/PKHeX.Core/Resources/legality/mgdb/";
    public static bool Enabled { get => Preferences.Get("mgdb_auto", true); set => Preferences.Set("mgdb_auto", value); }
    public static string Dir { get { var d = Path.Combine(FileSystem.AppDataDirectory, "mgdb_auto"); Directory.CreateDirectory(d); return d; } }
    public static int Count { get { try { return Directory.GetFiles(Dir).Length; } catch { return 0; } } }
    public static DateTime LastCheck => Preferences.Get("mgdb_last", DateTime.MinValue);
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static string EtagKey(string file) => $"mgdb_etag_{AppInfo.Current.VersionString}_{file}";

    // 파일 이름, 카드 크기, 확장자, 앱에 들어 있는 DB
    private static readonly (string File, int Size, string Ext, Func<IEnumerable<DataMysteryGift>> Embedded)[] Kinds =
    [
        ("wa9.pkl", WA9.Size, ".wa9", () => EncounterEvent.MGDB_G9A),
        ("wc9.pkl", WC9.Size, ".wc9", () => EncounterEvent.MGDB_G9),
        ("wc8.pkl", WC8.Size, ".wc8", () => EncounterEvent.MGDB_G8),
        ("wb8.pkl", WB8.Size, ".wb8", () => EncounterEvent.MGDB_G8B),
        ("wa8.pkl", WA8.Size, ".wa8", () => EncounterEvent.MGDB_G8A),
    ];

    /// <summary>앱 시작 시: 받아 둔 카드와 배포 기간을 PKHeX에 등록.</summary>
    public static void LoadLocal()
    {
        try { if (Count > 0) EncounterEvent.RefreshMGDB(Dir); } catch { }
        try { if (File.Exists(WinPath)) { Windows = ParseWindows(File.ReadAllText(WinPath)); Inject(); } } catch { }
    }

    /// <summary>앱의 PKHeX.Core 배포 기간표(EncounterServerDate)에 없는 카드만 추가 → 합법성 검사·자동 합법화·만난 날짜 설정이 그대로 인식. 기존 값은 바꾸지 않음.</summary>
    private static int Inject()
    {
        int n = 0;
        foreach (var kv in Windows)
        {
            try
            {
                var parts = kv.Key.Split(':');
                var f = typeof(EncounterServerDate).GetField(parts[0], System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (f?.GetValue(null) is Dictionary<int, DistributionWindow> dict && !dict.ContainsKey(int.Parse(parts[1])) && dict.TryAdd(int.Parse(parts[1]), kv.Value)) n++;
            }
            catch { }
        }
        return n;
    }

    // ===== 배포 기간 (새 카드는 앱의 PKHeX.Core에 기간 정보가 없어 만난 날짜가 항상 '배포 기간 밖'으로 나옴 → 최신 PKHeX 기간표도 함께 받음) =====
    private const string WinUrl = "https://raw.githubusercontent.com/kwsch/PKHeX/master/PKHeX.Core/Legality/Encounters/Data/Live/EncounterServerDate.cs";
    private static string WinPath => Path.Combine(FileSystem.AppDataDirectory, "mgdb_windows.txt");
    private static Dictionary<string, DistributionWindow> Windows = new();

    /// <summary>PKHeX 소스의 배포 기간표 읽기: <c>{0603, new(2026, 08, 28)}</c>, <c>{0xE5EB, new(2022, 11, 17, 2023, 02, 03)}</c>, 끝의 +n은 생성 날짜 보정.</summary>
    public static Dictionary<string, DistributionWindow> ParseWindows(string src)
    {
        var d = new Dictionary<string, DistributionWindow>();
        string table = null;
        foreach (var line in src.Split('\n'))
        {
            var t = System.Text.RegularExpressions.Regex.Match(line, @"Dictionary<int,\s*DistributionWindow>\s+(\w+)\s*=");
            if (t.Success) { table = t.Groups[1].Value; continue; }
            if (table == null) continue;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(line, @"\{\s*(0x[0-9A-Fa-f]+|\d+)\s*,\s*new\(([^)]*)\)\s*\}"))
            {
                try
                {
                    var keyS = m.Groups[1].Value;
                    int key = keyS.StartsWith("0x") ? Convert.ToInt32(keyS[2..], 16) : int.Parse(keyS);
                    var a = m.Groups[2].Value.Split(',').Select(x => int.Parse(x.Trim().TrimStart('+'))).ToArray();
                    DistributionWindow w = a.Length switch
                    {
                        3 => new(a[0], a[1], a[2]),
                        4 => new(a[0], a[1], a[2], (byte)a[3]),
                        6 => new(a[0], a[1], a[2], a[3], a[4], a[5]),
                        7 => new(a[0], a[1], a[2], a[3], a[4], a[5], (byte)a[6]),
                        _ => throw new FormatException(),
                    };
                    d[$"{table}:{key}"] = w;
                }
                catch { }
            }
        }
        return d;
    }

    private static bool BundledWindow(MysteryGift g, out DistributionWindow w)
    {
        w = default;
        try
        {
            return g switch
            {
                WB7 x => x.GetDistributionWindow(out w), WC8 x => x.GetDistributionWindow(out w), WA8 x => x.GetDistributionWindow(out w),
                WB8 x => x.GetDistributionWindow(out w), WC9 x => x.GetDistributionWindow(out w), WA9 x => x.GetDistributionWindow(out w), _ => false,
            };
        }
        catch { return false; }
    }

    /// <summary>앱의 PKHeX.Core에 없고 받아 둔 최신 기간표에만 있는 배포 기간.</summary>
    public static DistributionWindow? NewWindow(MysteryGift g)
    {
        if (Windows.Count == 0 || BundledWindow(g, out _)) return null;
        var (name, chk) = g switch { WB7 => ("WB7Gifts", ""), WC8 => ("WC8Gifts", "WC8GiftsChk"), WA8 => ("WA8Gifts", ""), WB8 => ("WB8Gifts", ""), WC9 => ("WC9Gifts", "WC9GiftsChk"), WA9 => ("WA9Gifts", ""), _ => ("", "") };
        if (name.Length == 0) return null;
        if (Windows.TryGetValue($"{name}:{g.CardID}", out var w)) return w;
        if (chk.Length > 0 && g is DataMysteryGift dg)
        {
            int sum = dg switch { WC8 x => x.Checksum, WC9 x => x.Checksum, _ => -1 };
            if (sum >= 0 && Windows.TryGetValue($"{chk}:{sum}", out w)) return w;
        }
        return null;
    }

    /// <summary>'배포 기간 밖' 판정이 앱의 기간표에 카드가 없어서 생긴 것이고, 최신 기간표로는 기간 안이면 true.</summary>
    public static bool DateOkByNewWindow(MysteryGift g, PKM pk)
    {
        try { return NewWindow(g) is { } w && w.Contains(pk.MetDate ?? DateOnly.MinValue); } catch { return false; }
    }

    /// <summary>최신 배포 DB 확인. 자동은 하루 한 번, 수동은 바로. 반환: 결과 문구.</summary>
    public static async Task<string> Run(bool manual)
    {
        if (!manual && (!Enabled || (DateTime.Now - LastCheck).TotalHours < 20)) return null;
        if (!await gate.WaitAsync(0)) return manual ? "이미 확인 중입니다" : null;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PKHeX-Mobile-KR");
            int before = Count, added = 0, failed = 0; bool changed = false;
            foreach (var k in Kinds)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, Base + k.File);
                    var etag = Preferences.Get(EtagKey(k.File), "");   // 앱 버전이 바뀌면(PKHeX.Core 갱신) 다시 비교
                    if (!manual && etag.Length > 0) req.Headers.TryAddWithoutValidation("If-None-Match", etag);
                    using var res = await http.SendAsync(req);
                    if (res.StatusCode == System.Net.HttpStatusCode.NotModified) continue;
                    res.EnsureSuccessStatusCode();
                    var bin = await res.Content.ReadAsByteArrayAsync();
                    if (bin.Length == 0 || bin.Length % k.Size != 0) { failed++; continue; }   // 형식이 바뀌었으면 건드리지 않음
                    var have = new HashSet<string>(k.Embedded().Select(g => Convert.ToHexString(g.Data)));
                    var fresh = new List<(string Name, byte[] Data)>();
                    for (int i = 0; i < bin.Length / k.Size; i++)
                    {
                        var d = bin.AsSpan(i * k.Size, k.Size).ToArray();
                        if (have.Contains(Convert.ToHexString(d))) continue;   // 앱에 이미 있는 카드
                        if (MysteryGift.GetMysteryGift(d, k.Ext) is not { } g || g.Species == 0 && !g.IsItem) continue;
                        fresh.Add(($"{g.CardID:0000}_{Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(d))[..8]}{k.Ext}", d));
                    }
                    // 이 종류의 이전 파일을 지우고 새로 (PKHeX.Core가 업데이트돼 이미 들어 있게 된 카드는 자연히 빠짐)
                    var old = Directory.GetFiles(Dir, "*" + k.Ext).Select(Path.GetFileName).ToHashSet();
                    if (!old.SetEquals(fresh.Select(f => f.Name)))
                    {
                        foreach (var f in old) File.Delete(Path.Combine(Dir, f));
                        foreach (var f in fresh) File.WriteAllBytes(Path.Combine(Dir, f.Name), f.Data);
                        added += fresh.Count(f => !old.Contains(f.Name)); changed = true;
                    }
                    if (res.Headers.ETag is { } et) Preferences.Set(EtagKey(k.File), et.Tag);
                }
                catch { failed++; }
            }
            try   // 배포 기간표
            {
                var src = await http.GetStringAsync(WinUrl);
                var w = ParseWindows(src);
                if (w.Count > 50) { File.WriteAllText(WinPath, src); Windows = w; if (Inject() > 0) changed = true; }
            }
            catch { }
            Preferences.Set("mgdb_last", DateTime.Now);
            if (changed)
            {
                EncounterEvent.RefreshMGDB(Dir);
                AppState.ClearLegalCache();
                MainThread.BeginInvokeOnMainThread(() => { AppState.RefreshSummary(); AppState.NotifyBox(); });
            }
            int now = Count;
            if (failed == Kinds.Length) return manual ? "배포 데이터를 확인하지 못했습니다 (인터넷 연결 확인)" : null;
            if (added > 0) return $"새 배포 데이터 {added}개를 받았습니다 (앱에 없던 배포 총 {now}개 반영)";
            return manual ? (now > 0 ? $"최신 상태입니다 (앱에 없던 배포 {now}개 반영 중)" : "최신 상태입니다 (앱에 들어 있는 배포 데이터가 최신)") : null;
        }
        catch { return manual ? "배포 데이터를 확인하지 못했습니다 (인터넷 연결 확인)" : null; }
        finally { gate.Release(); }
    }
}
