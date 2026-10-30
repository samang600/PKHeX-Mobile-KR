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
    /// <summary>다른 화면에서 주문 문장을 넣어 줄 때 (도감 진행의 "빠진 종 빠른 생성" 등).</summary>
    public static void Preset(string text) { lastText = text ?? ""; }
    // 만난 날짜 (달력으로 고름, 시트를 닫아도 유지): 0 지정 안 함, 1 그 날, 2 구간
    private static int metMode;
    private static DateTime metFrom = DateTime.Today, metTo = DateTime.Today;

    public QuickSheet() : base("텍스트로 빠른 생성")
    {
        input.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
        if (AppState.LangCode != "ko") input.Placeholder = Lexicon.Current.Hint;   // 영어·일본어: 띄어쓰기 안내 등
        input.Text = lastText;
        status.LineBreakMode = LineBreakMode.WordWrap;
        parseBtn.Clicked += (_, _) => ShowParse();
        makeBtn.Clicked += async (_, _) => await Make();
        stopBtn.Clicked += (_, _) => cts?.Cancel(); stopBtn.IsVisible = false;
        var dictBtn = T.Pill("내 사전"); dictBtn.Clicked += (_, _) => SheetHost.Show(new UserDictSheet());
        var histBtn = T.Pill("기록"); histBtn.Clicked += (_, _) => SheetHost.Show(new QuickHistorySheet(t => { input.Text = t; lastText = t; preview.Children.Clear(); status.Text = "기록에서 불러왔습니다"; }));
        var clearBtn = T.Pill("지우기"); clearBtn.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text)) return;
            if (!await Application.Current.Windows[0].Page.DisplayAlertAsync("지우기", "입력한 내용을 모두 지울까요?", "지우기", "취소")) return;
            input.Text = ""; lastText = ""; preview.Children.Clear(); status.Text = "";
        };
        var frame = new Border { Content = input, StrokeThickness = 1, Padding = 6, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
        frame.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D1D5DB"), Color.FromArgb("#374151"));
        // 만난 날짜: 달력에서 고르기 (적힌 모든 포켓몬에 적용)
        static DatePicker Day(DateTime v) { var d = new DatePicker { Format = "yyyy-MM-dd", FontSize = 15, HeightRequest = 44, MinimumDate = new DateTime(1996, 1, 1), MaximumDate = DateTime.Today.AddYears(1), Date = v }; d.SetAppThemeColor(DatePicker.TextColorProperty, Color.FromArgb("#111827"), Color.FromArgb("#F3F4F6")); return d; }
        var fromP = Day(metFrom); var toP = Day(metTo);
        var toField = T.Field("마지막 날", toP);
        var fromField = T.Field("날짜", fromP);
        var dates = T.Cols(2, 8); dates.Add(fromField, 0); dates.Add(toField, 1);
        var metHint = T.L("", 12, sub: true); metHint.LineBreakMode = LineBreakMode.WordWrap;
        void MetUi()
        {
            dates.IsVisible = metMode > 0; toField.IsVisible = metMode == 2;
            ((Label)((Layout)fromField).Children[0]).Text = metMode == 2 ? "처음 날" : "날짜";
            metHint.Text = metMode switch
            {
                1 => $"모든 포켓몬의 만난 날짜를 {metFrom:yyyy-MM-dd}로 (합법일 때만)",
                2 => $"{metFrom:yyyy-MM-dd}~{metTo:yyyy-MM-dd} 사이에서 합법인 날짜로",
                _ => "만난 날짜는 게임에서 자연스럽게 나오는 값(오늘·배포 기간 등)으로",
            };
        }
        var metChips = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) } };
        var chipBtns = new List<Button>();
        void ChipStyle() { for (int j = 0; j < chipBtns.Count; j++) { var b = chipBtns[j]; if (j == metMode) { b.BackgroundColor = T.Accent; b.TextColor = Colors.White; } else { b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); b.SetAppThemeColor(Button.TextColorProperty, T.Accent, Color.FromArgb("#A5B4FC")); } } }
        string[] chipNames = ["지정 안 함", "그 날", "구간"];
        for (int i = 0; i < 3; i++)
        {
            int k = i; var b = T.Pill(chipNames[i], size: 13); b.HorizontalOptions = LayoutOptions.Fill;
            b.Clicked += (_, _) => { metMode = k; ChipStyle(); MetUi(); }; chipBtns.Add(b); metChips.Add(b, i);
        }
        fromP.DateSelected += (_, e) => { metFrom = e.NewDate ?? metFrom; if (metTo < metFrom) { metTo = metFrom; toP.Date = metTo; } MetUi(); };
        toP.DateSelected += (_, e) => { metTo = e.NewDate ?? metTo; if (metTo < metFrom) { metFrom = metTo; fromP.Date = metFrom; } MetUi(); };
        ChipStyle(); MetUi();
        var metCard = T.Card(new VerticalStackLayout { Spacing = 8, Children = { T.L("만난 날짜", 13, sub: true), metChips, dates, metHint } }, 10);
        var hint = T.L("한 줄에 한 마리 · 순서 상관없이\n예) 빠르모트 암컷 이로치 럽볼 가장작게 6V Lv50 x3\n• 증표·리본 여러 개 가능 · '어버이: 이름 TID' 줄은 아래 전체에 · 범위: 리빙덱스·도감·전국도감 (1~151) 이로치 프볼 · 특훈\n• 한 마리 → 편집기, 여러 마리 → 현재 박스 빈 칸", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, frame, metCard, T.Btns(2, parseBtn, makeBtn).Also(g => { stopBtn.FontSize = 12.5; stopBtn.Padding = new Thickness(6, 2); stopBtn.HorizontalOptions = LayoutOptions.Fill; g.Add(stopBtn, 0, 0); }), T.Btns(3, histBtn, dictBtn, clearBtn), status, preview } } });
    }

    private List<OrderSpec> Parse()
    {
        lastText = input.Text ?? "";
        var list = QuickParse.Parse(lastText, AppState.Sav, UserDict.Load());
        if (metMode > 0)   // 달력에서 고른 만난 날짜 → 모든 포켓몬
            foreach (var o in list) { o.MetFrom = DateOnly.FromDateTime(metFrom); o.MetTo = DateOnly.FromDateTime(metMode == 2 ? metTo : metFrom); }
        return list;
    }

    private void ShowParse()
    {
        var list = Parse(); preview.Children.Clear();
        foreach (var o in list)
        {
            string note = null;
            if (o.Species != 0 && !QuickGen.InGame(o, AppState.Sav))
            {
                var other = QuickGen.OtherGames(o, AppState.Sav, AppState.Games);
                note = other.Count == 0 ? "이 게임에 없는 포켓몬입니다" : $"이 게임에 없는 포켓몬 · 만들 수 있는 게임: {string.Join(", ", other.Take(4).Select(AppState.GameName))}{(other.Count > 4 ? " 등" : "")} (만들기를 누르면 새 인스턴스로 만들지 물어봅니다)";
            }
            if (o.Species != 0 && o.Ball == StrangeBall.BallId && !StrangeBall.IsPossible(AppState.Sav, o.Species, o.Form))
                note = (note == null ? "" : note + "\n") + "스트레인지볼은 이 포켓몬(폼)에 불가능해요 (포켓몬 GO 와일드 에어리어 사파리볼 출신만) · 아래 '스트레인지볼 가능한 포켓몬' 참고";
            preview.Children.Add(Line(o, note));
        }
        if (StrangeHelp(list) is { } sh) preview.Children.Add(sh);
        status.Text = $"{list.Count}마리로 해석했습니다" + (list.Any(x => x.Unknown.Count > 0) ? " · '?' 표시 단어는 알아보지 못해 무시합니다" : "");
    }

    /// <summary>주문에 스트레인지볼이 있으면 '가능한 포켓몬' 버튼 (증표 가능한 포켓몬 보기처럼). 고르면 그 포켓몬 줄을 입력란에 더함</summary>
    private View StrangeHelp(List<OrderSpec> list)
    {
        if (!list.Any(o => o.Ball == StrangeBall.BallId)) return null;
        var sav = AppState.Sav;
        var poss = StrangeBall.Possible(sav);
        var b = T.Pill($"스트레인지볼 가능한 포켓몬 {poss.Select(e => e.Species).Distinct().Count()}종 보기", size: 12);
        b.HorizontalOptions = LayoutOptions.Start;
        b.Clicked += (_, _) =>
        {
            if (poss.Count == 0) { Note.Show("이 게임에서 스트레인지볼이 가능한 포켓몬이 없어요"); return; }
            var s = GameInfo.Strings;
            var items = poss.Select((e, i) => new ComboItem($"{s.Species[e.Species]}{RaidSearchSheet.FormText(e.Species, e.Form)}{(e.Evolved ? " · 진화" : "")} · {e.Events}", i)).ToList();
            SheetHost.Show(new PickerSheet($"스트레인지볼 가능 {items.Count}가지 (GO 와일드 에어리어)", items, -1, c =>
            {
                var e = poss[c.Value];
                var name = s.Species[e.Species] + (e.Form > 0 ? RaidSearchSheet.FormText(e.Species, e.Form).Replace(" (", "(") : "");
                var cur = (input.Text ?? "").TrimEnd();
                input.Text = (cur.Length > 0 ? cur + "\n" : "") + $"{name} 스트레인지볼";
                status.Text = $"'{name} 스트레인지볼' 줄을 더했어요 · 만난 날짜는 이벤트 기간({e.Events})으로 정해져요";
            }, c => AppState.Sprite(poss[c.Value].Species, poss[c.Value].Form, false)));
        };
        return b;
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
        makeBtn.IsEnabled = parseBtn.IsEnabled = false; stopBtn.IsVisible = true; parseBtn.IsVisible = false; preview.Children.Clear();
        var made = new List<PKM>(); var sav = AppState.Sav; var failed = new List<(OrderSpec O, string How)>(); int total = list.Sum(x => x.Species == 0 ? 1 : x.Count), done = 0;
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
                    if (pk == null && k == 0 && (how == QuickGen.NotInGame || how.StartsWith(QuickGen.NoEncounter))) { failed.Add((o, how)); done += o.Count - 1; break; }   // 다른 게임으로 (마릿수는 거기서)
                }
            }
        }
        finally { makeBtn.IsEnabled = parseBtn.IsEnabled = true; stopBtn.IsVisible = false; parseBtn.IsVisible = true; }
        if (StrangeHelp(list) is { } shv) preview.Children.Add(shv);
        QuickHistory.Add(lastText, $"{AppState.GameName(sav.Version)} · {made.Count}/{total}마리{(failed.Count > 0 ? $" · 이 게임에 없음 {failed.Count}" : "")}");
        // 이 게임에서 만들 수 없던 포켓몬 (게임에 없음·이 게임 조우 없음) → 만들 수 있는 게임으로 새 인스턴스
        var away = failed.Where(x => !tok.IsCancellationRequested && QuickGen.OtherGames(x.O, sav, AppState.Games).Count > 0).ToList();
        var plan = away.Count > 0 ? await AskOtherGame(away.Select(x => x.O).Distinct().ToList(), sav) : null;
        string here = "";
        if (made.Count == 1 && plan == null)
        {
            status.Text = "편집기로 불러왔습니다";
            Close(); if (AppState.Load(made[0])) { AppState.Edited(); Note.Show("텍스트로 만든 포켓몬을 불러왔습니다"); }
            return;
        }
        if (made.Count == 1) { if (AppState.Load(made[0])) AppState.Edited(); here = $"{GameInfo.Strings.Species[made[0].Species]}을(를) 편집기로"; }
        else if (made.Count > 1)
        {
            int put = QuickGen.PlaceInBoxes(sav, made, AppState.Box);
            AppState.NotifyBox();
            here = $"{made.Count}마리 중 {put}마리를 {AppState.BoxName(AppState.Box)}부터 빈 칸에 넣었습니다" + (put < made.Count ? " (박스가 가득 참)" : "");
        }
        if (plan == null) { status.Text = made.Count == 0 ? "만든 포켓몬이 없습니다" : here; return; }
        var report = new List<string>(); if (here.Length > 0) report.Add($"{AppState.GameName(sav.Version)}: {here}");
        cts = new CancellationTokenSource(); tok = cts.Token;
        makeBtn.IsEnabled = parseBtn.IsEnabled = false; stopBtn.IsVisible = true; parseBtn.IsVisible = false;
        try
        {
            foreach (var (game, orders) in plan)
            {
                if (tok.IsCancellationRequested) break;
                // 같은 게임 인스턴스가 열려 있으면 그것을, 없으면 빈 세이브로 새 인스턴스
                AppState.SaveActive();
                int open = AppState.Instances.FindIndex(i => i.Sav.Version == game);
                if (open >= 0) AppState.SwitchTo(open); else AppState.AddInstance(AppState.NewBlankSave(game));
                var s2 = AppState.Sav; var made2 = new List<PKM>();
                foreach (var o in orders)
                    for (int k = 0; k < o.Count && !tok.IsCancellationRequested; k++)
                    {
                        status.Text = $"{AppState.GameName(game)} 인스턴스에서 만드는 중… {GameInfo.Strings.Species[o.Species]}";
                        var (pk, how) = await Task.Run(() => QuickGen.Make(o, s2, tok));
                        bool ok = pk != null && AppState.IsLegal(pk);
                        if (pk != null) made2.Add(pk);
                        preview.Children.Add(Line(o, $"[{AppState.GameName(game)}] " + (pk == null ? how : $"{how} · {(ok ? "합법" : "불법 — 편집기에서 합법성 표시를 확인하세요")}{QuickGen.Stats(pk)}"), pk != null && ok));
                    }
                if (made2.Count == 1) { if (AppState.Load(made2[0])) AppState.Edited(); report.Add($"{AppState.GameName(game)}: {GameInfo.Strings.Species[made2[0].Species]}을(를) 편집기로"); }
                else if (made2.Count > 1) { int put2 = QuickGen.PlaceInBoxes(s2, made2, AppState.Box); AppState.NotifyBox(); report.Add($"{AppState.GameName(game)}: {put2}마리를 {AppState.BoxName(AppState.Box)}부터"); }
                else report.Add($"{AppState.GameName(game)}: 만들지 못했습니다");
            }
        }
        finally { makeBtn.IsEnabled = parseBtn.IsEnabled = true; stopBtn.IsVisible = false; parseBtn.IsVisible = true; }
        status.Text = string.Join("\n", report) + "\n(인스턴스는 메뉴 → 인스턴스에서 오갈 수 있어요)";
        Note.Show($"{AppState.GameName(AppState.Sav.Version)} 인스턴스로 전환했습니다");
    }

    /// <summary>만들 수 없던 포켓몬을 어느 게임 인스턴스에서 만들지 물어봄. 반환: 게임 → 주문 (null = 만들지 않음).</summary>
    private static async Task<List<(GameVersion, List<OrderSpec>)>> AskOtherGame(List<OrderSpec> orders, SaveFile sav)
    {
        var page = Application.Current.Windows[0].Page;
        var names = string.Join(", ", orders.Take(5).Select(x => GameInfo.Strings.Species[x.Species])) + (orders.Count > 5 ? " 외" : "");
        var per = orders.ToDictionary(o => o, o => QuickGen.OtherGames(o, sav, AppState.Games));
        // 모두 만들 수 있는 게임 (최신 순, 열려 있는 인스턴스의 게임을 먼저)
        var common = AppState.Games.Where(v => per.Values.All(l => l.Contains(v))).ToList();
        var openGames = AppState.Instances.Where(i => i?.Sav != null && i.Sav != sav).Select(i => i.Sav.Version).ToHashSet();
        common = common.OrderBy(v => openGames.Contains(v) ? 0 : 1).ThenBy(v => Array.IndexOf(AppState.Games, v)).Take(6).ToList();
        string Label(GameVersion v) => AppState.GameName(v) + (openGames.Contains(v) ? " (열린 인스턴스)" : " (새 인스턴스)");
        const string each = "포켓몬마다 만들 수 있는 최신 게임으로";
        var opts = common.Select(Label).ToList();
        if (common.Count == 0 || orders.Count > 1) opts.Add(each);
        var pick = await page.DisplayActionSheetAsync($"{AppState.GameName(sav.Version)}에서 만들 수 없는 포켓몬 {orders.Count}마리 ({names})\n만들 수 있는 게임으로 새 인스턴스를 열어 만들까요?", "만들지 않기", null, opts.ToArray());
        if (pick == null || pick == "만들지 않기") return null;
        if (pick == each)
            return orders.GroupBy(o => per[o].OrderBy(v => openGames.Contains(v) ? 0 : 1).ThenBy(v => Array.IndexOf(AppState.Games, v)).First())
                .Select(g => (g.Key, g.ToList())).ToList();
        int i = opts.IndexOf(pick);
        return i >= 0 && i < common.Count ? [(common[i], orders)] : null;
    }
}


/// <summary>빠른 생성 기록: 최근 20개 주문 문장과 결과 요약 (다시 쓰기).</summary>
public static class QuickHistory
{
    public record Item(string Text, string When, string Result);
    private static string Path_ => Path.Combine(FileSystem.AppDataDirectory, "quick_history.json");
    public static List<Item> Load()
    {
        try { if (File.Exists(Path_)) return System.Text.Json.JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(Path_)) ?? []; } catch { }
        return [];
    }
    public static void Add(string text, string result)
    {
        try
        {
            var t = (text ?? "").Trim(); if (t.Length == 0) return;
            var list = Load(); list.RemoveAll(x => x.Text == t);
            list.Insert(0, new Item(t, DateTime.Now.ToString("MM-dd HH:mm"), result ?? ""));
            if (list.Count > 20) list.RemoveRange(20, list.Count - 20);
            File.WriteAllText(Path_, System.Text.Json.JsonSerializer.Serialize(list));
        }
        catch { }
    }
    public static void Remove(string text) { try { var list = Load(); list.RemoveAll(x => x.Text == text); File.WriteAllText(Path_, System.Text.Json.JsonSerializer.Serialize(list)); } catch { } }
}

public class QuickHistorySheet : Sheet
{
    public QuickHistorySheet(Action<string> pick) : base("빠른 생성 기록 (최근 20개)", 0.8)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        var items = QuickHistory.Load();
        if (items.Count == 0) stack.Children.Add(T.L("아직 기록이 없습니다", 13, sub: true));
        foreach (var it in items)
        {
            var text = T.L(it.Text.Length > 160 ? it.Text[..160] + "…" : it.Text, 13, bold: true); text.LineBreakMode = LineBreakMode.WordWrap;
            var sub = T.L($"{it.When} · {it.Result}", 12, sub: true); sub.LineBreakMode = LineBreakMode.WordWrap;
            var use = T.Pill("다시 쓰기", primary: true, size: 12); use.Clicked += (_, _) => { Close(); pick(it.Text); };
            var del = T.Pill("지우기", size: 12); del.Clicked += (_, _) => { QuickHistory.Remove(it.Text); Close(); SheetHost.Show(new QuickHistorySheet(pick)); };
            stack.Children.Add(T.Card(new VerticalStackLayout { Spacing = 4, Children = { text, sub, new HorizontalStackLayout { Spacing = 6, Children = { use, del } } } }, 10));
        }
        Body.Add(new ScrollView { Content = stack });
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
        var hint = T.L("한 줄에 '줄임말 = 바꿀 말' · '/'는 여러 마리 · 기본 사전보다 먼저 적용", 12, sub: true); hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, frame, save } } });
    }
}
