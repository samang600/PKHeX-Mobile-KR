using System.Text;
using System.Text.Json;
using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// 빠른 생성 문장 해석용 언어별 사전 (Lexicon/lexicon.&lt;lang&gt;.json, 앱에 내장).
/// 파서는 "이 말이 FEMALE 키인가"처럼 뜻(키)만 묻고, 실제 낱말은 언어 파일이 가짐.
/// 영어 낱말은 어느 언어에서나 통하게 영어 사전도 같이 봄 (shiny, 6v, ha, gmax …).
/// </summary>
public sealed class Lexicon
{
    public string Lang { get; private set; } = "ko";
    /// <summary>키 → 낱말들 (정규화됨)</summary>
    public Dictionary<string, HashSet<string>> Keys { get; } = new();
    public HashSet<string> AllLine { get; } = new();
    public HashSet<string> CommonLine { get; } = new();
    /// <summary>크기 말 → (최소 배율, 최대 배율, 표시 이름)</summary>
    public Dictionary<string, (int Min, int Max, string Name)> Sizes { get; } = new();
    public Dictionary<string, string> SizeText { get; } = new();
    /// <summary>볼 줄임말 → 영어 볼 이름 (볼 번호는 게임 문자열로 찾음)</summary>
    public Dictionary<string, string> BallAlias { get; } = new();
    public List<string> MarkSuffix { get; } = new();
    /// <summary>증표 줄임말(접미사 뺀 것) → RibbonIndex 이름</summary>
    public Dictionary<string, string> MarkAlias { get; } = new();
    /// <summary>접미사 없이 그 말만으로 증표인 것</summary>
    public Dictionary<string, string> MarkWord { get; } = new();
    /// <summary>리본 줄임말 → 리본 속성 이름 (CHAMPION = 지금 게임 지방)</summary>
    public Dictionary<string, string> RibbonAlias { get; } = new();
    /// <summary>타입 줄임말 → 영어 타입 이름</summary>
    public Dictionary<string, string> TypeAlias { get; } = new();
    public HashSet<string> Stellar { get; } = new();
    /// <summary>리전폼 접두어 → 폼 이름 조각 (원문 그대로)</summary>
    public Dictionary<string, string> Regions { get; } = new();
    public List<string> FormStrip { get; } = new();
    public Dictionary<string, string[]> Suffix { get; } = new();
    /// <summary>포켓몬 줄임말 → 정식 이름 (원문, '/'는 여러 마리)</summary>
    public Dictionary<string, string> Aliases { get; } = new();
    public Dictionary<string, string> Ui { get; } = new();
    public string[] AbilityNames { get; private set; } = ["1", "2", "H"];
    public Dictionary<string, string> Msg { get; } = new();
    public string Hint { get; private set; } = "";

    // ---------- 현재 언어
    private static readonly Dictionary<string, Lexicon> cache = new();
    private static string lang = "ko";
    /// <summary>해석에 쓸 언어 (ko·en·ja). 앱 언어를 바꿀 때 같이 바꿈</summary>
    public static string Language { get => lang; set { lang = Supported.Contains(value ?? "") ? value : "en"; } }
    public static readonly string[] Supported = ["ko", "en", "ja"];
    public static Lexicon Current => Get(lang);
    public static Lexicon En => Get("en");
    public static Lexicon Get(string l)
    {
        l = Supported.Contains(l ?? "") ? l : "en";
        lock (cache)
        {
            if (cache.TryGetValue(l, out var x)) return x;
            var lx = new Lexicon(); lx.Load(l); cache[l] = lx; return lx;
        }
    }

    // ---------- 낱말 정규화
    /// <summary>소문자·띄어쓰기 없음·전각→반각·히라가나→가타카나·라틴 악센트 제거. 모든 비교는 이 꼴로</summary>
    public static string Norm(string x)
    {
        if (string.IsNullOrEmpty(x)) return "";
        var s = x.Trim();
        bool wide = false;
        foreach (var ch in s) if (ch >= '　') { wide = true; break; }
        if (wide) s = s.Normalize(NormalizationForm.FormKC);   // 全角英数・半角カナ → 半角・全角カナ
        var sb = new StringBuilder(s.Length);
        foreach (var ch0 in s.ToLowerInvariant())
        {
            var ch = ch0;
            if (ch == ' ' || ch == '　' || ch == '\t') continue;
            if (ch >= 'ぁ' && ch <= 'ゖ') ch = (char)(ch + 0x60);   // ひらがな → カタカナ
            else if (ch == 'ー' || ch == '―' || ch == '—') ch = 'ー';   // 長音のゆれ
            else if (ch < 'ɐ') ch = Latin(ch);
            sb.Append(ch);
        }
        return sb.ToString();
    }
    private static char Latin(char c) => c switch
    {
        'é' or 'è' or 'ê' or 'ë' => 'e', 'á' or 'à' or 'â' or 'ä' or 'ã' or 'å' => 'a', 'í' or 'ì' or 'î' or 'ï' => 'i',
        'ó' or 'ò' or 'ô' or 'ö' or 'õ' => 'o', 'ú' or 'ù' or 'û' or 'ü' => 'u', 'ñ' => 'n', 'ç' => 'c', 'ß' => 's', _ => c,
    };

    // ---------- 조회
    /// <summary>정규화된 낱말이 이 뜻인가 (이 언어, 없으면 영어 사전)</summary>
    public bool Is(string norm, string key)
    {
        if (Keys.TryGetValue(key, out var set) && set.Contains(norm)) return true;
        return !ReferenceEquals(this, En) && En.Keys.TryGetValue(key, out var es) && es.Contains(norm);
    }
    public bool IsAny(string norm, params string[] keys) { foreach (var k in keys) if (Is(norm, k)) return true; return false; }
    public (int Min, int Max, string Name)? Size(string norm)
    {
        if (Sizes.TryGetValue(norm, out var v)) return v;
        if (!ReferenceEquals(this, En) && En.Sizes.TryGetValue(norm, out v)) return (v.Min, v.Max, SizeText.TryGetValue(SizeKey(v), out var n) ? n : v.Name);
        return null;
    }
    private static string SizeKey((int Min, int Max, string Name) v) => (v.Min, v.Max) switch { (0, 0) => "MIN", (255, 255) => "MAX", (0, 24) => "XS", (25, 59) => "S", (60, 195) => "M", (196, 230) => "L", (231, 255) => "XL", _ => "" };
    public string SizeName(string key) => SizeText.TryGetValue(key, out var n) ? n : key;
    public string Ball(string norm) => BallAlias.TryGetValue(norm, out var b) ? b : !ReferenceEquals(this, En) && En.BallAlias.TryGetValue(norm, out b) ? b : null;
    public string Mark(string norm) => MarkAlias.TryGetValue(norm, out var m) ? m : !ReferenceEquals(this, En) && En.MarkAlias.TryGetValue(norm, out m) ? m : null;
    public string MarkOnly(string norm) => MarkWord.TryGetValue(norm, out var m) ? m : !ReferenceEquals(this, En) && En.MarkWord.TryGetValue(norm, out m) ? m : null;
    public string Ribbon(string norm) => RibbonAlias.TryGetValue(norm, out var r) ? r : !ReferenceEquals(this, En) && En.RibbonAlias.TryGetValue(norm, out r) ? r : null;
    public string Type(string norm) => TypeAlias.TryGetValue(norm, out var t) ? t : !ReferenceEquals(this, En) && En.TypeAlias.TryGetValue(norm, out t) ? t : null;
    public bool IsStellar(string norm) => Stellar.Contains(norm) || En.Stellar.Contains(norm);
    public string[] Suf(string key) => Suffix.TryGetValue(key, out var s) ? s : [];
    public string Alias(string norm) => Aliases.TryGetValue(norm, out var a) ? a : null;
    public string U(string key) => Ui.TryGetValue(key, out var u) ? u : En.Ui.TryGetValue(key, out u) ? u : key;
    public string M(string key, params object[] args) { var f = Msg.TryGetValue(key, out var m) ? m : En.Msg.TryGetValue(key, out m) ? m : key; return args.Length > 0 ? string.Format(f, args) : f; }

    // ---------- 읽기
    private void Load(string l)
    {
        Lang = l;
        using var st = typeof(Lexicon).Assembly.GetManifestResourceStream($"PKHeXKR.Lexicon.lexicon.{l}.json")
            ?? throw new FileNotFoundException("lexicon." + l + ".json");
        using var doc = JsonDocument.Parse(st, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = doc.RootElement;
        if (root.TryGetProperty("keywords", out var kw))
            foreach (var p in kw.EnumerateObject()) Keys[p.Name] = new HashSet<string>(p.Value.EnumerateArray().Select(x => Norm(x.GetString())));
        if (root.TryGetProperty("allLine", out var al)) foreach (var x in al.EnumerateArray()) AllLine.Add(Norm(x.GetString()));
        if (root.TryGetProperty("commonLine", out var cl)) foreach (var x in cl.EnumerateArray()) CommonLine.Add(Norm(x.GetString()));
        if (root.TryGetProperty("sizeText", out var stx)) foreach (var p in stx.EnumerateObject()) SizeText[p.Name] = p.Value.GetString();
        if (root.TryGetProperty("sizes", out var sz))
            foreach (var p in sz.EnumerateObject())
            {
                var (min, max) = p.Name switch { "MIN" => (0, 0), "MAX" => (255, 255), "XS" => (0, 24), "S" => (25, 59), "M" => (60, 195), "L" => (196, 230), "XL" => (231, 255), _ => (-1, -1) };
                if (min < 0) continue;
                var name = SizeText.TryGetValue(p.Name, out var n) ? n : p.Name;
                foreach (var x in p.Value.EnumerateArray()) Sizes[Norm(x.GetString())] = (min, max, name);
            }
        if (root.TryGetProperty("balls", out var bl))
            foreach (var p in bl.EnumerateObject()) foreach (var x in p.Value.EnumerateArray()) BallAlias[Norm(x.GetString())] = p.Name;
        if (root.TryGetProperty("markSuffix", out var ms)) foreach (var x in ms.EnumerateArray()) MarkSuffix.Add(Norm(x.GetString()));
        if (root.TryGetProperty("marks", out var mk))
            foreach (var p in mk.EnumerateObject()) foreach (var x in p.Value.EnumerateArray()) MarkAlias.TryAdd(Norm(x.GetString()), p.Name);
        if (root.TryGetProperty("markWord", out var mw))
            foreach (var p in mw.EnumerateObject()) foreach (var x in p.Value.EnumerateArray()) MarkWord[Norm(x.GetString())] = p.Name;
        if (root.TryGetProperty("ribbonAlias", out var ra))
            foreach (var p in ra.EnumerateObject()) foreach (var x in p.Value.EnumerateArray()) RibbonAlias[Norm(x.GetString())] = p.Name;
        if (root.TryGetProperty("types", out var ty))
            foreach (var p in ty.EnumerateObject()) foreach (var x in p.Value.EnumerateArray()) TypeAlias[Norm(x.GetString())] = p.Name;
        if (root.TryGetProperty("stellar", out var stl)) foreach (var x in stl.EnumerateArray()) Stellar.Add(Norm(x.GetString()));
        if (root.TryGetProperty("regions", out var rg)) foreach (var p in rg.EnumerateObject()) Regions[Norm(p.Name)] = p.Value.GetString();
        if (root.TryGetProperty("formStrip", out var fs)) foreach (var x in fs.EnumerateArray()) FormStrip.Add(Norm(x.GetString()));
        if (root.TryGetProperty("suffix", out var sf)) foreach (var p in sf.EnumerateObject()) Suffix[p.Name] = p.Value.EnumerateArray().Select(x => Norm(x.GetString())).ToArray();
        if (root.TryGetProperty("aliases", out var ali)) foreach (var p in ali.EnumerateObject()) Aliases[Norm(p.Name)] = p.Value.GetString();
        if (root.TryGetProperty("rotom", out var ro))
        {
            var baseName = ro.GetProperty("base").GetString();
            var tails = ro.GetProperty("tails").EnumerateArray().Select(x => x.GetString()).ToList();
            if (ro.TryGetProperty("forms", out var forms))
                foreach (var f in forms.EnumerateObject())
                    foreach (var nick in f.Value.EnumerateArray())
                        foreach (var tail in tails) Aliases.TryAdd(Norm(nick.GetString() + tail), baseName + " " + f.Name);
            if (ro.TryGetProperty("plain", out var plain)) foreach (var x in plain.EnumerateArray()) Aliases.TryAdd(Norm(x.GetString()), baseName);
        }
        if (root.TryGetProperty("ui", out var ui))
            foreach (var p in ui.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Array) { if (p.Name == "ABILITY") AbilityNames = p.Value.EnumerateArray().Select(x => x.GetString()).ToArray(); }
                else Ui[p.Name] = p.Value.GetString();
            }
        if (root.TryGetProperty("msg", out var msg)) foreach (var p in msg.EnumerateObject()) Msg[p.Name] = p.Value.GetString();
        if (root.TryGetProperty("hint", out var h)) Hint = h.GetString() ?? "";
    }
}
