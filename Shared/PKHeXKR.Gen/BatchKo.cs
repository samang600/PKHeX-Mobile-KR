using System.Text.RegularExpressions;
using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// 일괄 수정: 대충 쓴 한국어 문장 → PKHeX 일괄 편집 명령어.
/// 예) "피카츄 전부 암컷으로 바꾸기", "가디안 계열 암컷 문볼로", "닉네임 붙은 포켓몬 닉네임 없애기", "이로치 포켓몬 어버이 미월로".
/// 한 줄 = 한 명령. 앞부분(대상: 종·계열·이로치·성별·닉네임·볼·레벨…) + 뒷부분(바꿀 것: '…로', '없애기' 등).
/// PKHeX 문법에 없는 것은 앱이 직접 처리하는 '@' 명령: @성별=1 (성별에 맞춰 성격값도), @어버이=이름/TID/SID (이로치 모양 유지).
/// </summary>
public static class BatchKo
{
    public sealed class Result
    {
        public List<string> Lines = [];      // 명령어 (세트 사이는 ';')
        public List<string> Unknown = [];    // 알아보지 못한 단어
        public List<string> Notes = [];
    }

    private static readonly HashSet<string> Verbs = ["바꾸기", "바꿔", "바꿔줘", "바꿔주세요", "바꾸고", "바꾼다", "바꿈", "변경", "변경하기", "변경해줘", "만들기", "만들어", "만들어줘", "설정", "설정하기", "하기", "해줘", "해", "적용", "교체", "고치기", "통일"];
    private static readonly HashSet<string> Clear = ["없애기", "없애", "없애줘", "지우기", "지워", "지워줘", "해제", "제거", "빼기", "빼", "초기화", "삭제", "없게", "떼기"];
    private static readonly HashSet<string> Terminators = ["아닌", "제외", "빼고", "포켓몬", "개체", "애들", "것", "놈", "전부", "모두", "다", "전체", "싹다", "몽땅"];
    private static readonly HashSet<string> Noise = ["포켓몬", "개체", "애들", "것", "놈", "전부", "모두", "다", "모든", "전체", "싹다", "몽땅", "중", "중에", "중에서", "가운데", "만", "들", "있는", "붙은", "달린", "을", "를", "은", "는", "이", "가", "의", "그리고", "및", "하고", "랑", "박스", "박스의", "에서"];
    private static readonly HashSet<string> Keys = ["어버이", "어버이를", "트레이너", "ot", "닉네임", "별명", "이름", "레벨", "렙", "볼", "몬스터볼", "성격", "성별", "언어", "특성", "테라", "테라타입", "친밀도", "기술", "기술배치", "리본", "개체값", "노력치", "크기", "사이즈"];

    /// <summary>'피카츄를'·'문볼로'·'포켓몬의' → 조사·'로' 떼기. 반환: (본말, '로'로 끝났는지).</summary>
    private static (string Word, bool To) Strip(string w)
    {
        var t = QuickParse.Norm(w);
        bool to = false;
        foreach (var suf in new[] { "으로", "로" })
            if (t.Length > suf.Length && t.EndsWith(suf) && !Known(t)) { t = t[..^suf.Length]; to = true; break; }
        if (!to)
            foreach (var suf in new[] { "들을", "들은", "들의", "들", "을", "를", "은", "는", "의", "만", "에", "이", "가" })
                if (t.Length > suf.Length && t.EndsWith(suf) && !Known(t) && Known(t[..^suf.Length])) { t = t[..^suf.Length]; break; }
        return (t, to);
    }
    private static bool Known(string t) => QuickParse.SpeciesOf(t) != 0 || QuickParse.BallOf(t) > 0 || Keys.Contains(t) || Terminators.Contains(t) || t.EndsWith("계열");

    public static Result Convert(string text, SaveFile sav, IReadOnlyDictionary<string, string> user = null)
    {
        var r = new Result();
        foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim().TrimEnd('.', '!', '?');
            if (line.Length == 0) continue;
            foreach (var set in Line(line, sav, user, r))
            {
                if (r.Lines.Count > 0) r.Lines.Add(";");
                r.Lines.AddRange(set);
            }
        }
        return r;
    }

    private static List<List<string>> Line(string line, SaveFile sav, IReadOnlyDictionary<string, string> user, Result r)
    {
        var toks = Regex.Replace(line, @"[,，/]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        // '가디안계열' → '가디안', '계열'
        for (int i = 0; i < toks.Count; i++)
        {
            var n = QuickParse.Norm(toks[i]);
            foreach (var fam in new[] { "진화계열", "계열", "일족" })
                if (n.Length > fam.Length && n.EndsWith(fam) && QuickParse.SpeciesOf(n[..^fam.Length], user) != 0) { toks[i] = n[..^fam.Length]; toks.Insert(i + 1, "계열"); break; }
        }
        var w = toks.Select(Strip).ToList();
        bool Change(int i) => w[i].To || Verbs.Contains(w[i].Word) || Clear.Contains(w[i].Word);
        // 대상과 바꿀 것의 경계: 뒤에 바꾸는 말이 있는 마지막 '대상 끝 단어'(종·계열·포켓몬·전부…) 다음
        int split = -1;
        for (int i = w.Count - 1; i >= 0; i--)
        {
            var t = w[i].Word;
            bool term = !w[i].To && (Terminators.Contains(t) || t == "계열" || QuickParse.SpeciesOf(t, user) != 0);
            if (term && Enumerable.Range(i + 1, w.Count - i - 1).Any(Change)) { split = i + 1; break; }
        }
        if (split < 0)
        {
            split = 0;
            for (int i = 0; i < w.Count; i++)
            {
                if (Change(i)) { split = i; break; }
                if (!Keys.Contains(w[i].Word)) continue;
                string n1 = i + 1 < w.Count ? w[i + 1].Word : "", n2 = i + 2 < w.Count ? w[i + 2].Word : "";
                if (n1 is "붙은" or "있는" or "없는" or "달린" or "안붙은" || (Regex.IsMatch(n1, @"^\d+$") && n2 is "이상" or "이하" or "미만" or "초과")) continue;   // 조건 ("닉네임 붙은", "레벨 30 미만")
                split = i; break;
            }
        }

        // ── 대상 (조건) ──
        var filters = new List<string>(); var species = new List<ushort>(); bool family = false;
        for (int i = 0; i < split; i++)
        {
            var t = w[i].Word; string next = i + 1 < split ? w[i + 1].Word : "";
            if (QuickParse.SpeciesOf(t, user) is var sp and > 0) { species.Add(sp); continue; }
            if (t is "계열" or "진화계열" or "일족" or "진화형") { family = true; continue; }
            if (t is "이로치" or "색違い" or "shiny") { filters.Add("=IsShiny=true"); continue; }
            if (t is "일반" or "일반색" or "노말색" or "비이로치") { filters.Add("=IsShiny=false"); continue; }
            if (t is "암" or "암컷" or "♀") { filters.Add("=Gender=1"); continue; }
            if (t is "수" or "수컷" or "♂") { filters.Add("=Gender=0"); continue; }
            if (t is "무성" or "성별없는") { filters.Add("=Gender=2"); continue; }
            if (t is "닉네임" or "별명" or "닉")
            {
                if (next is "없는" or "안붙은" or "없음") { filters.Add("=IsNicknamed=false"); i++; }
                else { filters.Add("=IsNicknamed=true"); if (next is "붙은" or "있는" or "달린" or "붙인") i++; }
                continue;
            }
            if (t is "닉붙은" or "닉네임붙은" or "별명붙은" or "닉네임있는") { filters.Add("=IsNicknamed=true"); continue; }
            if (t is "알") { filters.Add("=IsEgg=true"); continue; }
            if (t is "우두머리" or "알파") { filters.Add("=IsAlpha=true"); continue; }
            if (t is "불법" or "불법인") { filters.Add("=Legal=false"); continue; }
            if (t is "합법" or "합법인") { filters.Add("=Legal=true"); continue; }
            if (Level(t, next) is { } lv)
            {
                if (lv.Used) i++;
                var after = i + 1 < split ? w[i + 1].Word : "";
                var op = after switch { "이상" => "≥", "이하" => "≤", "초과" => ">", "미만" => "<", _ => "=" };
                if (op != "=") i++;
                filters.Add($"{op}CurrentLevel={lv.Value}"); continue;
            }
            if (t is "아닌" or "아니고" or "아닌것" or "제외" or "빼고" && filters.Count > 0 && filters[^1][0] == '=') { filters[^1] = "!" + filters[^1][1..]; continue; }
            if (t is not ("레벨" or "렙") && QuickParse.BallOf(t) is var b and > 0) { filters.Add($"=Ball={b}"); continue; }
            if (QuickParse.NatureOf(t) is var na and >= 0) { filters.Add($"=Nature={na}"); continue; }
            if (Noise.Contains(t) || t is "레벨" or "렙") continue;
            r.Unknown.Add(toks[i]);
        }

        // ── 바꿀 것 ──
        var mods = new List<string>(); bool format8 = sav.Generation >= 8;
        for (int i = split; i < w.Count; i++)
        {
            var (t, _) = w[i]; string next = i + 1 < w.Count ? w[i + 1].Word : "";
            bool clearNext = Clear.Contains(next);
            if (Verbs.Contains(t) || Clear.Contains(t) || Noise.Contains(t)) continue;
            if (t is "어버이" or "트레이너" or "ot" or "어버이를")
            {
                if (clearNext) { r.Notes.Add("어버이는 없앨 수 없어 무시했습니다"); i++; continue; }
                if (i + 1 >= w.Count) { r.Unknown.Add(toks[i]); continue; }
                i++; var name = w[i].To ? Regex.Replace(toks[i].Trim(), "(으로|로)$", "") : toks[i].Trim();
                string tid = "", sid = "";
                if (i + 1 < w.Count && Regex.IsMatch(w[i + 1].Word, @"^\d{1,6}$")) { tid = w[++i].Word; if (i + 1 < w.Count && Regex.IsMatch(w[i + 1].Word, @"^\d{1,5}$")) sid = w[++i].Word; }
                mods.Add($"@어버이={name}{(tid.Length > 0 ? "/" + tid : "")}{(sid.Length > 0 ? "/" + sid : "")}"); continue;
            }
            if (t is "닉네임" or "별명" or "닉" or "이름")
            {
                if (clearNext || next is "없이" or "없음") { mods.Add(".IsNicknamed=false"); i++; continue; }
                if (i + 1 < w.Count && w[i + 1].To) { var nick = toks[++i].Trim(); nick = Regex.Replace(nick, "(으로|로)$", ""); mods.Add($".Nickname={nick}"); mods.Add(".IsNicknamed=true"); continue; }
                r.Unknown.Add(toks[i]); continue;
            }
            if (t is "닉네임없이" or "닉없이" or "닉네임해제") { mods.Add(".IsNicknamed=false"); continue; }
            if (t is "성별") continue;
            if (t is "암" or "암컷" or "♀") { mods.Add("@성별=1"); continue; }
            if (t is "수" or "수컷" or "♂") { mods.Add("@성별=0"); continue; }
            if (t is "이로치" or "색違い" or "shiny") { mods.Add(clearNext ? ".PID=$rand" : ".PID=$shiny"); if (clearNext) i++; continue; }
            if (t is "별" or "별이로치" or "별로치") { mods.Add(".PID=$shiny1"); continue; }
            if (t is "네모" or "네모이로치" or "네모로치") { mods.Add(".PID=$shiny0"); continue; }
            if (t is "일반" or "일반색" or "노말색") { mods.Add(".PID=$rand"); r.Notes.Add("일반 색: 성격값을 새로 뽑음 (드물게 다시 이로치일 수 있음)"); continue; }
            if (t is "레벨" or "렙" && next is "최대" or "만렙") { mods.Add(".CurrentLevel=100"); i++; continue; }
            if (t is "만렙" or "레벨최대") { mods.Add(".CurrentLevel=100"); continue; }
            if (Level(t, next) is { } lv) { if (lv.Used) i++; mods.Add($".CurrentLevel={lv.Value}"); continue; }
            if (t is "레벨" or "렙") continue;
            if (t is "볼" or "몬스터볼종류") continue;
            if (t is not ("레벨" or "렙") && QuickParse.BallOf(t) is var b and > 0) { mods.Add($".Ball={b}"); continue; }
            if (t is "성격") continue;
            if (QuickParse.NatureOf(t) is var na and >= 0) { mods.Add($".Nature={na}"); if (format8) mods.Add($".StatAlignment={na}"); continue; }
            if (t is "6v" or "6V" or "개체값최대" or "올v" or "올브이" or "최대개체값" || (t == "개체값" && next is "최대" or "31")) { if (t == "개체값") i++; foreach (var iv in new[] { "HP", "ATK", "DEF", "SPA", "SPD", "SPE" }) mods.Add($".IV_{iv}=31"); continue; }
            if (t is "친밀도" or "친밀도최대" or "친밀도max") { if (next is "최대" or "max" or "255") i++; mods.Add(".CurrentFriendship=255"); continue; }
            if (t is "리본")
            {
                if (clearNext) { mods.Add(".Ribbons=$suggestNone"); i++; continue; }
                if (next is "전부" or "모두" or "최대" or "다") i++;
                mods.Add(".Ribbons=$suggestAll"); continue;
            }
            if (t is "리본전부" or "모든리본" or "리본최대") { mods.Add(".Ribbons=$suggestAll"); continue; }
            if (t is "기술" or "기술배치" && next is "추천" or "추천기술" or "자동") { mods.Add(".Moves=$suggest"); i++; continue; }
            if (t is "추천기술" or "기술추천") { mods.Add(".Moves=$suggest"); continue; }
            if (t is "노력치" && (clearNext || next is "0")) { foreach (var ev in new[] { "HP", "ATK", "DEF", "SPA", "SPD", "SPE" }) mods.Add($".EV_{ev}=0"); i++; continue; }
            if (t is "숨특" or "숨겨진특성" or "드림특성") { mods.Add(".AbilityNumber=$2"); continue; }
            if (t is "특성1" or "1특성") { mods.Add(".AbilityNumber=$0"); continue; }
            if (t is "특성2" or "2특성") { mods.Add(".AbilityNumber=$1"); continue; }
            if (t is "특성") continue;
            if (Lang(t) is int lg) { mods.Add($".Language={lg}"); continue; }
            if (t is "언어") continue;
            if (t is "테라" or "테라타입" && i + 1 < w.Count && QuickParse.TeraOf(w[i + 1].Word) is int tt) { mods.Add($".TeraTypeOverride={tt}"); i++; continue; }
            if (QuickParse.TeraOf(t.Replace("테라", "")) is int tt2 && t.Contains("테라")) { mods.Add($".TeraTypeOverride={tt2}"); continue; }
            if (t is "크기" or "사이즈" or "최대크기" or "최소크기" or "가장크게" or "가장작게")
            {
                bool max = t is "최대크기" or "가장크게" || next is "최대" or "가장크게" or "255";
                bool min = t is "최소크기" or "가장작게" || next is "최소" or "가장작게" or "0";
                if (!max && !min) { r.Unknown.Add(toks[i]); continue; }
                if (t is "크기" or "사이즈") i++;
                int v = max ? 255 : 0;
                if (sav.Context is EntityContext.Gen9 or EntityContext.Gen9a or EntityContext.Gen8a) mods.Add($".Scale={v}");
                if (sav.Generation >= 8) { mods.Add($".HeightScalar={v}"); mods.Add($".WeightScalar={v}"); }
                continue;
            }
            if (t is "회복" or "치료") { mods.Add(".Heal=$suggest"); continue; }
            if (t is "최대" or "만") continue;
            r.Unknown.Add(toks[i]);
        }
        if (mods.Count == 0) { r.Notes.Add($"'{line}': 바꿀 것을 알아보지 못했습니다 (예: '암컷으로', '문볼로', '닉네임 없애기')"); return []; }

        // 종(계열) 조건: 종마다 세트 하나 (PKHeX 조건은 모두 '그리고'라서)
        var spList = species.Distinct().ToList();
        if (family)
        {
            var tree = EvolutionTree.GetEvolutionTree(sav.Context);
            var all = new List<ushort>();
            foreach (var s0 in spList)
            {
                var root = tree.GetBaseSpeciesForm(s0, 0).Species;
                for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
                    try { if (tree.GetBaseSpeciesForm(s, 0).Species == root && !all.Contains(s)) all.Add(s); } catch { }
            }
            if (all.Count > 0) { spList = all; r.Notes.Add("계열: " + string.Join("·", all.Select(x => GenHost.Strings.Species[x]))); }
        }
        if (spList.Count == 0) return [[.. filters, .. mods]];
        return spList.Select(s => new List<string> { $"=Species={s}" }.Concat(filters).Concat(mods).ToList()).ToList();
    }

    private static (int Value, bool Used)? Level(string t, string next)
    {
        var m = Regex.Match(t, @"^(?:lv\.?|레벨|렙)(\d{1,3})$|^(\d{1,3})(?:렙|레벨|lv)$");
        if (m.Success) return (Math.Clamp(int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value), 1, 100), false);
        if (t is "레벨" or "렙" or "lv" && Regex.IsMatch(next, @"^\d{1,3}$")) return (Math.Clamp(int.Parse(next), 1, 100), true);
        return null;
    }

    private static int? Lang(string t) => t switch
    {
        "한국어" or "한국판" or "한글" or "한국" => 8, "일본어" or "일본판" or "일어" or "일본" => 1, "영어" or "영문판" or "영어판" => 2,
        "프랑스어" or "불어" => 3, "이탈리아어" => 4, "독일어" => 5, "스페인어" => 7, "중국어" or "간체" or "중국어간체" => 9, "번체" or "중국어번체" => 10, _ => null,
    };

    public sealed record Set(string[] Filters, string[] Pkhex, List<string> Custom) { public bool HasPkhex => Pkhex.Any(l => l.Length > 0 && !StringInstruction.IsFilterInstruction(l[0])); }
    /// <summary>명령어를 세트(';'로 나눔)별로: 조건, PKHeX 명령(조건 포함), '@' 명령.</summary>
    public static List<Set> Sets(IEnumerable<string> lines)
    {
        var sets = new List<Set>(); var filters = new List<string>(); var pk = new List<string>(); var cur = new List<string>();
        void Flush() { if (pk.Count > 0 || cur.Count > 0) sets.Add(new Set(filters.ToArray(), pk.ToArray(), cur)); filters = []; pk = []; cur = []; }
        foreach (var l0 in lines)
        {
            var l = l0.Trim(); if (l.Length == 0) continue;
            if (l.StartsWith(';')) { Flush(); continue; }
            if (l.StartsWith('@')) { cur.Add(l); continue; }
            if (StringInstruction.IsFilterInstruction(l[0])) filters.Add(l);
            pk.Add(l);
        }
        Flush();
        return sets;
    }

    /// <summary>'@' 명령 하나를 개체에 적용. 반환: 바뀌었는지.</summary>
    public static bool ApplyCustom(PKM p, string cmd)
    {
        var i = cmd.IndexOf('='); if (i < 0) return false;
        var key = cmd[1..i].Trim(); var val = cmd[(i + 1)..].Trim();
        switch (key)
        {
            case "성별":
                if (!byte.TryParse(val, out var g) || p.Gender == g || p.PersonalInfo.Genderless || (g == 0 && p.PersonalInfo.OnlyFemale) || (g == 1 && p.PersonalInfo.OnlyMale)) return false;
                bool shiny = p.IsShiny; bool square = shiny && p.ShinyXor == 0;
                p.SetGender(g);
                if (shiny && !p.IsShiny) p.SetShiny(square ? Shiny.AlwaysSquare : Shiny.AlwaysStar);
                p.RefreshChecksum(); return true;
            case "어버이":
            {
                var parts = val.Split('/');
                var name = parts[0].Trim(); if (name.Length == 0) return false;
                bool six = p.Format >= 7;
                uint id32 = p.ID32;
                if (parts.Length > 1 && int.TryParse(parts[1], out var tid))
                {
                    int sid = parts.Length > 2 && int.TryParse(parts[2], out var sd) ? sd : (six ? (int)(p.ID32 / 1_000_000) : p.SID16);
                    id32 = six ? (uint)(Math.Min(sid, 4294) * 1_000_000L + Math.Min(tid, 999_999)) : (uint)((Math.Min(sid, 65535) << 16) | Math.Min(tid, 65535));
                }
                if (p.OriginalTrainerName == name && p.ID32 == id32) return false;
                GenHost.AutoOT(p, name, id32, p.OriginalTrainerGender, p.Language);
                p.RefreshChecksum(); return true;
            }
        }
        return false;
    }

    /// <summary>세트의 조건(=·!·>·<…)이 맞는지 (PKHeX와 같은 판단).</summary>
    public static bool Matches(PKM p, string[] filters)
    {
        if (filters.Length == 0) return true;
        var set = new StringInstructionSet(filters.AsSpan());
        var normal = set.Filters.Where(f => f.PropertyName != "Legal").ToList();
        if (!EntityBatchEditor.Instance.IsFilterMatch(normal, p)) return false;
        foreach (var f in set.Filters.Where(f => f.PropertyName == "Legal"))
            if (new LegalityAnalysis(p).Valid != f.PropertyValue.Equals("true", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}
