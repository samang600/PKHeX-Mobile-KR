using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
#if !LOCTEST
using Microsoft.Maui.Handlers;
#endif

namespace PKHeXKR;

/// <summary>
/// 화면 번역 (한국어 원문 → 영어·일본어). 코드는 한국어 문자열을 그대로 쓰고, 화면에 나가는 순간에 바꿈:
/// Label·Button 글자, Entry·Editor·SearchBar 안내문은 핸들러 매핑에서, 대화상자는 *Tr 확장 메서드에서.
/// 사전(Resources/Raw/loc.en.json·loc.ja.json)은 원문 → 번역. 보간 문자열은 {0},{1} 구멍이 있는 틀로 맞춤.
/// 데이터 언어(설정)를 따라가며, 한국어면 아무것도 하지 않음.
/// </summary>
public static class Loc
{
    private static string lang = "ko";
    private static Dictionary<string, string> exact = new();
    private static List<Template> templates = [];
    private static (string Ko, string Tr)[] fragments = [];
    private static readonly Dictionary<string, string> cache = new();
    private static readonly Regex Hangul = new(@"[가-힣ㄱ-ㆎ]", RegexOptions.Compiled);

    public static bool Active => lang != "ko";
    public static string Language => lang;

    /// <summary>데이터 언어 설정 (ko·en·ja; 그 밖의 언어는 영어 화면)</summary>
    public static void SetLanguage(string code)
    {
        var l = code switch { "ko" => "ko", "ja" => "ja", _ => "en" };
        lock (cache)
        {
            lang = l; cache.Clear();
            exact = new(); templates = []; fragments = [];
            if (l == "ko") return;
            Dictionary<string, string> dict;
            try
            {
#if LOCTEST
                using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, $"loc.{l}.json"));
#else
                using var s = FileSystem.OpenAppPackageFileAsync($"loc.{l}.json").GetAwaiter().GetResult();
#endif
                dict = JsonSerializer.Deserialize(s, LocJson.Default.DictionaryStringString) ?? new();
            }
            catch { dict = new(); }
            var frag = new List<(string, string)>();
            foreach (var (ko, tr) in dict)
            {
                if (string.IsNullOrEmpty(tr)) continue;
                if (HolePattern.IsMatch(ko)) { templates.Add(new Template(ko, tr)); continue; }
                exact[ko] = tr;
                if (ko.Trim().Length >= 2 && Hangul.IsMatch(ko) && ko.IndexOfAny(RegexChars) < 0) frag.Add((ko.Trim(), tr.Trim()));
            }
            templates.Sort((a, b) => b.LiteralLength.CompareTo(a.LiteralLength));
            fragments = frag.OrderByDescending(f => f.Item1.Length).ToArray();
        }
    }

    private static readonly char[] RegexChars = "^$\\|[".ToCharArray();
    private static readonly Regex HolePattern = new(@"\{\d+\}", RegexOptions.Compiled);

    // ───────── 번역 ─────────
    /// <summary>한국어 원문 → 지금 언어. 모르면 아는 조각만 바꾸고, 그래도 없으면 그대로.</summary>
    public static string T(string s)
    {
        if (!Active || string.IsNullOrEmpty(s) || !Hangul.IsMatch(s)) return s;
        lock (cache)
        {
            if (cache.TryGetValue(s, out var c)) return c;
            var r = Translate(s, 0, allowFragments: true);
            if (cache.Count > 20000) cache.Clear();
            cache[s] = r;
            return r;
        }
    }

    private static string Translate(string s, int depth, bool allowFragments)
    {
        if (!Hangul.IsMatch(s) || depth > 4) return s;
        if (exact.TryGetValue(s, out var e)) return e;
        // 앞뒤 공백·줄바꿈은 그대로 두고 가운데만
        int a = 0, b = s.Length;
        while (a < b && char.IsWhiteSpace(s[a])) a++;
        while (b > a && char.IsWhiteSpace(s[b - 1])) b--;
        if (a > 0 || b < s.Length)
        {
            var mid = s[a..b];
            var t = Translate(mid, depth + 1, allowFragments);
            return t == mid ? s : s[..a] + t + s[b..];
        }
        foreach (var tp in templates)
            if (tp.TryApply(s, x => Translate(x, depth + 1, allowFragments: false), out var r)) return r;
        if (s.Contains('\n'))
        {
            var parts = s.Split('\n');
            return string.Join("\n", parts.Select(p => Translate(p, depth + 1, allowFragments)));
        }
        foreach (var sep in new[] { " · ", "  ·  ", " / ", ": ", " — " })
            if (s.Contains(sep))
            {
                var parts = s.Split(sep);
                var joined = string.Join(sep, parts.Select(p => Translate(p, depth + 1, allowFragments)));
                if (joined != s) return joined;
            }
        if (!allowFragments) return s;
        // 문장 가운데 끼어 있는 틀 ("3. 피카츄 Lv.5  ⚠ {0} 포켓몬이 아님  · 불법")
        bool changed = false;
        foreach (var tp in templates)
            if (tp.TryReplaceInside(ref s, x => Translate(x, depth + 1, allowFragments: false))) { changed = true; if (!Hangul.IsMatch(s)) return s; }
        // 마지막: 아는 조각을 긴 것부터 바꿈 ("어버이 새아 468686" → "OT 새아 468686")
        var sb = new StringBuilder(s);
        foreach (var (ko, tr) in fragments)
        {
            if (sb.Length < ko.Length) continue;
            var cur = sb.ToString();
            var at = cur.IndexOf(ko, StringComparison.Ordinal);
            if (at < 0) continue;
            sb.Replace(ko, tr); changed = true;
            if (!Hangul.IsMatch(sb.ToString())) break;
        }
        return changed ? sb.ToString() : s;
    }

    private sealed class Template
    {
        private readonly string[] lits;   // 구멍 사이 글자 (구멍 수 + 1개)
        private readonly int[] order;     // i번째 구멍의 원래 번호
        private readonly string tr;
        private Regex rx;
        public int LiteralLength { get; }

        public Template(string ko, string tr)
        {
            this.tr = tr;
            var ms = HolePattern.Matches(ko);
            var l = new List<string>(); var o = new List<int>(); int pos = 0;
            foreach (Match m in ms) { l.Add(ko[pos..m.Index]); o.Add(int.Parse(m.Value[1..^1])); pos = m.Index + m.Length; }
            l.Add(ko[pos..]);
            lits = l.ToArray(); order = o.ToArray();
            LiteralLength = lits.Sum(x => x.Length);
        }

        private Regex inside;
        /// <summary>앞뒤 글자가 모두 한글을 품은 틀만: 문자열 안 어디에 있어도 바꿈 (구멍은 한 줄 안에서만).</summary>
        public bool TryReplaceInside(ref string s, Func<string, string> sub)
        {
            if (lits[0].Length == 0 || lits[^1].Length == 0 || !Hangul.IsMatch(lits[^1]) || s.Length < LiteralLength) return false;
            if (!s.Contains(lits[0], StringComparison.Ordinal) || !s.Contains(lits[^1], StringComparison.Ordinal)) return false;
            inside ??= new Regex(string.Join("([^\n]*?)", lits.Select(Regex.Escape)), RegexOptions.CultureInvariant);
            bool hit = false;
            s = inside.Replace(s, m =>
            {
                hit = true;
                var vals = new string[order.Length == 0 ? 0 : order.Max() + 1];
                for (int i = 0; i < order.Length; i++) vals[order[i]] = sub(m.Groups[i + 1].Value);
                return HolePattern.Replace(tr, h => { var n = int.Parse(h.Value[1..^1]); return n < vals.Length ? vals[n] ?? "" : h.Value; });
            });
            return hit;
        }

        public bool TryApply(string s, Func<string, string> sub, out string result)
        {
            result = null;
            if (LiteralLength == 0) return false;
            if (!s.StartsWith(lits[0], StringComparison.Ordinal) || !s.EndsWith(lits[^1], StringComparison.Ordinal)) return false;
            if (s.Length < LiteralLength) return false;
            rx ??= new Regex("^" + string.Join("(.*?)", lits.Select(Regex.Escape)) + "$", RegexOptions.Singleline | RegexOptions.CultureInvariant);
            var m = rx.Match(s);
            if (!m.Success) return false;
            var vals = new string[order.Length == 0 ? 0 : order.Max() + 1];
            for (int i = 0; i < order.Length; i++) vals[order[i]] = sub(m.Groups[i + 1].Value);
            result = HolePattern.Replace(tr, h => { var n = int.Parse(h.Value[1..^1]); return n < vals.Length ? vals[n] ?? "" : h.Value; });
            return true;
        }
    }

#if !LOCTEST
    // ───────── 화면에 붙이기 ─────────
    private static readonly ConditionalWeakTable<BindableObject, Dictionary<BindableProperty, (string Orig, string Shown)>> shown = new();
    [ThreadStatic] private static bool applying;

    /// <summary>앱 시작 때 한 번 (MauiProgram): 글자가 바뀔 때마다 번역해서 다시 넣음.</summary>
    public static void Install()
    {
        // MAUI는 컨트롤별 매핑(Controls용 ReplaceMapping)을 그 컨트롤을 처음 만들 때 함 → 먼저 하나씩 만들어 두고 그 뒤에 덧붙임
        // (안 그러면 Label의 Text 매핑이 나중에 통째로 바뀌어 번역이 빠짐)
        _ = new Label(); _ = new Button(); _ = new Entry(); _ = new Editor(); _ = new SearchBar(); _ = new Span();
        LabelHandler.Mapper.AppendToMapping(nameof(ILabel.Text), (h, v) => { if (v is Label l && l.FormattedText == null) Apply(l, Label.TextProperty); });
        LabelHandler.Mapper.AppendToMapping(nameof(Label.FormattedText), (h, v) => { if (v is Label l) ApplySpans(l); });
        ButtonHandler.Mapper.AppendToMapping(nameof(IText.Text), (h, v) => { if (v is Button b) Apply(b, Button.TextProperty); });
        EntryHandler.Mapper.AppendToMapping(nameof(IPlaceholder.Placeholder), (h, v) => { if (v is Entry e) Apply(e, Entry.PlaceholderProperty); });
        EditorHandler.Mapper.AppendToMapping(nameof(IPlaceholder.Placeholder), (h, v) => { if (v is Editor e) Apply(e, Editor.PlaceholderProperty); });
        SearchBarHandler.Mapper.AppendToMapping(nameof(IPlaceholder.Placeholder), (h, v) => { if (v is SearchBar e) Apply(e, SearchBar.PlaceholderProperty); });
    }

    private static void Apply(BindableObject o, BindableProperty p)
    {
        if (applying) return;
        var cur = o.GetValue(p) as string;
        shown.TryGetValue(o, out var map);
        // 코드가 새 글자를 넣었으면 그게 새 원문
        string orig = map != null && map.TryGetValue(p, out var st) && st.Shown == cur ? st.Orig : cur;
        if (orig == cur && (!Active || string.IsNullOrEmpty(cur) || !Hangul.IsMatch(cur))) { map?.Remove(p); return; }
        var t = Active ? T(orig) : orig;
        map ??= shown.GetOrCreateValue(o);
        if (t == cur) { if (orig != cur) map[p] = (orig, cur); else map.Remove(p); return; }
        map[p] = (orig, t);
        applying = true;
        try { o.SetValue(p, t); } finally { applying = false; }
    }

    private static void ApplySpans(Label l)
    {
        if (l.FormattedText == null) return;
        foreach (var sp in l.FormattedText.Spans) Apply(sp, Span.TextProperty);
    }

    /// <summary>언어를 바꾼 뒤: 열려 있는 화면의 글자를 원문에서 다시 번역.</summary>
    public static void Refresh()
    {
        try
        {
            foreach (var w in Application.Current?.Windows ?? [])
            {
                if (w.Page is not { } page) continue;
                Walk(page);
                foreach (var m in page.Navigation?.ModalStack ?? []) Walk(m);
            }
        }
        catch { }
    }

    private static void Walk(IVisualTreeElement e)
    {
        switch (e)
        {
            case Label l when l.FormattedText != null: ApplySpans(l); break;
            case Label l: Apply(l, Label.TextProperty); break;
            case Button b: Apply(b, Button.TextProperty); break;
            case Entry en: Apply(en, Entry.PlaceholderProperty); break;
            case Editor ed: Apply(ed, Editor.PlaceholderProperty); break;
            case SearchBar sb: Apply(sb, SearchBar.PlaceholderProperty); break;
        }
        foreach (var c in e.GetVisualChildren()) Walk(c);
    }

    /// <summary>묶인(Binding) 글자용 변환기: ItemTemplate의 Label.Text에 씀.</summary>
    public static readonly IValueConverter Conv = new TrConverter();
    private sealed class TrConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is string s ? T(s) : value;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value;
    }

    // ───────── 대화상자 ─────────
    public static Task DisplayAlertTr(this Page p, string title, string message, string cancel)
        => p.DisplayAlertAsync(T(title), T(message), T(cancel));

    public static Task<bool> DisplayAlertTr(this Page p, string title, string message, string accept, string cancel)
        => p.DisplayAlertAsync(T(title), T(message), T(accept), T(cancel));

    /// <summary>고른 단추는 원문(한국어)으로 돌려줌 → 기존 비교 코드 그대로.</summary>
    public static async Task<string> DisplayActionSheetTr(this Page p, string title, string cancel, string destruction, params string[] buttons)
    {
        var map = new Dictionary<string, string>();
        string Tr(string s)
        {
            if (s == null) return null;
            var t = T(s);
            while (map.TryGetValue(t, out var prev) && prev != s) t += " ";   // 번역이 겹치면 구분
            map[t] = s; return t;
        }
        var trTitle = T(title); var trCancel = Tr(cancel); var trDestr = Tr(destruction);
        var trButtons = buttons?.Select(Tr).ToArray() ?? [];
        var r = await p.DisplayActionSheetAsync(trTitle, trCancel, trDestr, trButtons);
        return r != null && map.TryGetValue(r, out var o) ? o : r;
    }

    public static Task<string> DisplayPromptTr(this Page p, string title, string message, string accept = "확인", string cancel = "취소", string placeholder = null, int maxLength = -1, Keyboard keyboard = null, string initialValue = "")
        => p.DisplayPromptAsync(T(title), T(message), T(accept), T(cancel), T(placeholder), maxLength, keyboard, initialValue);
#endif
}

[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class LocJson : System.Text.Json.Serialization.JsonSerializerContext { }
