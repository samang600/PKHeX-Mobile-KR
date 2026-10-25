using PKHeX.Core;
using PKHeX.Core.AutoMod;
namespace PKHeXKR;

/// <summary>
/// 모바일 앱과 PC 플러그인이 함께 쓰는 생성 엔진의 연결 지점.
/// 앱마다 다른 부분(합법 판정 기준, 추가 배포 기간 등)은 여기 대리자를 바꿔 끼움.
/// </summary>
public static class GenHost
{
    /// <summary>문장 해석·표시에 쓰는 이름 데이터. 모바일: 데이터 언어(GameInfo.Strings), PC 플러그인: PKHeX 표시 언어와 상관없이 한국어.</summary>
    public static Func<GameStrings> StringsSource = () => GameInfo.Strings;
    public static GameStrings Strings => StringsSource();

    /// <summary>합법 판정. 모바일은 설정(검사 끄기·스위치판 FRLG 등)을 반영한 판정으로 바꿔 끼움.</summary>
    /// <summary>true면 교환·HOME 맡기기가 안 되는 조우(라이드 코라이돈·미라이돈 등)는 후보에서 뺌 (교환봇용)</summary>
    public static bool ExcludeUntradable;
    public static Func<PKM, bool> IsLegal = p => { try { return p.Species != 0 && new LegalityAnalysis(p).Valid; } catch { return false; } };

    /// <summary>앱에 들어 있는 PKHeX.Core에 없는 배포 기간 (모바일: 배포 데이터 자동 업데이트).</summary>
    public static Func<MysteryGift, DistributionWindow?> ExtraWindow = _ => null;

    /// <summary>5~7세대 카드 배포 기간 (불바피디아 기준, card_windows.json).</summary>
    public static Dictionary<string, string[][]> CardWindows = new();
    public static void LoadCardWindows(Stream st)
    {
        try { CardWindows = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[][]>>(st) ?? new(); } catch { }
    }

    /// <summary>
    /// 배포 기간 정보가 없는 시리얼 코드 배포(예: 색이 다른 무한다이노 ID 221118, 신비한 포켓몬 2022 ID 220909)는
    /// 카드의 트레이너 ID가 배포 날짜(YYMMDD)인 경우가 많음 → 게임 발매일 이후·오늘 이전의 올바른 날짜면 사용.
    /// </summary>
    private static DateOnly? IdDate(MysteryGift g)
    {
        try
        {
            var id = (g.ID32 % 1_000_000).ToString("000000");
            if (!DateOnly.TryParseExact(id, "yyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)) return null;
            var min = g switch { WB7 => new DateOnly(2018, 11, 16), WC8 => new DateOnly(2019, 11, 15), WB8 => new DateOnly(2021, 11, 19), WA8 => new DateOnly(2022, 1, 28), WC9 => new DateOnly(2022, 11, 18), WA9 => new DateOnly(2025, 10, 16), _ => new DateOnly(2000, 1, 1) };
            return d >= min && d <= DateOnly.FromDateTime(DateTime.Today) ? d : null;
        }
        catch { return null; }
    }

    /// <summary>배포 카드의 배포 날짜 (개체 언어 지역 기준).</summary>
    public static DateOnly? DistributionDate(MysteryGift g, int lang)
    {
        // 레츠고·8~9세대 배포: PKHeX가 합법성 검사에 쓰는 배포 기간(서버 날짜) 데이터의 시작일
        {
            DistributionWindow w = default; bool ok = false;
            try
            {
                if (g is WB7 g7b) ok = g7b.GetDistributionWindow(out w);
                else if (g is WC8 g8) ok = g8.GetDistributionWindow(out w);
                else if (g is WA8 g8a) ok = g8a.GetDistributionWindow(out w);
                else if (g is WB8 g8b) ok = g8b.GetDistributionWindow(out w);
                else if (g is WC9 g9) ok = g9.GetDistributionWindow(out w);
                else if (g is WA9 g9a) ok = g9a.GetDistributionWindow(out w);
                else if (g.Generation >= 8) return null;
            }
            catch { return null; }
            if (!ok && ExtraWindow(g) is { } nw) { w = nw; ok = true; }
            if (g is WB7 or WC8 or WA8 or WB8 or WC9 or WA9) return ok ? w.Start.AddDays(w.GenerateDaysAfterStart) : IdDate(g);
        }
        if (g.Generation is < 5 or > 7) return null;
        var tid = g.Generation >= 7 ? (g.ID32 % 1000000).ToString("000000") : g.TID16.ToString("00000");
        var key = $"{g.GetType().Name}|{g.CardID}|{GameInfo.GetStrings("en").Species[g.Species]}|{tid}";
        if (!CardWindows.TryGetValue(key, out var ws)) return null;
        bool Ok(string r)
        {
            r = r.ToLowerInvariant(); if (r.Trim() is "all" or "") return true;
            return (LanguageID)lang switch { LanguageID.Korean => r.Contains("korea"), LanguageID.Japanese => r.Contains("japan"), _ => r.Contains("american") || r.Contains("pal") || r.Contains("english") };
        }
        var pick = ws.FirstOrDefault(w => Ok(w[2])) ?? ws.FirstOrDefault();
        return pick != null && DateOnly.TryParse(pick[0], out var d) ? d : null;
    }

    /// <summary>개체가 배포 카드에서 왔으면 만난 날짜를 배포 날짜로 (합법이 유지될 때만).</summary>
    public static void FixEventDate(PKM p)
    {
        try
        {
            if (p.Species == 0 || p.Format < 5) return;
            if (new LegalityAnalysis(p).EncounterMatch is not MysteryGift mg) return;
            if (DistributionDate(mg, p.Language) is not { } d || p.MetDate == d) return;
            var t = p.Clone(); t.MetDate = d; t.RefreshChecksum();
            if (IsLegal(t) || !IsLegal(p)) { p.MetDate = d; p.RefreshChecksum(); }
        }
        catch { }
    }

    public static void ClearNickTrash(PKM p) { try { var n = p.Nickname; p.NicknameTrash.Clear(); p.Nickname = n; } catch { } }
    public static void ClearOTTrash(PKM p) { try { var n = p.OriginalTrainerName; p.OriginalTrainerTrash.Clear(); p.OriginalTrainerName = n; } catch { } }

    /// <summary>어버이 바꾸기: 이로치였다면 모양(별·네모)을 유지해 다시 이로치로.</summary>
    public static void AutoOT(PKM p, string ot, uint id32, byte gender, int lang)
    {
        bool shiny = p.IsShiny; bool square = shiny && p.ShinyXor == 0;
        try { p.OriginalTrainerTrash.Clear(); } catch { }   // 긴 세이브 이름 뒤에 짧은 이름을 넣으면 남는 쓰레기 바이트 제거
        p.OriginalTrainerName = ot; p.OriginalTrainerGender = gender; p.ID32 = id32;
        if (lang > 0) { var nick = p.IsNicknamed; p.Language = lang; if (!nick) { p.Nickname = SpeciesName.GetSpeciesNameGeneration(p.Species, p.Language, p.Format); p.IsNicknamed = false; } }
        if (p.Format >= 6)   // 교환하지 않은 상태로: 맡은 트레이너 정보(이름 쓰레기 바이트·친밀도·언어·추억)까지 비움
        {
            try { p.HandlingTrainerTrash.Clear(); } catch { }
            p.HandlingTrainerName = ""; p.CurrentHandler = 0; p.HandlingTrainerFriendship = 0; p.HandlingTrainerGender = 0;
            if (p is IHandlerLanguage hl) hl.HandlingTrainerLanguage = 0;
            if (p is IMemoryHT mh) mh.ClearMemoriesHT();
        }
        if (shiny && !p.IsShiny)   // ID가 바뀌어 이로치가 풀리면 같은 모양(별·네모)으로 다시 이로치
        {
            if (p.Generation >= 6) p.PID = ShinyUtil.GetShinyPID(p.TID16, p.SID16, p.PID, square ? 0u : 1u);
            else p.SetShiny();
        }
        p.RefreshChecksum();
    }

    /// <summary>
    /// 개체를 만들 때 쓸 트레이너. 1~3세대 세이브는 언어가 0으로 읽혀 영어 개체가 되고,
    /// 일본판 세이브의 가나 이름이 영어 글자표에 안 들어가 어버이 이름이 비는 문제(OT 너무 짧음) → 세이브 언어를 채운 사본.
    /// </summary>
    public static ITrainerInfo TrainerOf(SaveFile sav) => TrainerOverride(sav) ?? (sav.Language > 0 ? sav : new SimpleTrainerInfo(sav) { Language = GenTrainers.SaveLanguage(sav, (int)LanguageID.English) });

    /// <summary>
    /// 지금 세이브 대신 쓸 트레이너 (null이면 세이브 트레이너).
    /// PC 플러그인: PKHeX의 빈 세이브(파일 없이 연 상태, "PKHeX"·123456·영어)이거나 이 게임을 따로 지정했으면 설정 트레이너.
    /// </summary>
    public static Func<SaveFile, ITrainerInfo> TrainerOverride = _ => null;

    /// <summary>자동 합법화(ALM): 지금 게임에 쓸 트레이너 기준으로 만들고 세이브 형식으로 맞춤.</summary>
    public static PKM Legal(SaveFile sav, string showdown) => Legal(sav, new ShowdownSet(showdown));
    public static PKM Legal(SaveFile sav, IBattleTemplate set)
    {
        var p = TrainerOf(sav).GetLegalFromSet(set).Created;
        return p == null ? null : EntityConverter.ConvertToType(p, sav.PKMType, out _) ?? p;
    }

    /// <summary>게임 이름 (데이터 언어 기준).</summary>
    public static string GameName(GameVersion v) { if ((int)v == 56) return "파이어레드·리프그린 (HOME 56)"; var g = GenHost.Strings.gamelist; return (int)v < g.Length && !string.IsNullOrEmpty(g[(int)v]) ? g[(int)v] : v.ToString(); }
}

/// <summary>게임별 트레이너 설정 (이름·성별·TID·SID·언어). 명시 설정이 없으면 null.</summary>
public sealed record TrainerPref(string OT, byte Gender, int TID, int SID, int Lang);

/// <summary>
/// ALM(자동 합법화·리빙덱스 등)이 쓸 게임별 어버이 등록.
/// ALM은 만드는 개체의 출신 게임에 등록된 트레이너가 없으면 "ALM"·영어로 만듦 → 모든 게임에 트레이너를 등록.
/// 우선순위: 지금 세이브(같은 게임) → 게임별 트레이너 설정 → 같은 세대 세이브의 트레이너 → 기본 트레이너.
/// </summary>
public static class GenTrainers
{
    public static readonly GameVersion[] Games = [GameVersion.ZA, GameVersion.SL, GameVersion.VL, GameVersion.PLA, GameVersion.BD, GameVersion.SP, GameVersion.SW, GameVersion.SH,
        GameVersion.GP, GameVersion.GE, GameVersion.US, GameVersion.UM, GameVersion.SN, GameVersion.MN, GameVersion.OR, GameVersion.AS, GameVersion.X, GameVersion.Y,
        GameVersion.B2, GameVersion.W2, GameVersion.B, GameVersion.W, GameVersion.HG, GameVersion.SS, GameVersion.Pt, GameVersion.D, GameVersion.P,
        GameVersion.E, GameVersion.FR, GameVersion.LG, GameVersion.R, GameVersion.S, GameVersion.C, GameVersion.GD, GameVersion.SI, GameVersion.YW, GameVersion.RD, GameVersion.BU,
        GameVersion.GN, GameVersion.CXD];

    public static bool IsJapaneseSave(SaveFile s) => s is SAV1 { Japanese: true } or SAV2 { Japanese: true } or SAV3 { Japanese: true };

    /// <summary>세이브 언어 (1~3세대는 0으로 읽혀 ALM이 영어로 만듦 → 일본판 형식이면 일본어, 아니면 대체 언어).</summary>
    public static int SaveLanguage(SaveFile s, int fallback) => s.Language > 0 ? s.Language : IsJapaneseSave(s) ? (int)LanguageID.Japanese : fallback;

    /// <summary>
    /// 트레이너 등록.
    /// <paramref name="explicitPref"/>: 그 게임에 따로 정한 트레이너(없으면 null), <paramref name="defaultPref"/>: 기본 트레이너, <paramref name="safeLang"/>: 그 게임에 있는 언어로 고친 값.
    /// </summary>
    public static void Register(SaveFile s, Func<GameVersion, TrainerPref> explicitPref, Func<GameVersion, TrainerPref> defaultPref, Func<GameVersion, int, int> safeLang)
    {
        TrainerSettings.Clear();
        var basePref = explicitPref(s.Version) ?? defaultPref(s.Version);
        int saveLang = SaveLanguage(s, safeLang(s.Version, basePref?.Lang ?? (int)LanguageID.English));
        var over = GenHost.TrainerOverride(s);
        var me = over ?? (s.Language > 0 ? s : new SimpleTrainerInfo(s) { Language = saveLang });   // 지금 게임에 쓸 트레이너
        if (over != null) saveLang = over.Language;
        TrainerSettings.Register(me);
        foreach (var v in Games.Distinct())
        {
            if (v == s.Version) continue;
            try
            {
                var gen = new SimpleTrainerInfo(v).Generation; var ctx = v.Context;
                var ex = explicitPref(v);
                bool sameGen = ex == null && s.Generation == gen && s.Context == ctx && !string.IsNullOrEmpty(me.OT);
                string ot; byte g; int lang; uint id32;
                if (sameGen) { ot = me.OT; g = me.Gender; lang = saveLang; id32 = me.ID32; }
                else
                {
                    var t = ex ?? defaultPref(v);
                    ot = t.OT; g = t.Gender; lang = safeLang(v, t.Lang);
                    id32 = gen >= 7 ? (uint)(Math.Min(t.SID, 4294) * 1_000_000L + Math.Min(t.TID, 999999)) : (uint)(Math.Min(t.TID, 65535) | (Math.Min(t.SID, 65535) << 16));
                }
                if (v is GameVersion.GN or GameVersion.BU) lang = (int)LanguageID.Japanese;
                var tr = gen is 6 or 7
                    ? new SimpleTrainerInfo(v) { OT = ot, Gender = g, Language = lang, ID32 = id32, ConsoleRegion = 5, Country = 136, Region = 1 }   // 3DS: 한국 본체
                    : new SimpleTrainerInfo(v) { OT = ot, Gender = g, Language = lang, ID32 = id32 };
                TrainerSettings.Register(tr);
            }
            catch { }
        }
    }

    /// <summary>설정 트레이너 → 그 게임용 트레이너 정보 (7세대 이후 6자리 TID·4자리 SID, 3DS는 한국 본체).</summary>
    public static SimpleTrainerInfo FromPref(GameVersion v, TrainerPref t, Func<GameVersion, int, int> safeLang)
    {
        var gen = new SimpleTrainerInfo(v).Generation;
        int lang = v is GameVersion.GN or GameVersion.BU ? (int)LanguageID.Japanese : safeLang(v, t.Lang);
        uint id32 = gen >= 7 ? (uint)(Math.Min(t.SID, 4294) * 1_000_000L + Math.Min(t.TID, 999999)) : (uint)(Math.Min(t.TID, 65535) | (Math.Min(t.SID, 65535) << 16));
        return gen is 6 or 7
            ? new SimpleTrainerInfo(v) { OT = t.OT, Gender = t.Gender, Language = lang, ID32 = id32, ConsoleRegion = 5, Country = 136, Region = 1 }
            : new SimpleTrainerInfo(v) { OT = t.OT, Gender = t.Gender, Language = lang, ID32 = id32 };
    }

    /// <summary>한국판이 없는 게임은 한국어 대신 다른 언어로.</summary>
    public static int SafeLang(GameVersion v, int lang)
    {
        bool noKorean = v is GameVersion.RD or GameVersion.GN or GameVersion.BU or GameVersion.YW or GameVersion.C
            or GameVersion.R or GameVersion.S or GameVersion.E or GameVersion.FR or GameVersion.LG or GameVersion.CXD;
        if (v is GameVersion.GN or GameVersion.BU) return (int)LanguageID.Japanese;
        if (noKorean && lang == (int)LanguageID.Korean) return (int)LanguageID.English;
        return lang <= 0 ? (int)LanguageID.English : lang;
    }
}
