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

    public string Describe(GameStrings s) => Describe(s, Lexicon.Current);
    public string Describe(GameStrings s, Lexicon L)
    {
        var p = new List<string>();
        if (Event) p.Add(L.U("EVENT") + (Keywords.Count > 0 ? $"[{string.Join(" ", Keywords)}]" : ""));
        if (Species != 0) p.Add((PreSpecies != 0 && PreSpecies != Species ? s.Species[PreSpecies] + "→" : "") + s.Species[Species] + (FormText.Length > 0 ? $"({FormText})" : ""));
        if (Gender >= 0) p.Add(Gender == 0 ? "♂" : "♀");
        if (Alpha == 1) p.Add(L.U("ALPHA"));
        p.Add(Shiny switch { 1 => L.U("SHINY"), 3 => L.U("SHINY_STAR"), 4 => L.U("SHINY_SQUARE"), 2 => L.U("NOT_SHINY"), _ => "" });
        if (Ball > 0 && Ball < s.balllist.Length) p.Add(s.balllist[Ball]);
        if (Nature >= 0) p.Add(s.natures[Nature]);
        if (Ability >= 0) p.Add(L.AbilityNames[Math.Min(Ability, L.AbilityNames.Length - 1)]);
        if (SizeText.Length > 0) p.Add(SizeText);
        var rib = Ribbons.Count > 0 ? Ribbons : Mark >= 0 ? ["Ribbon" + (RibbonIndex)Mark] : [];
        if (rib.Count > 0) p.Add(string.Join("·", rib.Select(n => s.Ribbons.GetNameSafe(n, out var k) ? k : n.Replace("Ribbon", ""))));
        if (MetFrom is { } mf) p.Add(L.U("MET") + " " + (MetTo is { } mt && mt != mf ? $"{mf:yyyy-MM-dd}~{mt:yyyy-MM-dd}" : $"{mf:yyyy-MM-dd}"));
        if (FlawlessIVs > 0) p.Add($"{FlawlessIVs}V");
        var ex = string.Join(" ", ExactIV.Select((v, i) => v < 0 ? "" : $"{"HABCDS"[i]}{v}").Where(x => x.Length > 0)); if (ex.Length > 0) p.Add(L.U("IV") + " " + ex);
        var evs = string.Join(" ", EV.Select((v, i) => v < 0 ? "" : $"{"HABCDS"[i]}{v}").Where(x => x.Length > 0)); if (evs.Length > 0) p.Add(L.U("EV") + " " + evs);
        if (Level > 0) p.Add($"Lv{Level}");
        if (Gmax) p.Add(L.U("GMAX"));
        if (HyperTrain) p.Add(L.U("HYPERTRAIN"));
        if (Tera >= 0) p.Add(string.Format(L.U("TERA"), Tera == 99 ? L.U("STELLAR") : Tera < s.types.Length ? s.types[Tera] : "?"));
        if (Moves.Count > 0) p.Add(L.U("MOVES") + " " + string.Join("·", Moves.Select(m => m < s.movelist.Length ? s.movelist[m] : "?")));
        if (OT != null || TID >= 0) p.Add($"{L.U("OT")} {OT ?? "-"}{(TID >= 0 ? $" {TID}" : "")}{(SID >= 0 ? $"/{SID}" : "")}");
        if (Count > 1) p.Add($"×{Count}");
        return string.Join(L.U("SEP"), p.Where(x => x.Length > 0));
    }
}

/// <summary>
/// 대충 쓴 주문 문장 → 주문 내용. 예: "빠르모트 암컷 이로치 러브볼 가장작게 / 찌리배리 레벨볼 큰증".
/// 낱말은 언어별 사전(Lexicon)에서, 포켓몬·기술·볼·성격 이름은 게임 문자열(지금 언어 + 영어)에서 찾음.
/// 줄바꿈으로 여러 마리, "종(폼1,폼2)"로 여러 폼, "어버이: 이름 TID" 줄은 아래 전체에 적용.
/// </summary>
public static class QuickParse
{
    private static Lexicon L => Lexicon.Current;
    /// <summary>사용자 사전 (앱 설정에 저장, 기본 사전보다 우선).</summary>
    public static Dictionary<string, string> User = new();
    /// <summary>기본 줄임말 사전 (지금 언어). 예전 코드 호환용</summary>
    public static IReadOnlyDictionary<string, string> BuiltInAlias => L.Aliases;

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

    /// <summary>사용자 사전 + 기본 사전으로 단어를 바꿔 씀. 여러 마리 값("/")은 그 줄을 여러 줄로 복제. 바꾼 결과는 다시 바꾸지 않음.</summary>
    private static List<string> ApplyAlias(string part, IReadOnlyDictionary<string, string> user)
    {
        var results = new List<List<string>> { new() };
        var toks = Tokenize(part);
        for (int ti = 0; ti < toks.Count; ti++)
        {
            var tok = toks[ti];
            var key = Norm(tok); string val = null;
            if (user != null) foreach (var kv in user) if (Norm(kv.Key) == key) { val = kv.Value; break; }
            if (val == null) foreach (var kv in User) if (Norm(kv.Key) == key) { val = kv.Value; break; }
            // "Tapu Koko"처럼 옆 낱말과 합쳐 이미 포켓몬 이름이면 줄임말('koko')로 보지 않음
            bool partOfName = (ti > 0 && FindSpecies(Norm(toks[ti - 1] + tok), out _)) || (ti + 1 < toks.Count && FindSpecies(Norm(tok + toks[ti + 1]), out _));
            if (val == null && !partOfName) val = L.Alias(key);
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
        var result = new List<OrderSpec>();
        var global = new OrderSpec(); var allSpec = new OrderSpec();
        foreach (var rawLine in ExpandRanges((text ?? "").Replace("\r", "").Split('\n'), sav))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            // 공통 줄: "어버이: 새아 468686", "공통 어버이 새아 / TID 468686", "모두 암컷"
            // 띄어쓰기나 ':'가 있어야 공통 줄 — 없으면 다투곰·다크라이·다부니처럼 '다'로 시작하는 이름이 공통 줄로 잡혔음
            var allM = Regex.Match(line, @"^(\S+?)(\s*[:：]\s*|\s+)(.+)$");
            if (allM.Success && L.AllLine.Contains(Norm(allM.Groups[1].Value))) { ApplyTokens(allSpec, Tokenize(allM.Groups[3].Value), sav, false); continue; }
            var comM = Regex.Match(line, @"^(\S+?)(\s*[:：]\s*|\s+)(.*)$");
            if (comM.Success && L.CommonLine.Contains(Norm(comM.Groups[1].Value)) && (comM.Groups[2].Value.Contains(':') || comM.Groups[2].Value.Contains('：') || IsCommonWord(comM.Groups[1].Value)))
            {
                var body = IsCommonWord(comM.Groups[1].Value) ? comM.Groups[3].Value : line;   // "공통 어버이 …" → 공통만 떼고, "어버이: …" → 그대로
                ApplyTokens(global, Tokenize(body), sav, onlyTrainer: true); continue;
            }
            foreach (var part in ApplyAlias(line, userAlias))
                foreach (var expanded in ExpandForms(part))
                {
                    var o = new OrderSpec { Raw = expanded.Text, OT = global.OT, TID = global.TID, SID = global.SID, OTGender = global.OTGender };
                    ApplyTokens(o, Tokenize(expanded.Text), sav, false);
                    if (expanded.Form != null) ResolveFormWord(o, expanded.Form, sav);
                    if (o.Species == 0) o.Notes.Add(L.M("UNKNOWN_SPECIES"));
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
    /// <summary>'공통'처럼 줄 전체가 아래 모두에 적용된다는 말 (어버이·ot·tn은 ':'가 있어야 공통 줄)</summary>
    private static bool IsCommonWord(string w) { var n = Norm(w); return L.CommonLine.Contains(n) && !L.Is(n, "OT_WORD"); }

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
            var m = Regex.Match(line, @"^(?:(?:리빙\s*덱스|리빙\s*도감|전국\s*도감|도감|dex|livingdex|living\s*dex|pokedex|図鑑|ずかん|全国図鑑)(?:\s*(\d{1,4})\s*(?:~|∼|〜|～|-|–|부터|to|から)\s*(\d{1,4})\s*(?:번|까지|번까지|まで)?|\s*(전체|전부|모두|all|全部|すべて)|(?=\s|$))|(\d{1,4})\s*(?:~|∼|〜|～|-|–|부터|から)\s*(\d{1,4})\s*(?:번|까지|번까지|まで)?)\s*(.*)$", RegexOptions.IgnoreCase);
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

    // "싸리용(뻗은,젖힌) 우두로치" → ("싸리용 우두로치", 뻗은), ("싸리용 우두로치", 젖힌)
    private static IEnumerable<(string Text, string Form)> ExpandForms(string part)
    {
        var m = Regex.Match(part, @"[(（]([^)）]*)[)）]");
        if (!m.Success) { yield return (part, null); yield break; }
        if (m.Groups[1].Value.Contains('/'))   // "에써르 (수/ 작증 / 럽볼)" → 괄호 안은 속성
        {
            yield return ((part[..m.Index] + " " + m.Groups[1].Value.Replace('/', ' ') + " " + part[(m.Index + m.Length)..]).Trim(), null); yield break;
        }
        var inner = m.Groups[1].Value;
        if (L.Keys.TryGetValue("EVO_WORD", out var evo) && evo.Any(e => e.Length > 1 && Norm(inner).Contains(e)))   // "나오하(마스카나진화)" → 나오하 다음에 진화 대상
        {
            var target = inner;
            foreach (var e in evo.Where(e => e.Length > 1).OrderByDescending(e => e.Length)) target = Regex.Replace(target, Regex.Escape(e) + @"(형|체|까지|後)?", "", RegexOptions.IgnoreCase);
            yield return ((part[..m.Index] + " " + target.Trim() + " " + part[(m.Index + m.Length)..]).Trim(), null); yield break;
        }
        var rest = (part[..m.Index] + " " + part[(m.Index + m.Length)..]).Trim();
        var forms = inner.Split(',', '，', '、').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (forms.Count == 0) { yield return (rest, null); yield break; }
        foreach (var f in forms) yield return (rest, f);
    }

    private static List<string> Tokenize(string t)
    {
        t = Dates(t);
        t = Regex.Replace(t, @"타입\s*[:：]?\s*널", "타입:널");   // 이름에 ':'가 든 포켓몬
        t = Regex.Replace(t, @"type\s*[:：]?\s*null", "type:null", RegexOptions.IgnoreCase);
        t = t.Replace("타입:널", "\u0001").Replace("type:null", "\u0002").Replace("Type:null", "\u0002").Replace("TYPE:NULL", "\u0002").Replace("Type:Null", "\u0002");
        t = Regex.Replace(t, @"([가-힣])의\s+(증표|리본|모습|태세|꽃)", "$1의$2");   // "카레의 증표" = "카레의증표", "가라르의 모습"
        t = Regex.Replace(t, @"([가-힣])\s+(증표|리본)(?=\s|$)", "$1$2");            // "절친 리본" = "절친리본", "커다란 증표"
        t = Regex.Replace(t, @"(?<=[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}])\s*の\s*(あかし|アカシ|証|すがた|姿)", "の$1");   // "雨 の あかし"
        t = Regex.Replace(t, @"[,，、。:：/·|]", " ");   // '/'·'·'는 구분용으로만 쓰고 무시 ('・'는 カプ・コケコ처럼 이름의 일부)
        t = t.Replace("\u0001", "타입:널").Replace("\u0002", "Type:Null");
        return t.Split([' ', '　'], StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static string Norm(string x) => Lexicon.Norm(x);

    /// <summary>이름 비교용: 정규화에 더해 ':'·'.'·'’'·'-'도 뺌 (Type: Null, Mr. Mime, Ho-Oh, Farfetch’d)</summary>
    private static string NameKey(string norm) => norm.Replace(":", "").Replace(".", "").Replace("’", "'").Replace("-", "").Replace("—", "");

    private static void ApplyTokens(OrderSpec o, List<string> tokens, SaveFile sav, bool onlyTrainer)
    {
        bool range = false;
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
            if (L.Is(t, "RANGE_WORD") || t == "~") { range = true; continue; }
            if (L.Is(t, "DATE_WORD")) continue;
            // 어버이·트레이너
            if (L.Is(t, "OT_WORD"))
            {
                if (i + 1 < tokens.Count && !IsNumber(tokens[i + 1]) && Norm(tokens[i + 1]) is not ("tid" or "sid")) { o.OT = tokens[++i]; }
                if (i + 1 < tokens.Count && IsNumber(tokens[i + 1])) { o.TID = int.Parse(tokens[++i]); if (i + 1 < tokens.Count && IsNumber(tokens[i + 1]) && tokens[i + 1].Length <= 5) o.SID = int.Parse(tokens[++i]); }
                continue;
            }
            // "어버이우마무스메"처럼 붙여 쓴 것
            if (!onlyTrainer && o.OT == null && L.Keys.TryGetValue("OT_WORD", out var otw) && otw.FirstOrDefault(w => w.Length >= 3 && t.StartsWith(w) && t.Length > w.Length + 1) is { } otp && !TryToken(new OrderSpec(), raw, t, sav))
            { o.OT = raw[otp.Length..]; continue; }
            if (t is "tid" or "id" && i + 1 < tokens.Count && IsNumber(tokens[i + 1])) { o.TID = int.Parse(tokens[++i]); continue; }
            if (t == "sid" && i + 1 < tokens.Count && IsNumber(tokens[i + 1])) { o.SID = int.Parse(tokens[++i]); continue; }
            var mtid = Regex.Match(t, @"^(tid|sid)[:：]?(\d{1,6})$"); if (mtid.Success) { if (mtid.Groups[1].Value == "tid") o.TID = int.Parse(mtid.Groups[2].Value); else o.SID = int.Parse(mtid.Groups[2].Value); continue; }
            if (onlyTrainer)
            {
                if (IsNumber(raw)) { if (o.TID < 0) o.TID = int.Parse(raw); else o.SID = int.Parse(raw); continue; }
                if (L.Is(t, "OT_FEMALE")) { o.OTGender = 1; continue; }
                if (L.Is(t, "OT_MALE")) { o.OTGender = 0; continue; }
                if (o.OT == null) o.OT = raw;
                continue;
            }
            if (L.Is(t, "EVENT")) { o.Event = true; continue; }
            if (L.Is(t, "EVO_WORD")) continue;
            if (L.Is(t, "MOVE_WORD")) continue;
            // "테라 불꽃" / "불꽃 테라스탈" (타입이 앞뒤 어느 쪽에 와도)
            if (L.Is(t, "TERA_WORD"))
            {
                if (i + 1 < tokens.Count && TeraWord(Norm(tokens[i + 1])) is int tw) { o.Tera = tw; i++; continue; }
                if (i > 0 && TeraWord(Norm(tokens[i - 1])) is int tp) { o.Tera = tp; o.Unknown.Remove(tokens[i - 1]); continue; }
                continue;
            }
            // "레벨 65", "Lv. 100", "x 3"
            if (L.Is(t, "LEVEL_WORD") && i + 1 < tokens.Count && Regex.IsMatch(tokens[i + 1], @"^\d{1,3}$")) { o.Level = Math.Clamp(int.Parse(tokens[++i]), 1, 100); continue; }
            // 띄어 쓴 이름 잇기: "Tapu Koko", "Dragon Claw", "Master Rank Ribbon", "최대 크기" (두세 낱말을 붙여 하나로 알아볼 수 있으면 그쪽이 먼저 — "Master"가 볼로 읽히지 않게)
            if (TryJoin(o, tokens, ref i, sav)) continue;
            if (TryToken(o, raw, t, sav)) continue;
            // 붙여 쓴 말 나누기 시도: "러브볼암컷" 같은 경우 앞에서부터 가장 긴 단어
            if (SplitCompound(o, raw, sav)) continue;
            if (L.Is(t, "IGNORE")) continue;
            o.Unknown.Add(raw);
        }
        // 배포: 알아보지 못한 단어는 배포를 고르는 단서로 ("리코의" → 리코)
        if (o.Event) { foreach (var u in o.Unknown) o.Keywords.Add(u.EndsWith("의") && u.Length > 1 ? u[..^1] : u); o.Unknown.Clear(); }
        // 종보다 앞에 적힌 폼 단어 ("붉은달 다투곰")는 종을 알게 된 뒤 다시 맞춰 봄
        if (o.Species != 0) foreach (var u in o.Unknown.ToList()) if (ResolveFormWord(o, u, sav, quiet: true)) o.Unknown.Remove(u);
    }

    private static bool IsNumber(string x) => Regex.IsMatch(x, @"^\d{1,6}$");

    /// <summary>다음 한두 낱말을 붙여 보고(띄어 쓴 이름) 알아볼 수 있으면 그걸로. 낱말 하나하나로도 알아볼 수 있는 것은 잇지 않음 (뜻이 바뀌지 않게)</summary>
    private static bool TryJoin(OrderSpec o, List<string> tokens, ref int i, SaveFile sav)
    {
        for (int n = 2; n <= 3 && i + n - 1 < tokens.Count; n++)
        {
            var joined = string.Concat(tokens.Skip(i).Take(n));
            var tn = Norm(joined);
            if (tn.Length < 3) continue;
            var tmp = new OrderSpec { Species = o.Species, Form = o.Form };
            bool ok; strictForm = true;
            try { ok = TryToken(tmp, joined, tn, sav); } finally { strictForm = false; }
            if (!ok) continue;
            TryToken(o, joined, tn, sav); i += n - 1; return true;
        }
        return false;
    }

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

    private static void SetSize(OrderSpec o, string key) { var (mn, mx) = key == "MIN" ? (0, 0) : (255, 255); o.ScaleMin = mn; o.ScaleMax = mx; o.SizeText = L.SizeName(key); }

    private static bool TryToken(OrderSpec o, string raw, string t, SaveFile sav)
    {
        var s = GenHost.Strings;
        if (t.Length == 0) return false;
        // 성별
        if (L.Is(t, "FEMALE")) { o.Gender = 1; return true; }
        if (L.Is(t, "WIFE"))   // 재미: 그 게임에 나오는 무작위 암컷 포켓몬
        {
            var cands = new List<ushort>();
            for (ushort wsp = 1; wsp <= sav.MaxSpeciesID; wsp++)
            {
                if (!sav.Personal.IsSpeciesInGame(wsp)) continue;
                var wpi = sav.Personal.GetFormEntry(wsp, 0);
                if (wpi.Genderless || wpi.OnlyMale) continue;
                cands.Add(wsp);
            }
            if (cands.Count > 0 && o.Species == 0) { o.Species = cands[Random.Shared.Next(cands.Count)]; o.Notes.Add(L.M("WIFE", s.Species[o.Species])); }
            o.Gender = 1; return true;
        }
        if (L.Is(t, "MALE")) { o.Gender = 0; return true; }
        // 이로치·우두머리
        if (L.Is(t, "ALPHA_SHINY")) { o.Alpha = 1; o.Shiny = Math.Max(o.Shiny, 1); return true; }
        if (L.Is(t, "ALPHA")) { o.Alpha = 1; return true; }
        if (L.Is(t, "SHINY")) { if (o.Shiny is 0 or 2) o.Shiny = 1; return true; }   // "일반 이로치" = 우두머리 아닌 이로치
        if (L.Is(t, "SHINY_STAR")) { o.Shiny = 3; return true; }
        if (L.Is(t, "SHINY_SQUARE")) { o.Shiny = 4; return true; }
        if (L.Is(t, "NOT_SHINY")) { if (o.Alpha < 0) o.Alpha = 0; o.Shiny = 2; return true; }
        if (L.Is(t, "NORMAL")) { if (o.Alpha < 0) o.Alpha = 0; if (o.Shiny == 0) o.Shiny = 2; return true; }   // 우두머리 아님 (이로치와 함께 쓰면 이로치)
        // 크기·증표
        if (L.Is(t, "SUPERL")) { o.Superl = true; return true; }
        if (L.Is(t, "SIZE_WORD")) return true;
        if (o.Superl && L.Is(t, "SUPERL_BIG")) { o.Superl = false; SetSize(o, "MAX"); return true; }
        if (o.Superl && L.Is(t, "SUPERL_SMALL")) { o.Superl = false; SetSize(o, "MIN"); return true; }
        if (L.Size(t) is { } sz) { o.ScaleMin = sz.Min; o.ScaleMax = sz.Max; o.SizeText = sz.Name; return true; }
        if (L.Is(t, "MINI_MARK")) { AddMark(o, RibbonIndex.MarkMini); SetSize(o, "MIN"); return true; }
        if (L.Is(t, "JUMBO_MARK")) { AddMark(o, RibbonIndex.MarkJumbo); SetSize(o, "MAX"); return true; }
        if (L.Is(t, "MARK_WORD"))   // 앞의 크기와 합쳐 작은·큰 증표
        {
            if (o.ScaleMax == 0) { AddMark(o, RibbonIndex.MarkMini); return true; }
            if (o.ScaleMin == 255) { AddMark(o, RibbonIndex.MarkJumbo); return true; }
            o.Notes.Add(L.M("MARK_UNKNOWN"));
            return true;
        }
        if (L.MarkOnly(t) is { } mo && Enum.TryParse<RibbonIndex>(mo, out var moi)) { AddMark(o, moi); return true; }
        foreach (var r in Enum.GetValues<RibbonIndex>().Where(x => x.ToString().StartsWith("Mark")))
            if (NameMatches("Ribbon" + r, t, s, (n, w) => n == w || StripOf(n) == w || n == w + "mark")) { AddMark(o, r); return true; }
        var ribbonSuf = L.Suf("RIBBON").Concat(["리본", "ribbon", "リボン"]).ToHashSet();
        foreach (var suf in L.MarkSuffix.Concat(Lexicon.En.MarkSuffix).Distinct())
        {
            if (!t.EndsWith(suf) || t.Length <= suf.Length) continue;
            var stem = t[..^suf.Length];
            // "파트너리본"처럼 진짜 리본 이름이면 리본이 먼저 (증표와 이름이 겹치는 것)
            if (ribbonSuf.Contains(suf) && FindRibbon(t, sav) is { } realRib && !realRib.StartsWith("RibbonMark")) { if (!o.Ribbons.Contains(realRib)) o.Ribbons.Add(realRib); return true; }
            if (L.Mark(stem) is { } mk && Enum.TryParse<RibbonIndex>(mk, out var mki)) { AddMark(o, mki); return true; }
            // "팔데아챔피언증표"처럼 리본을 증표라고 적은 것
            if (L.Ribbon(stem) is { } ra0) { var prop = ra0 == "CHAMPION" ? ChampionRibbon(sav) : ra0; if (prop != null && RibbonProps(sav.BlankPKM.GetType()).Contains(prop)) { if (!o.Ribbons.Contains(prop)) o.Ribbons.Add(prop); return true; } }
            if (FindRibbon(stem, sav) is { } rib0 && !rib0.StartsWith("RibbonMark")) { if (!o.Ribbons.Contains(rib0)) o.Ribbons.Add(rib0); return true; }
        }
        // 개체값·레벨·개수
        var mv = Regex.Match(t, @"^(\d{1,3})(?:v|iv|ivs)$"); if (mv.Success) { o.FlawlessIVs = Math.Min(6, int.Parse(mv.Groups[1].Value)); return true; }   // 6V, 5IV, 100V(=6V)
        if (t is "6vs" or "perfect" or "perfectivs" or "flawless") { o.FlawlessIVs = 6; return true; }
        // 개체값·노력치 구문: 'a0'·'s31' = 개체값, 'as252'·'h4'·'b252' = 노력치 (글자 여러 개, 32 이상, 또는 4)
        var ms = Regex.Match(t, @"^([habcds]+)(\d{1,3})$");
        if (!ms.Success)
        {   // "0atk", "atk0", "31spe", "hp31" (영어 능력치 낱말)
            var me = Regex.Match(t, @"^(?:(hp|atk|att|attack|def|defense|spa|spatk|spattack|spd|spdef|spdefense|spe|speed)(\d{1,3})|(\d{1,3})(hp|atk|att|attack|def|defense|spa|spatk|spattack|spd|spdef|spdefense|spe|speed))$");
            if (me.Success)
            {
                var word = me.Groups[1].Success ? me.Groups[1].Value : me.Groups[4].Value; var num = me.Groups[2].Success ? me.Groups[2].Value : me.Groups[3].Value;
                var letter = word switch { "hp" => "h", "atk" or "att" or "attack" => "a", "def" or "defense" => "b", "spa" or "spatk" or "spattack" => "c", "spd" or "spdef" or "spdefense" => "d", _ => "s" };
                ms = Regex.Match(letter + num, @"^([habcds]+)(\d{1,3})$");
            }
        }
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
        var lvWords = L.Suf("LEVEL").Concat(Lexicon.En.Suf("LEVEL")).Distinct().Select(Regex.Escape).ToList();
        var ml = Regex.Match(t, $@"^(?:{string.Join("|", lvWords)})\.?(\d{{1,3}})$|^(\d{{1,3}})(?:{string.Join("|", lvWords)})$");
        if (ml.Success) { o.Level = Math.Clamp(int.Parse(ml.Groups[1].Success ? ml.Groups[1].Value : ml.Groups[2].Value), 1, 100); return true; }
        var cntWords = L.Suf("COUNT").Concat(Lexicon.En.Suf("COUNT")).Distinct().Select(Regex.Escape).ToList();
        var mc = Regex.Match(t, $@"^(?:x|×)(\d{{1,2}})$|^(\d{{1,2}})(?:{string.Join("|", cntWords)})$"); if (mc.Success) { o.Count = Math.Clamp(int.Parse(mc.Groups[1].Success ? mc.Groups[1].Value : mc.Groups[2].Value), 1, 30); return true; }
        if (L.Is(t, "HIDDEN_ABILITY")) { o.Ability = 2; return true; }
        // 거다이맥스·특훈·테라스탈 타입 ("노말테라", "불테라", "테라페어리", "스텔라테라")
        if (L.Is(t, "HYPERTRAIN")) { o.HyperTrain = true; return true; }
        if (L.Is(t, "GMAX")) { o.Gmax = true; return true; }
        if (L.Keys.TryGetValue("TERA_WORD", out var teraWords))
            foreach (var suf in teraWords.Concat(Lexicon.En.Keys.TryGetValue("TERA_WORD", out var tw2) ? tw2 : []).OrderByDescending(x => x.Length))
            {
                if (t.EndsWith(suf) && t.Length > suf.Length && TeraWord(t[..^suf.Length]) is int ta) { o.Tera = ta; return true; }
                if (t.StartsWith(suf) && t.Length > suf.Length && TeraWord(t[suf.Length..]) is int tb) { o.Tera = tb; return true; }
            }
        // 볼
        if (BallOf(t) is int ball && ball > 0) { o.Ball = ball; return true; }
        // 성격 ("고집", "고집성격")
        if (NatureOf(t) is int nat && nat >= 0) { o.Nature = nat; return true; }
        // 포켓몬 (리전폼 접두어: 가라르·알로라·히스이·팔데아)
        foreach (var (prefix, formWord) in L.Regions.Concat(Lexicon.En.Regions).OrderByDescending(x => x.Key.Length))
        {
            if (t.StartsWith(prefix) && t.Length > prefix.Length && FindSpecies(t[prefix.Length..], out var sr)) { o.Species = sr; ResolveFormWord(o, formWord, sav); return true; }
            if (t == prefix) { o.FormText = formWord; if (o.Species != 0) ResolveFormWord(o, formWord, sav); else o.Notes.Add(L.M("REGION_FORM", raw)); return true; }
        }
        if (FindSpecies(t, out var sp)) { if (o.Species != 0 && o.Species != sp) o.PreSpecies = o.Species; o.Species = sp; if (o.FormText.Length > 0 && o.Form < 0) ResolveFormWord(o, o.FormText, sav); return true; }
        // 특성 이름
        if (o.Species != 0)
        {
            for (int a = 1; a < s.abilitylist.Length; a++)
                if (NameMatches(a, t, s.abilitylist, Lexicon.En.Lang == L.Lang ? null : GameInfo.GetStrings("en").abilitylist))
                {
                    try { var pi = sav.Personal.GetFormEntry(o.Species, (byte)Math.Max(0, o.Form)); for (int k = 0; k < pi.AbilityCount; k++) if (pi.GetAbilityAtIndex(k) == a) { o.Ability = k; return true; } } catch { }
                    o.Notes.Add(L.M("ABILITY_NOT", s.abilitylist[a])); return true;
                }
        }
        // 기술 이름 ("10만볼트", "thunderbolt")
        if (FindMove(t) is ushort mvid)
        {
            if (o.Moves.Count < 4 && !o.Moves.Contains(mvid)) o.Moves.Add(mvid);
            else if (!o.Moves.Contains(mvid)) o.Notes.Add(L.M("MOVES_MAX", s.movelist[mvid]));
            return true;
        }
        // 폼 단어 ("뻗은", "붉은달")
        if (o.Species != 0 && ResolveFormWord(o, raw, sav, quiet: true)) return true;
        // 리본 이름 ("챔피언리본", "절친리본") — 여러 개 적으면 모두
        if (L.Ribbon(t) is { } ra)
        {
            var prop = ra == "CHAMPION" ? ChampionRibbon(sav) : ra;
            if (prop != null && RibbonProps(sav.BlankPKM.GetType()).Contains(prop)) { if (!o.Ribbons.Contains(prop)) o.Ribbons.Add(prop); return true; }
            if (ra == "CHAMPION") return false;
        }
        if (FindRibbon(t, sav) is { } rib)
        {
            if (rib.StartsWith("RibbonMark") && Enum.TryParse<RibbonIndex>(rib[6..], out var ri)) AddMark(o, ri);
            else if (!o.Ribbons.Contains(rib)) o.Ribbons.Add(rib);
            return true;
        }
        return false;
    }

    /// <summary>증표·리본 이름 비교: 지금 언어 이름과 영어 이름 모두, 접미사('의'·'mark'·'のあかし')를 뺀 꼴로도</summary>
    private static bool NameMatches(string prop, string t, GameStrings s, Func<string, string, bool> eq)
    {
        if (s.Ribbons.GetNameSafe(prop, out var kn) && eq(Norm(Regex.Replace(kn, @"\s*\([^)]*\)", "")), t)) return true;
        if (L.Lang != "en" && GameInfo.GetStrings("en").Ribbons.GetNameSafe(prop, out var en) && eq(Norm(Regex.Replace(en, @"\s*\([^)]*\)", "")), t)) return true;
        return false;
    }
    private static bool NameMatches(int idx, string t, string[] cur, string[] en)
    {
        if (idx < cur.Length && Norm(cur[idx]) == t) return true;
        return en != null && idx < en.Length && Norm(en[idx]) == t;
    }
    /// <summary>이름에서 꾸밈말을 뺀 꼴 ("카레의증표" → "카레증표", "rainymark" → "rainy")</summary>
    private static string StripOf(string n)
    {
        foreach (var w in L.FormStrip) if (w.Length > 0) n = n.Replace(w, "");
        return n;
    }

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
        // 커다란·조그만 증표는 크기가 정해져 있음 (배율 255 / 0) — 어떤 말로 적었든 크기도 같이 맞춤 ("커다란증표"가 크기 없이 들어가 생성 실패하던 문제)
        if (m == RibbonIndex.MarkJumbo && o.ScaleMin != 255) SetSize(o, "MAX");
        if (m == RibbonIndex.MarkMini && o.ScaleMax != 0) SetSize(o, "MIN");
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
        var ribSuf = L.Suf("RIBBON").Concat(["리본", "ribbon", "リボン"]).Distinct().ToArray();
        foreach (var prop in RibbonProps(sav.BlankPKM.GetType()))
        {
            if (Norm(prop[6..]) == t) return prop;
            if (NameMatches(prop, t, s, (n, w) => n == w || ribSuf.Any(rs => n == w + rs) || StripOf(n) == StripOf(w))) return prop;   // "쿨리본 (3세대)" → "쿨리본"
        }
        return null;
    }

    /// <summary>날짜 표기를 한 단어로: 2024-01-05·2024.1.5·2024년 1월 5일 → @D20240105, 2024-03 → @M202403, 2024년 → @Y2024. '~'·'부터'는 구간.</summary>
    private static string Dates(string t)
    {
        static bool Ok(int y, int m, int d) => y is >= 1990 and <= 2099 && m is >= 1 and <= 12 && d >= 1 && d <= DateTime.DaysInMonth(y, m);
        t = Regex.Replace(t, @"[~∼〜～]", " ~ ");
        t = Regex.Replace(t, @"(?<!\d)(\d{4})\s*[-./년年]\s*(\d{1,2})\s*[-./월月]\s*(\d{1,2})\s*[일日]?(?!\d)", m =>
        {
            int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), d = int.Parse(m.Groups[3].Value);
            return Ok(y, mo, d) ? $" @D{y:0000}{mo:00}{d:00} " : m.Value;
        });
        t = Regex.Replace(t, @"(?<![\d@A-Za-z])(\d{4})(\d{2})(\d{2})(?!\d)", m =>
        {
            int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), d = int.Parse(m.Groups[3].Value);
            return Ok(y, mo, d) ? $" @D{y:0000}{mo:00}{d:00} " : m.Value;
        });
        t = Regex.Replace(t, @"(?<![\d@A-Za-z])(\d{4})\s*[-./년年]\s*(\d{1,2})\s*[월月]?(?![\d.])", m =>
        {
            int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value);
            return Ok(y, mo, 1) ? $" @M{y:0000}{mo:00} " : m.Value;
        });
        t = Regex.Replace(t, @"(?<![\d@A-Za-z])(\d{4})\s*[년年]", m => int.Parse(m.Groups[1].Value) is >= 1990 and <= 2099 ? $" @Y{m.Groups[1].Value} " : m.Value);
        t = Regex.Replace(t, @"(@[DMY]\d+)\s*(부터|에서|から|from)", "$1 ~ ", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"(@[DMY]\d+)\s*(까지|사이|まで|until|to)", "$1 ", RegexOptions.IgnoreCase);
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

    // ===== 일괄 수정에서 쓰는 단어 찾기 =====
    public static ushort SpeciesOf(string w, IReadOnlyDictionary<string, string> user = null)
    {
        var t = Norm(w);
        if (FindSpecies(t, out var sp)) return sp;
        string v = null;
        if (user != null) foreach (var kv in user) if (Norm(kv.Key) == t) { v = kv.Value; break; }
        v ??= L.Alias(t);
        return v != null && !v.Contains('/') && FindSpecies(Norm(v.Split(' ')[0]), out sp) ? sp : (ushort)0;
    }
    /// <summary>볼 이름·줄임말 → 볼 번호 (없으면 -1). 지금 언어 이름, 영어 이름, '볼'을 뺀 이름 모두</summary>
    public static int BallOf(string w)
    {
        var t = Norm(w); if (t.Length == 0) return -1;
        var s = GenHost.Strings; var en = GameInfo.GetStrings("en");
        var ballSuf = L.Suf("BALL").Concat(Lexicon.En.Suf("BALL")).Distinct().ToArray();
        var alias = L.Ball(t);   // 영어 볼 이름
        for (int b = 1; b < s.balllist.Length; b++)
        {
            if (string.IsNullOrEmpty(s.balllist[b])) continue;
            if (alias != null && b < en.balllist.Length && Norm(en.balllist[b]) == Norm(alias)) return b;
            if (alias != null) continue;
            var cur = Norm(s.balllist[b]); var e = b < en.balllist.Length ? Norm(en.balllist[b]) : "";
            if (cur == t || e == t || ballSuf.Any(suf => cur == t + suf || e == t + suf)) return b;
        }
        return -1;
    }
    public static int NatureOf(string w)
    {
        var t = Norm(w);
        foreach (var suf in L.Suf("NATURE").Concat(Lexicon.En.Suf("NATURE"))) if (t.EndsWith(suf) && t.Length > suf.Length) { t = t[..^suf.Length]; break; }
        var s = GenHost.Strings; var en = L.Lang == "en" ? null : GameInfo.GetStrings("en").natures;
        for (int n = 0; n < 25 && n < s.natures.Length; n++) if (Norm(s.natures[n]) == t || en != null && n < en.Length && Norm(en[n]) == t) return n;
        return -1;
    }
    public static int? TeraOf(string w) => TeraWord(Norm(w));

    /// <summary>타입 이름 → 테라스탈 타입 번호 (스텔라 99). 아니면 null.</summary>
    private static int? TeraWord(string w)
    {
        foreach (var suf in L.Suf("TYPE").Concat(Lexicon.En.Suf("TYPE"))) if (w.EndsWith(suf) && w.Length > suf.Length) { w = w[..^suf.Length]; break; }
        if (w.Length == 0) return null;
        if (L.IsStellar(w)) return 99;
        var en = GameInfo.GetStrings("en").types;
        if (L.Type(w) is { } ta) w = Norm(ta);   // 줄임말 → 영어 타입 이름
        var cur = GenHost.Strings.types;
        for (int k = 0; k < 18 && k < cur.Length; k++) if (Norm(cur[k]) == w || k < en.Length && Norm(en[k]) == w) return k;
        return null;
    }

    private static ushort? FindMove(string t)
    {
        if (t.Length < 2) return null;
        var cur = GenHost.Strings.movelist; var en = GameInfo.GetStrings("en").movelist;
        var key = NameKey(t);
        for (int m = 1; m < cur.Length; m++) if (NameKey(Norm(cur[m])) == key || (m < en.Length && NameKey(Norm(en[m])) == key)) return (ushort)m;
        return null;
    }

    private static bool FindSpecies(string t, out ushort sp)
    {
        var cur = GenHost.Strings.Species; var en = GameInfo.GetStrings("en").Species;
        var key = NameKey(t);
        if (key.Length > 0)
            for (ushort i = 1; i < cur.Count; i++) if (NameKey(Norm(cur[i])) == key || (i < en.Count && NameKey(Norm(en[i])) == key)) { sp = i; return true; }
        sp = 0; return false;
    }

    /// <summary>폼 단어를 그 포켓몬의 폼 이름과 맞춰 봄 (부분 일치). 지금 언어 폼 이름, 그리고 영어 폼 이름으로도</summary>
    [ThreadStatic] private static bool strictForm;   // 낱말을 이어 볼 때는 폼 이름이 정확히 맞아야 함 ("galar shiny"가 폼으로 읽히지 않게)
    private static bool ResolveFormWord(OrderSpec o, string word, SaveFile sav, bool quiet = false)
    {
        if (o.Species == 0) { o.FormText = word; return false; }
        var s = GenHost.Strings;
        var w = StripOf(Norm(word));
        if (w.Length == 0) return false;
        foreach (var strings in L.Lang == "en" ? new[] { s } : new[] { s, GameInfo.GetStrings("en") })
        {
            string[] names;
            try { names = FormConverter.GetFormList(o.Species, strings.types, strings.forms, GameInfo.GenderSymbolUnicode, sav.Context); } catch { continue; }
            var hits = new List<int>();
            for (int f = 0; f < names.Length; f++) { var n0 = StripOf(Norm(names[f])); if (n0.Length > 0 && n0 == w) hits.Add(f); }
            if (hits.Count == 0 && !strictForm) for (int f = 0; f < names.Length; f++) { var n0 = StripOf(Norm(names[f])); if (n0.Length > 0 && (n0.Contains(w) || w.Contains(n0))) hits.Add(f); }
            if (hits.Count > 1 && hits.Contains(o.Form)) return true;   // "rapid strike": 'strike'는 두 폼에 다 있으니 이미 고른 폼 유지
            foreach (var f in hits)
            {
                {
                    o.Form = f;
                    string[] curNames = null;
                    try { curNames = FormConverter.GetFormList(o.Species, s.types, s.forms, GameInfo.GenderSymbolUnicode, sav.Context); } catch { }
                    o.FormText = (curNames != null && f < curNames.Length ? curNames[f] : names[f]).Trim();
                    return true;
                }
            }
        }
        if (!quiet) o.Notes.Add(L.M("FORM_NOT_FOUND", word));
        return false;
    }
}
