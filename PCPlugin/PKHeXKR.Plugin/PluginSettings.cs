using System.Reflection;
using System.Text.Json;
using PKHeX.Core;
using PKHeX.Core.AutoMod;

namespace PKHeXKR.Plugin;

/// <summary>게임별 트레이너 한 명 (설정 파일 저장용).</summary>
public sealed class TrainerEntry
{
    public string OT { get; set; } = "PKHeX";
    public int Gender { get; set; }
    public int TID { get; set; } = 123456;
    public int SID { get; set; } = 1234;
    public int Lang { get; set; } = (int)LanguageID.Korean;
    public TrainerPref ToPref() => new(OT, (byte)Gender, TID, SID, Lang);
}

/// <summary>플러그인 설정: PKHeX 실행 파일 옆 pkhexkr_plugin.json.</summary>
public sealed class PluginSettings
{
    public TrainerEntry Default { get; set; } = new();
    /// <summary>게임별 트레이너 (키: GameVersion 이름).</summary>
    public Dictionary<string, TrainerEntry> PerGame { get; set; } = new();
    /// <summary>내 사전 ("줄임말 = 바꿀 말" 줄).</summary>
    public string UserDict { get; set; } = "";
    public string LastQuickText { get; set; } = "";
    public string LastOTs { get; set; } = "";
    public string LivingDexPresets { get; set; } = "";
    /// <summary>PokeLDN 위치: pokeldn.exe (릴리스) 또는 소스 폴더</summary>
    public string LdnPath { get; set; } = "";
    /// <summary>보드 포트 (비우면 자동: COM 포트가 하나일 때)</summary>
    public string LdnPort { get; set; } = "";
    public bool LdnHost { get; set; } = true;
    /// <summary>PokeLDN 교환 트레이너 국적 (PKHeX 언어 번호, 8 = 한국어)</summary>
    public int LdnLanguage { get; set; } = 8;
    public bool LdnSkipLegality { get; set; } = true;

    // 저장 위치: PKHeX 폴더 (쓰기 권한이 없으면 %AppData%\PKHeXKR)
    private const string FileName = "pkhexkr_plugin.json";
    private static string ExePath => Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", FileName);
    private static string AppDataPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PKHeXKR", FileName);
    public static string SavedPath { get; private set; }
    public static PluginSettings Current { get; private set; } = Load();

    private static PluginSettings Load()
    {
        foreach (var p in new[] { ExePath, AppDataPath })
        {
            try { if (File.Exists(p)) { SavedPath = p; return JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(p)) ?? new(); } } catch { }
        }
        return new();
    }
    /// <summary>저장. 실패하면 오류 문구, 성공하면 null.</summary>
    public string Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        string err = null;
        foreach (var p in new[] { SavedPath ?? ExePath, ExePath, AppDataPath }.Distinct())
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, json); SavedPath = p; return null; }
            catch (Exception ex) { err = ex.Message; }
        }
        return err ?? "저장하지 못했습니다";
    }

    public TrainerEntry Explicit(GameVersion v) => PerGame.TryGetValue(v.ToString(), out var t) ? t : null;

    public Dictionary<string, string> Dict()
    {
        var d = new Dictionary<string, string>();
        foreach (var line in (UserDict ?? "").Replace("\r", "").Split('\n'))
        {
            var i = line.IndexOf('='); if (i <= 0) continue;
            var k = line[..i].Trim(); var v = line[(i + 1)..].Trim();
            if (k.Length > 0 && v.Length > 0) d[k] = v;
        }
        return d;
    }
}

/// <summary>
/// 생성하는 동안만 ALM 환경을 이 플러그인 기준으로 바꾸고 끝나면 되돌림.
/// (ALM 플러그인이 함께 깔려 있어도 사용자의 ALM 설정·트레이너 목록을 망가뜨리지 않게)
/// </summary>
public sealed class AlmScope : IDisposable
{
    private readonly bool allowOverride, matchBalls, forceBall, allRibbons, easter;
    private readonly int timeout;
    private readonly Dictionary<GameVersion, List<ITrainerInfo>> trainers;

    public AlmScope(SaveFile sav)
    {
        allowOverride = APILegality.AllowTrainerOverride; matchBalls = APILegality.SetMatchingBalls; forceBall = APILegality.ForceSpecifiedBall;
        allRibbons = APILegality.SetAllLegalRibbons; timeout = APILegality.Timeout; easter = Legalizer.EnableEasterEggs;
        trainers = SnapshotTrainers();
        APILegality.AllowTrainerOverride = true; APILegality.SetMatchingBalls = true; APILegality.ForceSpecifiedBall = true;
        APILegality.SetAllLegalRibbons = false; APILegality.Timeout = 45; Legalizer.EnableEasterEggs = false;
        var st = PluginSettings.Current;
        GenTrainers.Register(sav, v => st.Explicit(v)?.ToPref(), v => st.Default.ToPref(), GenTrainers.SafeLang);
    }

    public void Dispose()
    {
        APILegality.AllowTrainerOverride = allowOverride; APILegality.SetMatchingBalls = matchBalls; APILegality.ForceSpecifiedBall = forceBall;
        APILegality.SetAllLegalRibbons = allRibbons; APILegality.Timeout = timeout; Legalizer.EnableEasterEggs = easter;
        RestoreTrainers(trainers);
    }

    // ALM 트레이너 목록(내부 필드)을 그대로 보관·복원
    private static Dictionary<GameVersion, List<ITrainerInfo>> Inner()
    {
        try
        {
            var db = typeof(TrainerSettings).GetField("Database", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            return db?.GetType().GetField("Database", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(db) as Dictionary<GameVersion, List<ITrainerInfo>>;
        }
        catch { return null; }
    }
    private static Dictionary<GameVersion, List<ITrainerInfo>> SnapshotTrainers() => Inner()?.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
    private static void RestoreTrainers(Dictionary<GameVersion, List<ITrainerInfo>> snap)
    {
        if (snap == null) return;
        var d = Inner(); if (d == null) return;
        d.Clear(); foreach (var kv in snap) d[kv.Key] = kv.Value;
    }
}
