using System.Text.RegularExpressions;
using PKHeX.Core;
namespace PKHeXKR;

/// <summary>한 마리 주문 내용 (해석 결과).</summary>
public sealed class OrderSpec
{
    public string Raw = "";
    public ushort Species; public int Form = -1; public string FormText = "";
    public int Gender = -1;                 // 0 ♂, 1 ♀
    public int Shiny;                       // 0 지정 없음(일반), 1 이로치, 2 일반 명시, 3 별, 4 네모
    public int Alpha = -1;                  // -1 지정 없음, 0 일반, 1 우두머리
    public int Ball = -1, Nature = -1, Ability = -1;   // Ability: 0/1/2(숨특)
    public int ScaleMin = -1, ScaleMax = -1; public string SizeText = "";
    public int Mark = -1;
    public int FlawlessIVs = -1;            // 6V/5V…
    public int[] ExactIV = [-1, -1, -1, -1, -1, -1];
    public int Level = -1, Count = 1;
    public string OT; public int TID = -1, SID = -1, OTGender = -1;
    public bool Event;                      // "배포" → 배포에서만 찾음
    public List<string> Keywords = [];      // 배포를 고를 단서 ("리코의" → 리코)
    public ushort PreSpecies;               // "나오하(마스카나진화)"의 나오하
    public bool Superl;                     // "가장"·"제일" 다음 크기 단어는 최대·최소
    public List<string> Unknown = [];
    public List<string> Notes = [];

    public string Describe(GameStrings s)
    {
        var p = new List<string>();
        if (Event) p.Add("배포" + (Keywords.Count > 0 ? $"[{string.Join(" ", Keywords)}]" : ""));
        if (Species != 0) p.Add((PreSpecies != 0 && PreSpecies != Species ? s.Species[PreSpecies] + "→" : "") + s.Species[Species] + (FormText.Length > 0 ? $"({FormText})" : ""));
        if (Gender >= 0) p.Add(Gender == 0 ? "♂" : "♀");
        if (Alpha == 1) p.Add("우두머리");
        p.Add(Shiny switch { 1 => "이로치", 3 => "별 이로치", 4 => "네모 이로치", 2 => "일반 색", _ => "" });
        if (Ball > 0 && Ball < s.balllist.Length) p.Add(s.balllist[Ball]);
        if (Nature >= 0) p.Add(s.natures[Nature]);
        if (Ability >= 0) p.Add(new[] { "특성1", "특성2", "숨겨진 특성" }[Ability]);
        if (SizeText.Length > 0) p.Add(SizeText);
        if (Mark >= 0) { var n = "Ribbon" + ((RibbonIndex)Mark); p.Add(s.Ribbons.GetNameSafe(n, out var k) ? k : ((RibbonIndex)Mark).ToString()); }
        if (FlawlessIVs > 0) p.Add($"{FlawlessIVs}V");
        var ex = string.Join(" ", ExactIV.Select((v, i) => v < 0 ? "" : $"{"HABCDS"[i]}{v}").Where(x => x.Length > 0)); if (ex.Length > 0) p.Add(ex);
        if (Level > 0) p.Add($"Lv{Level}");
        if (OT != null || TID >= 0) p.Add($"어버이 {OT ?? "-"}{(TID >= 0 ? $" {TID}" : "")}{(SID >= 0 ? $"/{SID}" : "")}");
        if (Count > 1) p.Add($"×{Count}");
        return string.Join(" · ", p.Where(x => x.Length > 0));
    }
}

/// <summary>
/// 대충 쓴 한국어 주문 문장 → 주문 내용. 예: "빠르모트 암컷 이로치 러브볼 가장작게 / 찌리배리 레벨볼 큰증".
/// 줄바꿈·"/"로 여러 마리, "종(폼1,폼2)"로 여러 폼, "어버이: 이름 TID" 줄은 아래 전체에 적용.
/// </summary>
public static class QuickParse
{
    /// <summary>기본 줄임말 사전 (줄임말 → 바꿀 말, "/"는 여러 마리).</summary>
    public static readonly Dictionary<string, string> BuiltIn = new()
    {
        ["달투곰"] = "다투곰 붉은달", ["영꽃"] = "플라엣테 영원의꽃", ["영원의꽃플라엣테"] = "플라엣테 영원의꽃", ["알나인"] = "나인테일 알로라", ["알로라나인"] = "나인테일 알로라", ["히검귀"] = "대검귀 히스이", ["히스이검귀"] = "대검귀 히스이", ["붉은달다투곰"] = "다투곰 붉은달",
        ["날치머"] = "날개치는머리", ["땅기날"] = "땅을기는날개",
        ["미라코라"] = "미라이돈 / 코라이돈", ["코라미라"] = "코라이돈 / 미라이돈",
        ["자시자마"] = "자시안 / 자마젠타", ["자마자시"] = "자마젠타 / 자시안", ["자마젠"] = "자마젠타",
    };
    /// <summary>사용자 사전 (앱 설정에 저장, 기본 사전보다 우선).</summary>
    public static Dictionary<string, string> User = new();

    /// <summary>"줄임말=바꿀말" 줄들을 사전으로.</summary>
    public static Dictionary<string, string> ParseDict(string text)
    {
        var d = new Dictionary<string, string>();
        foreach (var line in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var i = line.IndexOfAny(['=', '→']);
            if (i <= 0) continue;
            var k = Norm(line[..i]); var v = line[(i + 1)..].Trim();
            if (k.Length > 0 && v.Length > 0) d[k] = v;
        }
        return d;
    }

    private static bool TryAlias(string token, out string v) => User.TryGetValue(Norm(token), out v) || BuiltIn.TryGetValue(Norm(token), out v);

    /// <summary>줄임말 풀기. "미라코라 이로치" → "미라이돈 이로치", "코라이돈 이로치".</summary>
    private static IEnumerable<string> ExpandAliases(string entry)
    {
        var tokens = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var results = new List<List<string>> { new() };
        foreach (var t in tokens)
        {
            // 괄호 폼 표기는 그대로 둠: "싸리용(뻗은,젖힌)"
            var key = t.Contains('(') ? t[..t.IndexOf('(')] : t;
            var tail = t.Contains('(') ? t[t.IndexOf('(')..] : "";
            if (!TryAlias(key, out var v)) { foreach (var r in results) r.Add(t); continue; }
            var alts = v.Split('/').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (alts.Count <= 1) { foreach (var r in results) r.Add(v + tail); continue; }
            results = results.SelectMany(r => alts.Select(a => new List<string>(r) { a + tail })).Take(30).ToList();
        }
        return results.Select(r => string.Join(" ", r));
    }
    private static readonly Dictionary<string, string> BallAlias = new()
    {
        ["럽볼"] = "러브러브볼", ["러브"] = "러브러브볼", ["러브볼"] = "러브러브볼", ["럽럽볼"] = "러브러브볼", ["렙볼"] = "레벨볼", ["레벨"] = "레벨볼", ["프볼"] = "프리미어볼", ["프레미어"] = "프리미어볼", ["프레미어볼"] = "프리미어볼", ["프리미어"] = "프리미어볼", ["다볼"] = "다이브볼", ["다이브"] = "다이브볼",
        ["하볼"] = "하이퍼볼", ["하이퍼"] = "하이퍼볼", ["몬볼"] = "몬스터볼", ["수볼"] = "슈퍼볼", ["수퍼볼"] = "슈퍼볼", ["슈볼"] = "슈퍼볼", ["문볼"] = "문볼", ["헤비"] = "헤비볼", ["루어"] = "루어볼",
        ["프렌드"] = "프렌드볼", ["스피드"] = "스피드볼", ["드볼"] = "드림볼", ["드림"] = "드림볼", ["비스트"] = "울트라볼", ["비스트볼"] = "울트라볼", ["마볼"] = "마스터볼", ["사파리"] = "사파리볼", ["스포츠"] = "컴퍼티션볼", ["스포츠볼"] = "컴퍼티션볼", ["네스트"] = "네스트볼", ["프레셔스"] = "프레셔스볼",
        ["럭볼"] = "럭셔리볼", ["울볼"] = "울트라볼", ["비볼"] = "울트라볼", ["럭셔"] = "럭셔리볼", ["퀵"] = "퀵볼", ["다크"] = "다크볼", ["힐"] = "힐볼", ["네트"] = "네트볼", ["타이머"] = "타이머볼", ["리피트"] = "리피트볼", ["럭셔리"] = "럭셔리볼", ["넷볼"] = "네트볼",
    };
    // 크기 표기 → 배율 범위 (PKHeX 크기 등급 기준: XS 0~24, S 25~59, M 60~195, L 196~230, XL 231~255)
    private static readonly (string[] Words, int Min, int Max, string Name)[] Sizes =
    [
        (["가장작게", "가장작은", "최소", "최소사이즈", "최소크기", "xxs", "초소형", "제일작게", "제일작은", "최저크기", "미니"], 0, 0, "최소 크기(배율 0)"),
        (["가장크게", "가장큰", "최대", "최대사이즈", "최대크기", "xxl", "초대형", "제일크게", "제일큰", "점보", "거대"], 255, 255, "최대 크기(배율 255)"),
        (["xs", "xs사이즈"], 0, 24, "XS"), (["s", "s사이즈", "작게"], 25, 59, "S"), (["m", "m사이즈", "보통"], 60, 195, "M"),
        (["l", "l사이즈", "크게"], 196, 230, "L"), (["xl", "xl사이즈"], 231, 255, "XL"),
    ];

    /// <summary>기본 줄임말·별칭. 값의 "/"는 여러 마리로 나눔 (예: 미라코라 → 미라이돈 / 코라이돈).</summary>
    public static readonly Dictionary<string, string> BuiltInAlias = new()
    {
        ["영꽃"] = "플라엣테 영원의꽃", ["영원의꽃플라엣테"] = "플라엣테 영원의꽃", ["알나인"] = "나인테일 알로라", ["알로라나인"] = "나인테일 알로라", ["히검귀"] = "대검귀 히스이", ["히스이검귀"] = "대검귀 히스이",
        ["달투곰"] = "다투곰 붉은달", ["붉은달다투곰"] = "다투곰 붉은달",
        ["날치머"] = "날개치는머리", ["땅기날"] = "땅을기는날개", ["고동달"] = "고동치는달", ["위대엄니"] = "위대한엄니", ["우렁꼬리"] = "우렁찬꼬리",
        ["미라코라"] = "미라이돈 / 코라이돈", ["코라미라"] = "코라이돈 / 미라이돈",
        ["자시자마"] = "자시안 / 자마젠타", ["자마자시"] = "자마젠타 / 자시안", ["자마젠"] = "자마젠타",
        ["백마"] = "버드렉스 백마탄모습", ["흑마"] = "버드렉스 흑마탄모습", ["백마버드렉스"] = "버드렉스 백마탄모습", ["흑마버드렉스"] = "버드렉스 흑마탄모습",
        ["일격우라오스"] = "우라오스 일격의태세", ["연격우라오스"] = "우라오스 연격의태세", ["일격"] = "일격의태세", ["연격"] = "연격의태세",
        ["한카"] = "한카리아스", ["망나"] = "망나뇽", ["보만"] = "보만다", ["메타그"] = "메타그로스", ["마기"] = "마기라스",
        ["테파"] = "테라파고스", ["모모"] = "복숭악동"
    };

    /// <summary>사용자 사전 + 기본 사전으로 단어를 바꿔 씀. 여러 마리 값("/")은 그 줄을 여러 줄로 복제. 바꾼 결과는 다시 바꾸지 않음.</summary>
    private static List<string> ApplyAlias(string part, IReadOnlyDictionary<string, string> user)
    {
        var results = new List<List<string>> { new() };
        foreach (var tok in Tokenize(part))
        {
            var key = Norm(tok); string val = null;
            if (user != null) foreach (var kv in user) if (Norm(kv.Key) == key) { val = kv.Value; break; }
            if (val == null) foreach (var kv in BuiltInAlias) if (Norm(kv.Key) == key) { val = kv.Value; break; }
            if (val == null) { foreach (var r in results) r.Add(tok); continue; }
            var alts = val.Split('/').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (alts.Count == 0) continue;
            var next = new List<List<string>>();
            foreach (var r in results) foreach (var a in alts) { var c = r.ToList(); c.Add(a); next.Add(c); }
            results = next;
        }
        return results.Select(r => string.Join(" ", r)).ToList();
    }

    public static List<OrderSpec> Parse(string text, SaveFile sav, IReadOnlyDictionary<string, string> userAlias = null)
    {
        var s = GameInfo.Strings;
        var result = new List<OrderSpec>();
        var global = new OrderSpec(); var allSpec = new OrderSpec();
        foreach (var rawLine in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            // 공통 줄: "어버이: 새아 468686", "공통 어버이 새아 / TID 468686"
            var allM = Regex.Match(line, @"^(모두|전부|전체|다)\s*[:：]?\s*(.+)$");
            if (allM.Success) { ApplyTokens(allSpec, Tokenize(allM.Groups[2].Value), sav, false); continue; }
            if (Regex.IsMatch(line, @"^(공통|어버이\s*[:：]|tn\s*[:：])", RegexOptions.IgnoreCase))
            { ApplyTokens(global, Tokenize(line.Replace("공통", "")), sav, onlyTrainer: true); continue; }
            foreach (var part0 in SplitEntries(line))
            foreach (var part in ApplyAlias(part0, userAlias))
                foreach (var expanded in ExpandForms(part))
                {
                    var o = new OrderSpec { Raw = expanded.Text, OT = global.OT, TID = global.TID, SID = global.SID, OTGender = global.OTGender };
                    ApplyTokens(o, Tokenize(expanded.Text), sav, false);
                    if (expanded.Form != null) ResolveFormWord(o, expanded.Form, sav);
                    if (o.Species == 0) o.Notes.Add("포켓몬을 알아보지 못했습니다");
                    result.Add(o);
                }
        }
        // "모두 암컷", "전부 울볼" 등: 적힌 항목만 모든 포켓몬에 덮어씀 (줄 위치 무관)
        foreach (var o in result)
        {
            if (allSpec.Gender >= 0) o.Gender = allSpec.Gender;
            if (allSpec.Shiny != 0) o.Shiny = allSpec.Shiny;
            if (allSpec.Alpha >= 0) o.Alpha = allSpec.Alpha;
            if (allSpec.Ball > 0) o.Ball = allSpec.Ball;
            if (allSpec.Nature >= 0) o.Nature = allSpec.Nature;
            if (allSpec.Ability >= 0) o.Ability = allSpec.Ability;
            if (allSpec.ScaleMin >= 0) { o.ScaleMin = allSpec.ScaleMin; o.ScaleMax = allSpec.ScaleMax; o.SizeText = allSpec.SizeText; }
            if (allSpec.Mark >= 0) o.Mark = allSpec.Mark;
            if (allSpec.FlawlessIVs >= 0) o.FlawlessIVs = allSpec.FlawlessIVs;
            if (allSpec.Level > 0) o.Level = allSpec.Level;
            if (allSpec.Event) o.Event = true;
            if (allSpec.OT != null) o.OT = allSpec.OT; if (allSpec.TID >= 0) o.TID = allSpec.TID; if (allSpec.SID >= 0) o.SID = allSpec.SID;
        }
        return result;
    }

    // "/" 로 나누되 "어버이 새아 / TID" 같은 공통 줄은 위에서 처리
    private static IEnumerable<string> SplitEntries(string line) { var t = line.Trim(); if (t.Length > 0) yield return t; }   // 한 줄 = 한 마리 ('/'는 구분자가 아님)

    // "싸리용(뻗은,젖힌) 우두로치" → ("싸리용 우두로치", 뻗은), ("싸리용 우두로치", 젖힌)
    private static IEnumerable<(string Text, string Form)> ExpandForms(string part)
    {
        var m = Regex.Match(part, @"\(([^)]*)\)");
        if (!m.Success) { yield return (part, null); yield break; }
        if (m.Groups[1].Value.Contains('/'))   // "에써르 (수/ 작증 / 럽볼)" → 괄호 안은 속성
        {
            yield return ((part[..m.Index] + " " + m.Groups[1].Value.Replace('/', ' ') + " " + part[(m.Index + m.Length)..]).Trim(), null); yield break;
        }
        if (m.Groups[1].Value.Contains("진화"))   // "나오하(마스카나진화)" → 나오하 다음에 진화 대상
        {
            var target = m.Groups[1].Value.Replace("진화형", "").Replace("진화", "").Replace("까지", "").Trim();
            yield return ((part[..m.Index] + " " + target + " " + part[(m.Index + m.Length)..]).Trim(), null); yield break;
        }
        var rest = (part[..m.Index] + " " + part[(m.Index + m.Length)..]).Trim();
        var forms = m.Groups[1].Value.Split(',', '，', '、').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (forms.Count == 0) { yield return (rest, null); yield break; }
        foreach (var f in forms) yield return (rest, f);
    }

    private static List<string> Tokenize(string t)
    {
        t = Regex.Replace(t, @"[,，:：/]", " ");   // '/'는 구분용으로만 쓰고 무시
        return t.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static string Norm(string x) => x.Trim().ToLowerInvariant().Replace(" ", "");

    private static void ApplyTokens(OrderSpec o, List<string> tokens, SaveFile sav, bool onlyTrainer)
    {
        var s = GameInfo.Strings;
        for (int i = 0; i < tokens.Count; i++)
        {
            var raw = tokens[i]; var t = Norm(raw);
            // 어버이·트레이너
            if (t is "어버이" or "어버이작" or "tn" or "ot" or "트레이너")
            {
                if (i + 1 < tokens.Count && !IsNumber(tokens[i + 1]) && Norm(tokens[i + 1]) is not ("tid" or "sid")) { o.OT = tokens[++i]; }
                if (i + 1 < tokens.Count && IsNumber(tokens[i + 1])) { o.TID = int.Parse(tokens[++i]); if (i + 1 < tokens.Count && IsNumber(tokens[i + 1]) && tokens[i + 1].Length <= 5) o.SID = int.Parse(tokens[++i]); }
                continue;
            }
            if (t is "tid" or "id" && i + 1 < tokens.Count && IsNumber(tokens[i + 1])) { o.TID = int.Parse(tokens[++i]); continue; }
            if (t == "sid" && i + 1 < tokens.Count && IsNumber(tokens[i + 1])) { o.SID = int.Parse(tokens[++i]); continue; }
            if (onlyTrainer)
            {
                if (IsNumber(raw)) { if (o.TID < 0) o.TID = int.Parse(raw); else o.SID = int.Parse(raw); continue; }
                if (t is "여" or "여자" or "♀") { o.OTGender = 1; continue; }
                if (t is "남" or "남자" or "♂") { o.OTGender = 0; continue; }
                if (o.OT == null) o.OT = raw;
                continue;
            }
            if (t is "배포" or "이벤트" or "배포판" or "배포개체") { o.Event = true; continue; }
            if (t is "진화" or "진화형" or "진화체" or "→" or "->") continue;
            if (TryToken(o, raw, t, sav)) continue;
            // 붙여 쓴 말 나누기 시도: "러브볼암컷" 같은 경우 앞에서부터 가장 긴 단어
            if (!SplitCompound(o, raw, sav)) o.Unknown.Add(raw);
        }
        // 배포: 알아보지 못한 단어는 배포를 고르는 단서로 ("리코의" → 리코)
        if (o.Event) { foreach (var u in o.Unknown) o.Keywords.Add(u.EndsWith("의") && u.Length > 1 ? u[..^1] : u); o.Unknown.Clear(); }
        // 종보다 앞에 적힌 폼 단어 ("붉은달 다투곰")는 종을 알게 된 뒤 다시 맞춰 봄
        if (o.Species != 0) foreach (var u in o.Unknown.ToList()) if (ResolveFormWord(o, u, sav, quiet: true)) o.Unknown.Remove(u);
    }

    private static bool IsNumber(string x) => Regex.IsMatch(x, @"^\d{1,6}$");

    private static bool SplitCompound(OrderSpec o, string raw, SaveFile sav)
    {
        for (int cut = raw.Length - 1; cut >= 1; cut--)
        {
            var a = raw[..cut]; var b = raw[cut..];
            var tmp = new OrderSpec();
            if (TryToken(tmp, a, Norm(a), sav) && TryToken(tmp, b, Norm(b), sav)) { TryToken(o, a, Norm(a), sav); TryToken(o, b, Norm(b), sav); return true; }
        }
        return false;
    }

    private static bool TryToken(OrderSpec o, string raw, string t, SaveFile sav)
    {
        var s = GameInfo.Strings;
        // 성별
        if (t is "암" or "암컷" or "♀" or "여" or "f" or "female") { o.Gender = 1; return true; }
        if (t is "아내" or "와이프" or "마누라")   // 재미: 그 게임에 나오는 무작위 암컷 포켓몬
        {
            var cands = new List<ushort>();
            for (ushort wsp = 1; wsp <= sav.MaxSpeciesID; wsp++)
            {
                if (!sav.Personal.IsSpeciesInGame(wsp)) continue;
                var wpi = sav.Personal.GetFormEntry(wsp, 0);
                if (wpi.Genderless || wpi.OnlyMale) continue;
                cands.Add(wsp);
            }
            if (cands.Count > 0 && o.Species == 0) { o.Species = cands[Random.Shared.Next(cands.Count)]; o.Notes.Add($"아내: {GameInfo.Strings.Species[o.Species]} 💕"); }
            o.Gender = 1; return true;
        }
        if (t is "수" or "수컷" or "♂" or "남" or "m♂" or "male") { o.Gender = 0; return true; }
        // 이로치·우두머리
        if (t is "우두로치" or "색우두" or "우두이로치" or "알파이로치") { o.Alpha = 1; o.Shiny = Math.Max(o.Shiny, 1); return true; }
        if (t is "일반우두" or "우두" or "우두머리" or "알파") { o.Alpha = 1; return true; }
        if (t is "일로치" or "이로치" or "색이다른" or "색違い" or "shiny") { if (o.Shiny == 0) o.Shiny = 1; return true; }
        if (t is "별" or "별이로치" or "별로치") { o.Shiny = 3; return true; }
        if (t is "네모" or "스퀘어" or "네모이로치" or "네모로치") { o.Shiny = 4; return true; }
        if (t is "일반" or "일반색" or "노말색") { if (o.Alpha < 0) o.Alpha = 0; o.Shiny = 2; return true; }
        // 크기·증표
        if (t is "가장" or "제일" or "최고로" or "완전") { o.Superl = true; return true; }
        if (t is "크기" or "사이즈" or "size" or "짜리") return true;
        if (o.Superl && t is "크게" or "큰" or "크기의" or "큰것") { o.Superl = false; o.ScaleMin = o.ScaleMax = 255; o.SizeText = "최대 크기(배율 255)"; return true; }
        if (o.Superl && t is "작게" or "작은" or "작은것") { o.Superl = false; o.ScaleMin = o.ScaleMax = 0; o.SizeText = "최소 크기(배율 0)"; return true; }
        foreach (var (words, min, max, name) in Sizes)
            if (words.Contains(t)) { o.ScaleMin = min; o.ScaleMax = max; o.SizeText = name; return true; }
        if (t is "작증" or "작은증표" or "최소증표" or "최소사이즈증표" or "미니증표") { o.Mark = (int)RibbonIndex.MarkMini; o.ScaleMin = o.ScaleMax = 0; o.SizeText = "최소 크기(배율 0)"; return true; }
        if (t is "큰증" or "큰증표" or "최대증표" or "최대사이즈증표" or "점보증표") { o.Mark = (int)RibbonIndex.MarkJumbo; o.ScaleMin = o.ScaleMax = 255; o.SizeText = "최대 크기(배율 255)"; return true; }
        if (t is "증표" or "증")   // 앞의 크기와 합쳐 작은·큰 증표
        {
            if (o.ScaleMax == 0) { o.Mark = (int)RibbonIndex.MarkMini; return true; }
            if (o.ScaleMin == 255) { o.Mark = (int)RibbonIndex.MarkJumbo; return true; }
            o.Notes.Add("어떤 증표인지 알 수 없어 무시했습니다");
            return true;
        }
        if (t is "카레" or "카레증표" or "카레의증표") { o.Mark = (int)RibbonIndex.MarkCurry; return true; }
        if (t is "최강" or "최강증표" or "최강의증표" or "최강의") { o.Mark = (int)RibbonIndex.MarkMightiest; return true; }
        if (t is "낚시" or "낚시증표" or "낚시의증표") { o.Mark = (int)RibbonIndex.MarkFishing; return true; }
        foreach (var r in Enum.GetValues<RibbonIndex>().Where(x => x.ToString().StartsWith("Mark")))
            if (s.Ribbons.GetNameSafe("Ribbon" + r, out var kn) && (Norm(kn) == t || Norm(kn).Replace("의", "") == t)) { o.Mark = (int)r; return true; }
        // 개체값·레벨·개수
        var mv = Regex.Match(t, @"^([0-6])v$"); if (mv.Success) { o.FlawlessIVs = int.Parse(mv.Groups[1].Value); return true; }
        var ms = Regex.Match(t, @"^([habcds])(\d{1,2})$"); if (ms.Success) { o.ExactIV["habcds".IndexOf(ms.Groups[1].Value)] = Math.Min(31, int.Parse(ms.Groups[2].Value)); return true; }
        var ml = Regex.Match(t, @"^(?:lv\.?|레벨)(\d{1,3})$|^(\d{1,3})(?:렙|레벨|lv)$"); if (ml.Success) { o.Level = Math.Clamp(int.Parse(ml.Groups[1].Success ? ml.Groups[1].Value : ml.Groups[2].Value), 1, 100); return true; }
        var mc = Regex.Match(t, @"^(?:x|×)(\d{1,2})$|^(\d{1,2})(?:마리|개)$"); if (mc.Success) { o.Count = Math.Clamp(int.Parse(mc.Groups[1].Success ? mc.Groups[1].Value : mc.Groups[2].Value), 1, 30); return true; }
        if (t is "숨특" or "숨겨진특성" or "드림특성" or "숨") { o.Ability = 2; return true; }
        // 볼
        var ballName = BallAlias.TryGetValue(t, out var al) ? al : t;
        for (int b = 1; b < s.balllist.Length; b++)
            if (!string.IsNullOrEmpty(s.balllist[b]) && (Norm(s.balllist[b]) == Norm(ballName) || Norm(s.balllist[b]) == Norm(ballName) + "볼")) { o.Ball = b; return true; }
        // 성격 ("고집", "고집성격")
        var nt = t.EndsWith("성격") ? t[..^2] : t;
        for (int n = 0; n < 25 && n < s.natures.Length; n++) if (Norm(s.natures[n]) == nt) { o.Nature = n; return true; }
        // 포켓몬 (리전폼 접두어: 가라르·알로라·히스이·팔데아)
        foreach (var region in new[] { "가라르", "알로라", "히스이", "팔데아" })
            if (t.StartsWith(region) && t.Length > region.Length && FindSpecies(t[region.Length..], out var sr)) { o.Species = sr; ResolveFormWord(o, region, sav); return true; }
            else if (t == region) { o.FormText = region; if (o.Species != 0) ResolveFormWord(o, region, sav); else o.Notes.Add($"{region}(폼)"); return true; }
        if (FindSpecies(t, out var sp)) { if (o.Species != 0 && o.Species != sp) o.PreSpecies = o.Species; o.Species = sp; if (o.FormText.Length > 0 && o.Form < 0) ResolveFormWord(o, o.FormText, sav); return true; }
        // 특성 이름
        if (o.Species != 0)
        {
            for (int a = 1; a < s.abilitylist.Length; a++)
                if (Norm(s.abilitylist[a]) == t)
                {
                    try { var pi = sav.Personal.GetFormEntry(o.Species, (byte)Math.Max(0, o.Form)); for (int k = 0; k < pi.AbilityCount; k++) if (pi.GetAbilityAtIndex(k) == a) { o.Ability = k; return true; } } catch { }
                    o.Notes.Add($"{s.abilitylist[a]}: 이 포켓몬의 특성이 아닙니다"); return true;
                }
            // 폼 단어 ("뻗은", "붉은달")
            if (ResolveFormWord(o, raw, sav, quiet: true)) return true;
        }
        return false;
    }

    private static bool FindSpecies(string t, out ushort sp)
    {
        var ko = GameInfo.Strings.Species; var en = GameInfo.GetStrings("en").Species;
        for (ushort i = 1; i < ko.Count; i++) if (Norm(ko[i]) == t || Norm(en[i]) == t) { sp = i; return true; }
        sp = 0; return false;
    }

    /// <summary>폼 단어를 그 포켓몬의 폼 이름과 맞춰 봄 (부분 일치).</summary>
    private static bool ResolveFormWord(OrderSpec o, string word, SaveFile sav, bool quiet = false)
    {
        if (o.Species == 0) { o.FormText = word; return false; }
        var s = GameInfo.Strings; string[] names;
        try { names = FormConverter.GetFormList(o.Species, s.types, s.forms, GameInfo.GenderSymbolUnicode, sav.Context); } catch { return false; }
        var w = Norm(word).Replace("모습", "").Replace("폼", "").Replace("의", "");
        if (w.Length == 0) return false;
        for (int f = 0; f < names.Length; f++)
        {
            var n = Norm(names[f]).Replace("의", "");
            if (n.Length > 0 && (n.Contains(w) || w.Contains(n.Replace("모습", "").Replace("폼", "")) && n.Replace("모습", "").Replace("폼", "").Length > 0)) { o.Form = f; o.FormText = names[f].Trim(); return true; }
        }
        if (!quiet) o.Notes.Add($"'{word}' 폼을 찾지 못했습니다");
        return false;
    }
}
