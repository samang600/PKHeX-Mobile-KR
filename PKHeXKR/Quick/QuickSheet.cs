using PKHeX.Core;
using PKHeX.Core.AutoMod;
using System.Reflection;
namespace PKHeXKR;

/// <summary>텍스트로 빠른 생성: 대충 쓴 주문 문장을 해석해 합법 개체를 만듦 (테스트 기능).</summary>
public class QuickSheet : Sheet
{
    private readonly Editor input = new() { AutoSize = EditorAutoSizeOption.TextChanges, FontSize = 15, MinimumHeightRequest = 120, Placeholder = "예)\n어버이: 새아 468686\n빠르모트 암컷 이로치 러브볼 가장작게 / 찌리배리 레벨볼 큰증\n싸리용(뻗은,젖힌) 우두로치 다이브볼 가장크게\n망나뇽 암" };
    private readonly VerticalStackLayout preview = new() { Spacing = 6 };
    private readonly Label status = T.L("", 13, sub: true);
    private readonly Button parseBtn = T.Pill("해석해 보기"), makeBtn = T.Pill("만들기", primary: true), stopBtn = T.Pill("중지");
    private CancellationTokenSource cts;
    private static string lastText = "";

    public QuickSheet() : base("텍스트로 빠른 생성")
    {
        input.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
        input.Text = lastText;
        status.LineBreakMode = LineBreakMode.WordWrap;
        parseBtn.Clicked += (_, _) => ShowParse();
        makeBtn.Clicked += async (_, _) => await Make();
        stopBtn.Clicked += (_, _) => cts?.Cancel(); stopBtn.IsVisible = false;
        var dictBtn = T.Pill("내 사전"); dictBtn.Clicked += (_, _) => SheetHost.Show(new UserDictSheet());
        var clearBtn = T.Pill("지우기"); clearBtn.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text)) return;
            if (!await Application.Current.Windows[0].Page.DisplayAlertAsync("지우기", "입력한 내용을 모두 지울까요?", "지우기", "취소")) return;
            input.Text = ""; lastText = ""; preview.Children.Clear(); status.Text = "";
        };
        var frame = new Border { Content = input, StrokeThickness = 1, Padding = 6, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
        frame.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D1D5DB"), Color.FromArgb("#374151"));
        var hint = T.L("한 줄에 한 마리(괄호 안에 '/'로 적어도 됨: 에써르 (수/ 작증/ 럽볼)). 포켓몬·성별(암/수)·이로치(이로치·우두로치·별·네모)·볼(럽볼·렙볼·프볼…)·크기(가장작게·L·가장크게)·증표(작증·큰증·카레)·성격·숨특·6V·A0·Lv50·x3 등을 순서 상관없이 적으세요. '어버이: 이름 TID' 줄은 아래 전체에 적용되고, 적지 않은 값은 게임에서 자연스럽게 나오는 값으로 채웁니다. 한 마리면 편집기로, 여러 마리면 현재 박스부터 빈 칸에 넣습니다.", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, frame, new HorizontalStackLayout { Spacing = 8, Children = { parseBtn, makeBtn, dictBtn, clearBtn, stopBtn } }, status, preview } } });
    }

    private List<OrderSpec> Parse() { lastText = input.Text ?? ""; return QuickParse.Parse(lastText, AppState.Sav, UserDict.Load()); }

    private void ShowParse()
    {
        var list = Parse(); preview.Children.Clear();
        foreach (var o in list) preview.Children.Add(Line(o, null));
        status.Text = $"{list.Count}마리로 해석했습니다" + (list.Any(x => x.Unknown.Count > 0) ? " · '?' 표시 단어는 알아보지 못해 무시합니다" : "");
    }

    private static View Line(OrderSpec o, string result, bool? ok = null)
    {
        var s = GameInfo.Strings;
        var main = T.L((ok == null ? "" : ok.Value ? "✔ " : "✖ ") + (o.Species == 0 ? "(포켓몬 없음)" : o.Describe(s)), 14, bold: true); main.LineBreakMode = LineBreakMode.WordWrap;
        if (ok == false) main.TextColor = T.Bad;
        var sub = T.L("입력: " + o.Raw.Trim() + (o.Unknown.Count > 0 ? "   ? " + string.Join(", ", o.Unknown) : "") + (o.Notes.Count > 0 ? "   ※ " + string.Join("; ", o.Notes) : "") + (result != null ? "\n" + result : ""), 12, sub: true);
        sub.LineBreakMode = LineBreakMode.WordWrap;
        var img = new Image { WidthRequest = 44, HeightRequest = 38, Source = o.Species == 0 ? null : AppState.Sprite(o.Species, (byte)Math.Max(0, o.Form), o.Shiny is 1 or 3 or 4) };
        var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 8 };
        g.Add(img, 0); g.Add(new VerticalStackLayout { Spacing = 2, Children = { main, sub } }, 1);
        return g;
    }

    private async Task Make()
    {
        var list = Parse();
        if (list.Count == 0) { status.Text = "입력한 내용이 없습니다"; return; }
        var noShiny = QuickGen.NoShinySpecified(list);   // 배포는 카드가 이로치 여부를 정함
        if (noShiny.Count > 0)
        {
            var names = string.Join(", ", noShiny.Take(5).Select(x => GameInfo.Strings.Species[x.Species])) + (noShiny.Count > 5 ? " 외" : "");
            var pick = await Application.Current.Windows[0].Page.DisplayActionSheetAsync($"이로치 여부를 적지 않은 포켓몬 {noShiny.Count}마리 ({names})", "취소", null, "이로치로 만들기", "일반 색으로 만들기");
            if (pick == null || pick == "취소") return;
            foreach (var x in noShiny) x.Shiny = pick.StartsWith("이로치") ? 1 : 2;
        }
        cts = new CancellationTokenSource(); var tok = cts.Token;
        makeBtn.IsEnabled = parseBtn.IsEnabled = false; stopBtn.IsVisible = true; preview.Children.Clear();
        var made = new List<PKM>(); var sav = AppState.Sav; int total = list.Sum(x => x.Species == 0 ? 1 : x.Count), done = 0;
        try
        {
            foreach (var o in list)
            {
                if (tok.IsCancellationRequested) break;
                if (o.Species == 0) { preview.Children.Add(Line(o, "포켓몬 이름을 알아보지 못했습니다", false)); done++; continue; }
                for (int k = 0; k < o.Count && !tok.IsCancellationRequested; k++)
                {
                    status.Text = $"만드는 중… {done + 1}/{total} · {GameInfo.Strings.Species[o.Species]}";
                    var (pk, how) = await Task.Run(() => QuickGen.Make(o, sav, tok));
                    done++;
                    bool ok = pk != null && AppState.IsLegal(pk);
                    if (pk != null) made.Add(pk);
                    preview.Children.Add(Line(o, pk == null ? how : $"{how} · {(ok ? "합법" : "불법 — 편집기에서 합법성 표시를 확인하세요")}{QuickGen.Stats(pk)}", pk != null && ok));
                }
            }
        }
        finally { makeBtn.IsEnabled = parseBtn.IsEnabled = true; stopBtn.IsVisible = false; }
        if (made.Count == 0) { status.Text = "만든 포켓몬이 없습니다"; return; }
        if (made.Count == 1)
        {
            status.Text = "편집기로 불러왔습니다";
            Close(); if (AppState.Load(made[0])) { AppState.Edited(); Note.Show("텍스트로 만든 포켓몬을 불러왔습니다"); }
            return;
        }
        int put = QuickGen.PlaceInBoxes(sav, made, AppState.Box);
        AppState.NotifyBox();
        status.Text = $"{made.Count}마리 중 {put}마리를 {AppState.BoxName(AppState.Box)}부터 빈 칸에 넣었습니다" + (put < made.Count ? " (박스가 가득 참)" : "");
    }
}


/// <summary>사용자 줄임말 사전 (한 줄에 "줄임말 = 바꿀 말", 여러 마리는 "/"로).</summary>
public static class UserDict
{
    public static string Text { get => Preferences.Get("quick_alias", ""); set => Preferences.Set("quick_alias", value ?? ""); }
    public static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>();
        foreach (var line in Text.Replace("\r", "").Split('\n'))
        {
            var i = line.IndexOf('='); if (i <= 0) continue;
            var k = line[..i].Trim(); var v = line[(i + 1)..].Trim();
            if (k.Length > 0 && v.Length > 0) d[k] = v;
        }
        return d;
    }
}

public class UserDictSheet : Sheet
{
    public UserDictSheet() : base("내 사전 (줄임말·별칭)")
    {
        var ed = new Editor { Text = UserDict.Text, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 160, FontSize = 15, Placeholder = "한 줄에 하나씩\n포니 = 포푸니라\n럽볼 = 러브러브볼\n전설쌍 = 칠색조 / 루기아" };
        ed.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
        var frame = new Border { Content = ed, StrokeThickness = 1, Padding = 6, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
        frame.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D1D5DB"), Color.FromArgb("#374151"));
        var save = T.Pill("저장", primary: true); save.Clicked += (_, _) => { UserDict.Text = ed.Text; Note.Show($"사전 {UserDict.Load().Count}개를 저장했습니다"); Close(); };
        var hint = T.L("'줄임말 = 바꿀 말' 형식으로 한 줄에 하나씩 적습니다. 바꿀 말에 '/'를 넣으면 여러 마리로 나뉩니다. 내 사전이 기본 사전보다 먼저 적용됩니다.", 12, sub: true); hint.LineBreakMode = LineBreakMode.WordWrap;
        var builtIn = T.L("기본 사전: " + string.Join(", ", QuickParse.BuiltInAlias.Select(kv => $"{kv.Key}→{kv.Value}")), 11, sub: true); builtIn.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, frame, save } } });
    }
}
