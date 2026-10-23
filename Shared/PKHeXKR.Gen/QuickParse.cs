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
    public int[] EV = [-1, -1, -1, -1, -1, -1];   // 노력치 (H A B C D S)
    public int Level = -1, Count = 1;
    public string OT; public int TID = -1, SID = -1, OTGender = -1;
    public bool Event;                      // "배포" → 배포에서만 찾음
    public List<string> Keywords = [];      // 배포를 고를 단서 ("리코의" → 리코)
    public ushort PreSpecies;               // "나오하(마스카나진화)"의 나오하
    public bool Superl;                     // "가장"·"제일" 다음 크기 단어는 최대·최소
    public List<string> Unknown = [];
    public List<string> Notes = [];
    public bool AllowTransfer;              // 리빙덱스: 다른 게임에서 옮겨 온 개체도 허용 (HOME 등)
    public double Seconds = 4;              // 조우 하나당 최대 시도 시간
    public bool SkipAlm;                    // 조우로 못 만들어도 자동 합법화로 넘어가지 않음 (리빙덱스: 이미 해 봄)
    public bool Gmax;                       // 거다이맥스
    public bool HyperTrain;                 // 특훈 (병뚜껑): 31이 아닌 능력치 모두
    public int Tera = -1;                   // 테라스탈 타입 (0~17, 99 스텔라)
    public List<ushort> Moves = [];         // 기술 (최대 4개)
    public List<string> Ribbons = [];       // 리본·증표 속성 이름 ("RibbonMarkCurry", "RibbonChampionKalos"…) — 적은 것 모두
    public DateOnly? MetFrom, MetTo;        // 만난 날짜 (같으면 그 날, 다르면 구간 안에서)

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
        var rib = Ribbons.Count > 0 ? Ribbons : Mark >= 0 ? ["Ribbon" + (RibbonIndex)Mark] : [];
        if (rib.Count > 0) p.Add(string.Join("·", rib.Select(n => s.Ribbons.GetNameSafe(n, out var k) ? k : n.Replace("Ribbon", ""))));
        if (MetFrom is { } mf) p.Add("만난 날짜 " + (MetTo is { } mt && mt != mf ? $"{mf:yyyy-MM-dd}~{mt:yyyy-MM-dd}" : $"{mf:yyyy-MM-dd}"));
        if (FlawlessIVs > 0) p.Add($"{FlawlessIVs}V");
        var ex = string.Join(" ", ExactIV.Select((v, i) => v < 0 ? "" : $"{"HABCDS"[i]}{v}").Where(x => x.Length > 0)); if (ex.Length > 0) p.Add("개체값 " + ex);
        var evs = string.Join(" ", EV.Select((v, i) => v < 0 ? "" : $"{"HABCDS"[i]}{v}").Where(x => x.Length > 0)); if (evs.Length > 0) p.Add("노력치 " + evs);
        if (Level > 0) p.Add($"Lv{Level}");
        if (Gmax) p.Add("거다이맥스");
        if (HyperTrain) p.Add("특훈");
        if (Tera >= 0) p.Add((Tera == 99 ? "스텔라" : Tera < s.types.Length ? s.types[Tera] : "?") + " 테라");
        if (Moves.Count > 0) p.Add("기술 " + string.Join("·", Moves.Select(m => m < s.movelist.Length ? s.movelist[m] : "?")));
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
        ["콤페볼"] = "컴퍼티션볼", ["콤페"] = "컴퍼티션볼", ["컴페볼"] = "컴퍼티션볼", ["컴퍼티션"] = "컴퍼티션볼", ["콤퍼티션볼"] = "컴퍼티션볼", ["컴피티션볼"] = "컴퍼티션볼",
        ["몬스터"] = "몬스터볼", ["몬스볼"] = "몬스터볼", ["포케볼"] = "몬스터볼", ["포켓볼"] = "몬스터볼",
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
    public static readonly Dictionary<string, string> BuiltInAlias = WithRotom(new()
    {
        ["영꽃"] = "플라엣테 영원의꽃", ["영원의꽃플라엣테"] = "플라엣테 영원의꽃", ["알나인"] = "나인테일 알로라", ["알로라나인"] = "나인테일 알로라", ["히검귀"] = "대검귀 히스이", ["히스이검귀"] = "대검귀 히스이",
        ["달투곰"] = "다투곰 붉은달", ["붉은달다투곰"] = "다투곰 붉은달",
        ["날치머"] = "날개치는머리", ["땅기날"] = "땅을기는날개", ["고동달"] = "고동치는달", ["위대엄니"] = "위대한엄니", ["우렁꼬리"] = "우렁찬꼬리",
        ["미라코라"] = "미라이돈 / 코라이돈", ["코라미라"] = "코라이돈 / 미라이돈",
        ["자시자마"] = "자시안 / 자마젠타", ["자마자시"] = "자마젠타 / 자시안", ["자마젠"] = "자마젠타",
        ["백마"] = "버드렉스 백마탄모습", ["흑마"] = "버드렉스 흑마탄모습", ["백마버드렉스"] = "버드렉스 백마탄모습", ["흑마버드렉스"] = "버드렉스 흑마탄모습",
        ["일격우라오스"] = "우라오스 일격의태세", ["연격우라오스"] = "우라오스 연격의태세", ["일격"] = "일격의태세", ["연격"] = "연격의태세",
        ["한카"] = "한카리아스", ["망나"] = "망나뇽", ["보만"] = "보만다", ["메타그"] = "메타그로스", ["마기"] = "마기라스",
        ["테파"] = "테라파고스", ["모모"] = "복숭악동",
        ["암써르"] = "에써르 암", ["수써르"] = "에써르 수", ["암컷에써르"] = "에써르 암", ["수컷에써르"] = "에써르 수",
        ["크세"] = "크레세리아",
    });

    /// <summary>로토무 폼 별칭: 물로톰·워시로토무 → 로토무 워시 (불·얼음·비행·풀 폼도).</summary>
    private static Dictionary<string, string> WithRotom(Dictionary<string, string> d)
    {
        var forms = new (string Form, string[] Nick)[]
        {
            ("히트", ["불", "불꽃", "히트", "전자레인지", "렌지"]),
            ("워시", ["물", "워시", "세탁기"]),
            ("프로스트", ["얼음", "얼", "냉", "냉동", "냉장고", "프로스트"]),
            ("스핀", ["비행", "바람", "선풍기", "스핀"]),
            ("커트", ["풀", "잔디", "예초기", "커트", "컷"]),
        };
        foreach (var (form, nicks) in forms)
            foreach (var n in nicks)
                foreach (var tail in new[] { "로톰", "로토무", "토무" })
                    d.TryAdd(n + tail, "로토무 " + form);
        d.TryAdd("일반로토무", "로토무"); d.TryAdd("기본로토무", "로토무");
        return d;
    }

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
        var s = GenHost.Strings;
        var result = new List<OrderSpec>();
        var global = new OrderSpec(); var allSpec = new OrderSpec();
        foreach (var rawLine in ExpandRanges((text ?? "").Replace("\r", "").Split('\n'), sav))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            // 공통 줄: "어버이: 새아 468686", "공통 어버이 새아 / TID 468686"
            // 띄어쓰기나 ':'가 있어야 공통 줄 ("다 암컷") — 없으면 다투곰·다크라이·다부니처럼 '다'로 시작하는 이름이 공통 줄로 잡혔음
            var allM = Regex.Match(line, @"^(모두|전부|전체|다)(\s*[:：]\s*|\s+)(.+)$");
            if (allM.Success) { ApplyTokens(allSpec, Tokenize(allM.Groups[3].Value), sav, false); continue; }
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
            if (allSpec.Mark >= 0 && o.Mark < 0) o.Mark = allSpec.Mark;
            foreach (var r in allSpec.Ribbons) if (!o.Ribbons.Contains(r)) o.Ribbons.Add(r);
            if (allSpec.MetFrom != null) { o.MetFrom = allSpec.MetFrom; o.MetTo = allSpec.MetTo; }
            if (allSpec.FlawlessIVs >= 0) o.FlawlessIVs = allSpec.FlawlessIVs;
            for (int k = 0; k < 6; k++) { if (allSpec.EV[k] >= 0) o.EV[k] = allSpec.EV[k]; if (allSpec.ExactIV[k] >= 0) o.ExactIV[k] = allSpec.ExactIV[k]; }
            if (allSpec.Level > 0) o.Level = allSpec.Level;
            if (allSpec.Gmax) o.Gmax = true;
            if (allSpec.HyperTrain) o.HyperTrain = true;
            if (allSpec.Tera >= 0) o.Tera = allSpec.Tera;
            foreach (var m in allSpec.Moves) if (o.Moves.Count < 4 && !o.Moves.Contains(m)) o.Moves.Add(m);
            if (allSpec.Event) o.Event = true;
            if (allSpec.OT != null) o.OT = allSpec.OT; if (allSpec.TID >= 0) o.TID = allSpec.TID; if (allSpec.SID >= 0) o.SID = allSpec.SID;
        }
        return result;
    }

    /// <summary>
    /// 범위 주문: "리빙덱스 1~151 이로치 프볼", "도감 152-251 럽볼", "1~1025번 모두", "리빙덱스 전체 이로치"
    /// → 그 번호 범위에서 이 게임에 나오는 종마다 한 줄("이상해씨 이로치 프볼", …)로 풀어 씀.
    /// </summary>
    public static IEnumerable<string> ExpandRanges(IEnumerable<string> lines, SaveFile sav)
    {
        var s = GenHost.Strings;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            // '모두 암컷' 같은 공통 줄과 겹치지 않게: '전체'는 리빙덱스·도감 뒤에 올 때만 범위
            // 리빙덱스·도감·전국도감 뒤에 번호 범위가 없으면 이 게임에 나오는 종 전체 ("전국도감 이로치", "도감 럽볼")
            var m = Regex.Match(line, @"^(?:(?:리빙\s*덱스|리빙\s*도감|전국\s*도감|도감|dex)(?:\s*(\d{1,4})\s*(?:~|∼|〜|-|–|부터)\s*(\d{1,4})\s*(?:번|까지|번까지)?|\s*(전체|전부|모두|all)|(?=\s|$))|(\d{1,4})\s*(?:~|∼|〜|-|–|부터)\s*(\d{1,4})\s*(?:번|까지|번까지)?)\s*(.*)$", RegexOptions.IgnoreCase);
            if (!m.Success || Regex.IsMatch(line, @"^\d{4}\s*[-./]\s*\d{1,2}")) { yield return raw; continue; }   // 날짜("2024-03-05 …")는 범위가 아님
            int from, to;
            if (m.Groups[3].Success || !(m.Groups[1].Success || m.Groups[4].Success)) { from = 1; to = sav.MaxSpeciesID; }
            else
            {
                var g1 = m.Groups[1].Success ? m.Groups[1] : m.Groups[4]; var g2 = m.Groups[2].Success ? m.Groups[2] : m.Groups[5];
                from = int.Parse(g1.Value); to = int.Parse(g2.Value); if (from > to) (from, to) = (to, from);
            }
            if (from < 1) from = 1; if (to > sav.MaxSpeciesID) to = sav.MaxSpeciesID;
            if (to < from) { yield return raw; continue; }
            var rest = m.Groups[6].Value.Trim();
            int n = 0;
            for (int sp = from; sp <= to; sp++)
            {
                if (!sav.Personal.IsSpeciesInGame((ushort)sp)) continue;
                yield return (s.Species[sp] + " " + rest).Trim(); n++;
            }
            if (n == 0) yield return raw;
        }
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
        t = Dates(t);
        t = Regex.Replace(t, @"타입\s*[:：]?\s*널", "타입:널");   // 이름에 ':'가 든 포켓몬
        t = t.Replace("타입:널", "\u0001");
        t = Regex.Replace(t, @"([가-힣])의\s+증표", "$1의증표");   // "카레의 증표" = "카레의증표"
        t = Regex.Replace(t, @"[,，:：/]", " ");   // '/'는 구분용으로만 쓰고 무시
        t = t.Replace("\u0001", "타입:널");
        return t.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static string Norm(string x) => x.Trim().ToLowerInvariant().Replace(" ", "");

    private static void ApplyTokens(OrderSpec o, List<string> tokens, SaveFile sav, bool onlyTrainer)
    {
        var s = GenHost.Strings; bool range = false;
        for (int i = 0; i < tokens.Count; i++)
        {
            var raw = tokens[i]; var t = Norm(raw);
            // 만난 날짜: "만난날짜 2024-01-05", "2024-01-01~2024-06-30", "2023년", "2024.3"
            if (DateWord(t) is { } dw)
            {
                if (range && o.MetFrom != null) { o.MetTo = dw.To; if (o.MetTo < o.MetFrom) (o.MetFrom, o.MetTo) = (o.MetTo, o.MetFrom); }
                else { o.MetFrom = dw.From; o.MetTo = dw.To; }
                range = false; continue;
            }
            if (t == "~") { range = true; continue; }
            if (t is "만난날짜" or "만난날" or "만난" or "날짜" or "만남" or "날" or "부터" or "까지" or "사이" or "metdate") continue;
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
            if (t is "기술" or "기술:" or "-" or "기술배치") continue;
            if (t is "테라" or "테라타입" or "테라스탈" && i + 1 < tokens.Count && TeraWord(Norm(tokens[i + 1])) is int tw) { o.Tera = tw; i++; continue; }
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
        var s = GenHost.Strings;
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
            if (cands.Count > 0 && o.Species == 0) { o.Species = cands[Random.Shared.Next(cands.Count)]; o.Notes.Add($"아내: {GenHost.Strings.Species[o.Species]} 💕"); }
            o.Gender = 1; return true;
        }
        if (t is "수" or "수컷" or "♂" or "남" or "m♂" or "male") { o.Gender = 0; return true; }
        // 이로치·우두머리
        if (t is "우두로치" or "색우두" or "우두이로치" or "알파이로치") { o.Alpha = 1; o.Shiny = Math.Max(o.Shiny, 1); return true; }
        if (t is "일반우두" or "우두" or "우두머리" or "알파") { o.Alpha = 1; return true; }
        if (t is "일로치" or "이로치" or "색이다른" or "색違い" or "shiny") { if (o.Shiny is 0 or 2) o.Shiny = 1; return true; }   // "일반 이로치" = 우두머리 아닌 이로치
        if (t is "별" or "별이로치" or "별로치") { o.Shiny = 3; return true; }
        if (t is "네모" or "스퀘어" or "네모이로치" or "네모로치") { o.Shiny = 4; return true; }
        if (t is "일반색" or "노말색") { if (o.Alpha < 0) o.Alpha = 0; o.Shiny = 2; return true; }
        if (t is "일반" or "일반개체" or "비우두") { if (o.Alpha < 0) o.Alpha = 0; if (o.Shiny == 0) o.Shiny = 2; return true; }   // 우두머리 아님 (이로치와 함께 쓰면 이로치)
        // 크기·증표
        if (t is "가장" or "제일" or "최고로" or "완전") { o.Superl = true; return true; }
        if (t is "크기" or "사이즈" or "size" or "짜리") return true;
        if (o.Superl && t is "크게" or "큰" or "크기의" or "큰것") { o.Superl = false; o.ScaleMin = o.ScaleMax = 255; o.SizeText = "최대 크기(배율 255)"; return true; }
        if (o.Superl && t is "작게" or "작은" or "작은것") { o.Superl = false; o.ScaleMin = o.ScaleMax = 0; o.SizeText = "최소 크기(배율 0)"; return true; }
        foreach (var (words, min, max, name) in Sizes)
            if (words.Contains(t)) { o.ScaleMin = min; o.ScaleMax = max; o.SizeText = name; return true; }
        if (t is "작증" or "작은증표" or "최소증표" or "최소사이즈증표" or "미니증표") { AddMark(o, RibbonIndex.MarkMini); o.ScaleMin = o.ScaleMax = 0; o.SizeText = "최소 크기(배율 0)"; return true; }
        if (t is "큰증" or "큰증표" or "최대증표" or "최대사이즈증표" or "점보증표") { AddMark(o, RibbonIndex.MarkJumbo); o.ScaleMin = o.ScaleMax = 255; o.SizeText = "최대 크기(배율 255)"; return true; }
        if (t is "증표" or "증")   // 앞의 크기와 합쳐 작은·큰 증표
        {
            if (o.ScaleMax == 0) { AddMark(o, RibbonIndex.MarkMini); return true; }
            if (o.ScaleMin == 255) { AddMark(o, RibbonIndex.MarkJumbo); return true; }
            o.Notes.Add("어떤 증표인지 알 수 없어 무시했습니다");
            return true;
        }
        if (t is "카레" or "카레증표" or "카레의증표") { AddMark(o, RibbonIndex.MarkCurry); return true; }
        if (t is "최강" or "최강증표" or "최강의증표" or "최강의") { AddMark(o, RibbonIndex.MarkMightiest); return true; }
        if (t is "낚시" or "낚시증표" or "낚시의증표") { AddMark(o, RibbonIndex.MarkFishing); return true; }
        foreach (var r in Enum.GetValues<RibbonIndex>().Where(x => x.ToString().StartsWith("Mark")))
            if (s.Ribbons.GetNameSafe("Ribbon" + r, out var kn) && (Norm(kn) == t || Norm(kn).Replace("의", "") == t)) { AddMark(o, r); return true; }
        foreach (var (words, mk) in MarkAlias)
            foreach (var w in words)
                if (t == w + "증표" || t == w + "의증표" || t == w + "증") { AddMark(o, mk); return true; }
        // 개체값·레벨·개수
        var mv = Regex.Match(t, @"^([0-6])v$"); if (mv.Success) { o.FlawlessIVs = int.Parse(mv.Groups[1].Value); return true; }
        // 개체값·노력치 구문: 'a0'·'s31' = 개체값, 'as252'·'h4'·'b252' = 노력치 (글자 여러 개, 32 이상, 또는 4)
        var ms = Regex.Match(t, @"^([habcds]+)(\d{1,3})$");
        if (ms.Success)
        {
            var letters = ms.Groups[1].Value; int val = int.Parse(ms.Groups[2].Value);
            bool ev = letters.Length > 1 || val > 31 || val == 4;
            foreach (var ch in letters.Distinct())
            {
                int idx = "habcds".IndexOf(ch);
                if (ev) o.EV[idx] = Math.Min(252, val); else o.ExactIV[idx] = Math.Min(31, val);
            }
            return true;
        }
        var ml = Regex.Match(t, @"^(?:lv\.?|레벨)(\d{1,3})$|^(\d{1,3})(?:렙|레벨|lv)$"); if (ml.Success) { o.Level = Math.Clamp(int.Parse(ml.Groups[1].Success ? ml.Groups[1].Value : ml.Groups[2].Value), 1, 100); return true; }
        var mc = Regex.Match(t, @"^(?:x|×)(\d{1,2})$|^(\d{1,2})(?:마리|개)$"); if (mc.Success) { o.Count = Math.Clamp(int.Parse(mc.Groups[1].Success ? mc.Groups[1].Value : mc.Groups[2].Value), 1, 30); return true; }
        if (t is "숨특" or "숨겨진특성" or "드림특성" or "숨") { o.Ability = 2; return true; }
        // 거다이맥스·테라스탈 타입 ("노말테라", "불테라", "테라페어리", "스텔라테라")
        if (t is "특훈" or "특훈완료" or "모두특훈" or "전부특훈" or "올특훈" or "풀특훈" or "6특훈" or "하이퍼트레이닝" or "병뚜껑" or "금병뚜껑" or "금색병뚜껑" or "ht" or "hypertrain" or "hypertrained") { o.HyperTrain = true; return true; }
        if (t is "거다이맥스" or "거다맥" or "거다이" or "거다이맥스개체" or "gmax" or "gigantamax" or "거대맥스") { o.Gmax = true; return true; }
        foreach (var suf in new[] { "테라타입", "테라스탈", "테라" })
        {
            if (t.EndsWith(suf) && t.Length > suf.Length && TeraWord(t[..^suf.Length]) is int ta) { o.Tera = ta; return true; }
            if (t.StartsWith(suf) && t.Length > suf.Length && TeraWord(t[suf.Length..]) is int tb) { o.Tera = tb; return true; }
        }
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
        }
        // 기술 이름 ("10만볼트", "thunderbolt")
        if (FindMove(t) is ushort mvid)
        {
            if (o.Moves.Count < 4 && !o.Moves.Contains(mvid)) o.Moves.Add(mvid);
            else if (!o.Moves.Contains(mvid)) o.Notes.Add($"기술은 4개까지: {s.movelist[mvid]} 무시");
            return true;
        }
        // 폼 단어 ("뻗은", "붉은달")
        if (o.Species != 0 && ResolveFormWord(o, raw, sav, quiet: true)) return true;
        // 리본 이름 ("챔피언리본", "절친리본") — 여러 개 적으면 모두
        if (t is "챔피언리본" or "챔피언" or "챔피언의리본" or "챔피리본" or "챔리본" && ChampionRibbon(sav) is { } champ && RibbonProps(sav.BlankPKM.GetType()).Contains(champ))
        { if (!o.Ribbons.Contains(champ)) o.Ribbons.Add(champ); return true; }
        if (FindRibbon(t, sav) is { } rib)
        {
            if (rib.StartsWith("RibbonMark") && Enum.TryParse<RibbonIndex>(rib[6..], out var ri)) AddMark(o, ri);
            else if (!o.Ribbons.Contains(rib)) o.Ribbons.Add(rib);
            return true;
        }
        return false;
    }

    // 증표 줄임말 ("비의증표"→강우의증표, "번개증표"→우레의증표 …)
    private static readonly (string[] Words, RibbonIndex Mark)[] MarkAlias =
    [
        (["비", "강우", "비오는", "레인"], RibbonIndex.MarkRainy), (["눈", "강설", "스노우"], RibbonIndex.MarkSnowy), (["폭설", "눈보라"], RibbonIndex.MarkBlizzard),
        (["번개", "천둥", "뇌우", "우레"], RibbonIndex.MarkStormy), (["흐림", "흐린", "구름"], RibbonIndex.MarkCloudy), (["건조", "쾌청", "맑음", "햇살", "가뭄"], RibbonIndex.MarkDry),
        (["모래", "모래바람", "모래먼지", "모래폭풍"], RibbonIndex.MarkSandstorm), (["안개", "짙은안개"], RibbonIndex.MarkMisty),
        (["정오", "점심", "낮"], RibbonIndex.MarkLunchtime), (["자정", "밤", "한밤"], RibbonIndex.MarkSleepyTime), (["황혼", "저녁", "노을"], RibbonIndex.MarkDusk), (["새벽", "아침"], RibbonIndex.MarkDawn),
        (["운명", "생일"], RibbonIndex.MarkDestiny), (["가끔", "가끔보는", "언커먼"], RibbonIndex.MarkUncommon), (["레어", "본적없는", "희귀"], RibbonIndex.MarkRare),
        (["우두머리", "알파"], RibbonIndex.MarkAlpha), (["주인", "타이탄", "누시"], RibbonIndex.MarkTitan), (["습득", "줍기"], RibbonIndex.MarkItemfinder),
        (["미식가", "미식"], RibbonIndex.MarkGourmand), (["파트너"], RibbonIndex.MarkPartner), (["낚시", "낚아올린"], RibbonIndex.MarkFishing),
        (["커다란", "점보", "큰"], RibbonIndex.MarkJumbo), (["조그만", "미니", "작은"], RibbonIndex.MarkMini),
    ];

    /// <summary>'챔피언리본' = 지금 게임 지방의 챔피언 리본.</summary>
    private static string ChampionRibbon(SaveFile sav) => sav.Context switch
    {
        EntityContext.Gen9 => "RibbonChampionPaldea", EntityContext.Gen8 => "RibbonChampionGalar", EntityContext.Gen8b or EntityContext.Gen4 => "RibbonChampionSinnoh",
        EntityContext.Gen7 => "RibbonChampionAlola", EntityContext.Gen6 => sav.Version is GameVersion.OR or GameVersion.AS ? "RibbonChampionG6Hoenn" : "RibbonChampionKalos",
        EntityContext.Gen3 => "RibbonChampionG3", _ => null,
    };

    // 증표 여러 개: 모두 Ribbons에 넣고, 조우를 정하는 증표(카레·낚시·날씨·최강)가 있으면 그것을 대표 증표(조우 검색용)로
    private static readonly HashSet<RibbonIndex> EncounterMarks =
    [
        RibbonIndex.MarkCurry, RibbonIndex.MarkFishing, RibbonIndex.MarkMightiest, RibbonIndex.MarkCloudy, RibbonIndex.MarkRainy, RibbonIndex.MarkStormy,
        RibbonIndex.MarkSnowy, RibbonIndex.MarkBlizzard, RibbonIndex.MarkDry, RibbonIndex.MarkSandstorm, RibbonIndex.MarkMisty,
    ];
    private static void AddMark(OrderSpec o, RibbonIndex m)
    {
        var name = "Ribbon" + m;
        if (!o.Ribbons.Contains(name)) o.Ribbons.Add(name);
        if (o.Mark < 0 || (EncounterMarks.Contains(m) && !EncounterMarks.Contains((RibbonIndex)o.Mark))) o.Mark = (int)m;
    }

    private static readonly Dictionary<Type, string[]> ribbonProps = [];
    /// <summary>이 게임 형식에 있는 리본·증표 속성 이름 (켜고 끄는 것만).</summary>
    public static string[] RibbonProps(Type pkm)
    {
        lock (ribbonProps)
            return ribbonProps.TryGetValue(pkm, out var v) ? v : ribbonProps[pkm] = pkm.GetProperties()
                .Where(x => x.Name.StartsWith("Ribbon") && x.PropertyType == typeof(bool) && x.CanWrite && x.GetSetMethod() != null).Select(x => x.Name).ToArray();
    }
    private static string FindRibbon(string t, SaveFile sav)
    {
        if (t.Length < 2) return null;
        var s = GenHost.Strings;
        foreach (var prop in RibbonProps(sav.BlankPKM.GetType()))
        {
            if (Norm(prop[6..]) == t) return prop;
            if (!s.Ribbons.GetNameSafe(prop, out var kn)) continue;
            var n = Norm(Regex.Replace(kn, @"\s*\([^)]*\)", ""));   // "쿨리본 (3세대)" → "쿨리본"
            if (n == t || n == t + "리본" || n.Replace("의", "") == t.Replace("의", "")) return prop;
        }
        return null;
    }

    /// <summary>날짜 표기를 한 단어로: 2024-01-05·2024.1.5·2024년 1월 5일 → @D20240105, 2024-03 → @M202403, 2024년 → @Y2024. '~'·'부터'는 구간.</summary>
    private static string Dates(string t)
    {
        static bool Ok(int y, int m, int d) => y is >= 1990 and <= 2099 && m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m);
        t = Regex.Replace(t, @"[~∼〜]", " ~ ");
        t = Regex.Replace(t, @"(?<!\d)(\d{4})\s*[-./년]\s*(\d{1,2})\s*[-./월]\s*(\d{1,2})\s*일?(?!\d)", m =>
        {
            int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), d = int.Parse(m.Groups[3].Value);
            return Ok(y, mo, d) ? $" @D{y:0000}{mo:00}{d:00} " : m.Value;
        });
        t = Regex.Replace(t, @"(?<![\d@A-Za-z])(\d{4})(\d{2})(\d{2})(?!\d)", m =>
        {
            int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), d = int.Parse(m.Groups[3].Value);
            return Ok(y, mo, d) ? $" @D{y:0000}{mo:00}{d:00} " : m.Value;
        });
        t = Regex.Replace(t, @"(?<![\d@A-Za-z])(\d{4})\s*[-./년]\s*(\d{1,2})\s*월?(?![\d.])", m =>
        {
            int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value);
            return Ok(y, mo, 1) ? $" @M{y:0000}{mo:00} " : m.Value;
        });
        t = Regex.Replace(t, @"(?<![\d@A-Za-z])(\d{4})\s*년", m => int.Parse(m.Groups[1].Value) is >= 1990 and <= 2099 ? $" @Y{m.Groups[1].Value} " : m.Value);
        t = Regex.Replace(t, @"(@[DMY]\d+)\s*(부터|에서)", "$1 ~ ");
        t = Regex.Replace(t, @"(@[DMY]\d+)\s*(까지|사이)", "$1 ");
        return t;
    }

    /// <summary>@D·@M·@Y 단어 → (처음 날, 마지막 날).</summary>
    private static (DateOnly From, DateOnly To)? DateWord(string t)
    {
        var m = Regex.Match(t, @"^@([dmy])(\d+)$");
        if (!m.Success) return null;
        var v = m.Groups[2].Value;
        try
        {
            switch (m.Groups[1].Value)
            {
                case "d" when v.Length == 8: { var d = new DateOnly(int.Parse(v[..4]), int.Parse(v[4..6]), int.Parse(v[6..])); return (d, d); }
                case "m" when v.Length == 6: { var d = new DateOnly(int.Parse(v[..4]), int.Parse(v[4..6]), 1); return (d, d.AddMonths(1).AddDays(-1)); }
                case "y" when v.Length == 4: { var d = new DateOnly(int.Parse(v), 1, 1); return (d, new DateOnly(d.Year, 12, 31)); }
            }
        }
        catch { }
        return null;
    }

    // ===== 일괄 수정(한국어)에서 쓰는 단어 찾기 =====
    public static ushort SpeciesOf(string w, IReadOnlyDictionary<string, string> user = null)
    {
        var t = Norm(w);
        if (FindSpecies(t, out var sp)) return sp;
        string v = null;
        if (user != null) foreach (var kv in user) if (Norm(kv.Key) == t) { v = kv.Value; break; }
        if (v == null && BuiltInAlias.TryGetValue(t, out var b)) v = b;
        return v != null && !v.Contains('/') && FindSpecies(Norm(v.Split(' ')[0]), out sp) ? sp : (ushort)0;
    }
    public static int BallOf(string w)
    {
        var t = Norm(w); var s = GenHost.Strings;
        var name = BallAlias.TryGetValue(t, out var al) ? al : t;
        for (int b = 1; b < s.balllist.Length; b++)
            if (!string.IsNullOrEmpty(s.balllist[b]) && (Norm(s.balllist[b]) == Norm(name) || Norm(s.balllist[b]) == Norm(name) + "볼")) return b;
        return -1;
    }
    public static int NatureOf(string w)
    {
        var t = Norm(w); if (t.EndsWith("성격")) t = t[..^2];
        var s = GenHost.Strings;
        for (int n = 0; n < 25 && n < s.natures.Length; n++) if (Norm(s.natures[n]) == t) return n;
        return -1;
    }
    public static int? TeraOf(string w) => TeraWord(Norm(w));

    private static readonly Dictionary<string, string> TypeAlias = new() { ["불"] = "불꽃", ["요정"] = "페어리", ["노멀"] = "노말", ["에스"] = "에스퍼", ["드래"] = "드래곤", ["드라곤"] = "드래곤", ["전"] = "전기", ["얼"] = "얼음", ["고스"] = "고스트", ["강"] = "강철" };
    /// <summary>타입 이름 → 테라스탈 타입 번호 (스텔라 99). 아니면 null.</summary>
    private static int? TeraWord(string w)
    {
        if (w.EndsWith("타입")) w = w[..^2];
        if (w.Length == 0) return null;
        if (w is "스텔라" or "stellar") return 99;
        if (TypeAlias.TryGetValue(w, out var a)) w = a;
        var ko = GenHost.Strings.types; var en = GameInfo.GetStrings("en").types;
        for (int k = 0; k < 18 && k < ko.Length; k++) if (Norm(ko[k]) == w || Norm(en[k]) == w) return k;
        return null;
    }

    private static ushort? FindMove(string t)
    {
        if (t.Length < 2) return null;
        var ko = GenHost.Strings.movelist; var en = GameInfo.GetStrings("en").movelist;
        for (int m = 1; m < ko.Length; m++) if (Norm(ko[m]) == t || (m < en.Length && Norm(en[m]).Replace("-", "") == t.Replace("-", ""))) return (ushort)m;
        return null;
    }

    private static bool FindSpecies(string t, out ushort sp)
    {
        var ko = GenHost.Strings.Species; var en = GameInfo.GetStrings("en").Species;
        for (ushort i = 1; i < ko.Count; i++) if (Norm(ko[i]) == t || Norm(en[i]) == t) { sp = i; return true; }
        sp = 0; return false;
    }

    /// <summary>폼 단어를 그 포켓몬의 폼 이름과 맞춰 봄 (부분 일치).</summary>
    private static bool ResolveFormWord(OrderSpec o, string word, SaveFile sav, bool quiet = false)
    {
        if (o.Species == 0) { o.FormText = word; return false; }
        var s = GenHost.Strings; string[] names;
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
