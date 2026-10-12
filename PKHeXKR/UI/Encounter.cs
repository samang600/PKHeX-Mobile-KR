using PKHeX.Core;
using System.Runtime.CompilerServices;
namespace PKHeXKR;

public class EncounterSheet : Sheet
{
    private ushort species;
    private static GameVersion onlyGame = 0;   // 0 = 모든 게임
    private static string onlyKind;            // null = 모든 유형
    private static List<string> lastKinds = [];
    static EncounterSheet() { AppState.SaveChanged += () => { onlyGame = 0; onlyKind = null; }; }   // 게임을 바꾸면 게임·유형 선택 초기화
    private Button kindBtn;
    private readonly Label spLabel;
    private readonly CheckBox eggOnly = new() { Color = T.Accent }, shinyOnly = new() { Color = T.Accent }, noPreEvo = new() { Color = T.Accent };
    private readonly CollectionView results = new() { SelectionMode = SelectionMode.None };
    private readonly Label status = T.L("포켓몬을 고르고 검색하세요", 13, sub: true), condSummary = T.L("", 12);
    private readonly ActivityIndicator busy = new() { Color = T.Accent, IsVisible = false, HeightRequest = 22, WidthRequest = 22 };
    private readonly Button tabSearch = T.Pill("검색", primary: true), tabCond = T.Pill("조건");
    private readonly View searchPane, condPane;

    public EncounterSheet() : base("인카운터 검색")
    {
        species = AppState.Pk.Species;
        (var sv, spLabel) = T.Chooser(() => SheetHost.Show(new PickerSheet("포켓몬", AppState.Src.Species, species, c => { species = (ushort)c.Value; spLabel.Text = c.Text; _ = Run(); }, c => AppState.Sprite((ushort)c.Value, 0, false))));
        spLabel.Text = species == 0 ? "포켓몬 선택" : GameInfo.Strings.Species[species];
        var go = T.Pill("검색", primary: true); go.Clicked += async (_, _) => await Run();
        var top = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
        top.Add(sv, 0); top.Add(go, 1);
        var gameBtn = T.Pill(onlyGame == 0 ? "게임: 전체" : "게임: " + AppState.GameName(onlyGame));
        gameBtn.Clicked += (_, _) =>
        {
            var sav = AppState.Sav; var b0 = sav.BlankPKM;
            var list = new List<ComboItem> { new("전체 게임", 0) };
            list.AddRange(GameUtil.GetVersionsWithinRange(b0, b0.Context).Distinct().Where(v => v.IsValidSavedVersion()).Select(v => new ComboItem(AppState.GameName(v), (int)v)));
            SheetHost.Show(new PickerSheet("등장 게임", list, (int)onlyGame, c => { onlyGame = (GameVersion)c.Value; gameBtn.Text = c.Value == 0 ? "게임: 전체" : "게임: " + c.Text; _ = Run(); }));
        };
        var optsRow = new HorizontalStackLayout { Spacing = 2, Children = { eggOnly, T.L("알만", 14), new BoxView { WidthRequest = 12, Color = Colors.Transparent }, shinyOnly, T.L("이로치 조우만", 14), new BoxView { WidthRequest = 12, Color = Colors.Transparent }, new BoxView { WidthRequest = 12, Color = Colors.Transparent }, noPreEvo, T.L("미진화체 제외", 14), new BoxView { WidthRequest = 12, Color = Colors.Transparent }, busy } };
        var opts = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = optsRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Never };
        condSummary.TextColor = T.Accent;
        results.ItemTemplate = new DataTemplate(() =>
        {
            var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10, Padding = new Thickness(4, 6) };
            var img = new Image { WidthRequest = 52, HeightRequest = 44 }; img.SetBinding(Image.SourceProperty, "Sprite");
            var aIco = new Image { Source = "ov_alpha.png", WidthRequest = 16, HeightRequest = 16, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.End }; aIco.SetBinding(IsVisibleProperty, "Alpha");
            var imgBox = new Grid { Children = { img, aIco } };
            var t1 = T.L("", 15, bold: true); t1.SetBinding(Label.TextProperty, "Title");
            var t2 = T.L("", 12, sub: true); t2.SetBinding(Label.TextProperty, "Detail"); t2.LineBreakMode = LineBreakMode.WordWrap;
            var t3 = T.L("", 11, sub: true); t3.SetBinding(Label.TextProperty, "Info"); t3.SetBinding(IsVisibleProperty, "HasInfo"); t3.LineBreakMode = LineBreakMode.WordWrap;
            var ball = new Image { WidthRequest = 22, HeightRequest = 22 }; ball.SetBinding(Image.SourceProperty, "Ball");
            g.Add(imgBox, 0); g.Add(new VerticalStackLayout { Spacing = 1, Children = { t1, t2, t3 }, VerticalOptions = LayoutOptions.Center }, 1); g.Add(ball, 2);
            var tap = new TapGestureRecognizer(); tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, ".");
            tap.Tapped += async (_, e) => { if (e.Parameter is Row r) await Choose(r); };
            g.GestureRecognizers.Add(tap);
            return g;
        });
        stopBtn.Clicked += (_, _) => cts?.Cancel(); stopBtn.IsVisible = false;
        markHelp.IsVisible = false; status.LineBreakMode = LineBreakMode.WordWrap;
        markHelp.Clicked += (_, _) =>
        {
            var sp = EncCond.MarkSpecies();
            if (sp.Count == 0) { Note.Show("이 증표는 미리 알 수 있는 조우 목록이 없습니다 (만든 뒤 합법성 검사로 확인)"); return; }
            var items = sp.Select(x => new ComboItem(GameInfo.Strings.Species[x], x)).ToList();
            SheetHost.Show(new PickerSheet($"{EncCond.MarkName(EncCond.Mark)} 가능 포켓몬 {sp.Count}종", items, species, c => { species = (ushort)c.Value; spLabel.Text = c.Text; _ = Run(); }, c => AppState.Sprite((ushort)c.Value, 0, false)));
        };
        var stRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 6 }; stRow.Add(status, 0); stRow.Add(stopBtn, 1); stRow.Add(markHelp, 2);
        var sp = new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) }, RowSpacing = 8 };
        sp.Add(top, 0, 0); kindBtn = T.Pill(onlyKind == null ? "유형: 전체" : "유형: " + onlyKind);
        kindBtn.Clicked += (_, _) =>
        {
            var list = new List<ComboItem> { new("모든 유형", 0) }; list.AddRange(lastKinds.Select((k, i) => new ComboItem(k, i + 1)));
            SheetHost.Show(new PickerSheet("조우 유형", list, onlyKind == null ? 0 : lastKinds.IndexOf(onlyKind) + 1, c => { onlyKind = c.Value == 0 ? null : lastKinds[c.Value - 1]; kindBtn.Text = onlyKind == null ? "유형: 전체" : "유형: " + onlyKind; _ = Run(); }));
        };
        sp.Add(new VerticalStackLayout { Spacing = 6, Children = { gameBtn, kindBtn, opts } }, 0, 1); sp.Add(condSummary, 0, 2); sp.Add(stRow, 0, 3); sp.Add(results, 0, 4);
        searchPane = sp;
        condPane = BuildCond();
        condPane.IsVisible = false;
        tabSearch.Clicked += (_, _) => ShowTab(false); tabCond.Clicked += (_, _) => ShowTab(true);
        Body.RowDefinitions.Add(new(GridLength.Auto)); Body.RowDefinitions.Add(new(GridLength.Star));
        Body.Add(new HorizontalStackLayout { Spacing = 8, Children = { tabSearch, tabCond } }, 0, 0);
        var host = new Grid(); host.Add(searchPane); host.Add(condPane);
        Body.Add(host, 0, 1);
        UpdateSummary();
        if (species != 0) _ = Run();
    }

    private void ShowTab(bool cond)
    {
        searchPane.IsVisible = !cond; condPane.IsVisible = cond;
        Style(tabSearch, !cond); Style(tabCond, cond);
        UpdateSummary();
    }
    private static void Style(Button b, bool on)
    {
        if (on) { b.BackgroundColor = T.Accent; b.TextColor = Colors.White; b.BorderWidth = 0; }
        else { b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); b.SetAppThemeColor(Button.TextColorProperty, T.Accent, Color.FromArgb("#A5B4FC")); b.BorderWidth = 1; }
    }

    private View BuildCond()
    {
        var s = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(0, 4, 0, 20) };
        s.Children.Add(Choice("성별", ["무관", "♂ 수컷", "♀ 암컷"], () => EncCond.Gender + 1, v => EncCond.Gender = v - 1));
        s.Children.Add(Choice("이로치", ["무관", "이로치", "일반", "별", "네모"], () => EncCond.ShinyMode, v => EncCond.ShinyMode = v));
        s.Children.Add(Choice("특성", ["무관", "특성 1", "특성 2", "숨겨진 특성"], () => EncCond.Ability + 1, v => EncCond.Ability = v - 1));
        (var nv, var nl) = T.Chooser(() => { });
        nl.Text = EncCond.Nature < 0 ? "무관" : GameInfo.Strings.natures[EncCond.Nature];
        var natures = new List<ComboItem> { new("무관", -1) }; natures.AddRange(AppState.NatureItems());
        var tapN = new TapGestureRecognizer(); tapN.Tapped += (_, _) => SheetHost.Show(new PickerSheet("성격", natures, EncCond.Nature, c => { EncCond.Nature = c.Value; nl.Text = c.Text; UpdateSummary(); }));
        nv.GestureRecognizers.Clear(); nv.GestureRecognizers.Add(tapN);
        s.Children.Add(T.Field("성격", nv));
        // 개체값 최솟값 (31이면 V)
        var ivGrid = T.Cols(6, 4); string[] ivn = ["H", "A", "B", "C", "D", "S"]; var ivE = new Entry[6];
        for (int i = 0; i < 6; i++)
        {
            int k = i; ivE[i] = T.Input(Keyboard.Numeric); ivE[i].Text = EncCond.IVMin[i].ToString(); ivE[i].MaxLength = 2; ivE[i].HorizontalTextAlignment = TextAlignment.Center;
            if (EncCond.IVExact && EncCond.IVMin[i] < 0) ivE[i].Text = "";
            ivE[i].TextChanged += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.NewTextValue)) { EncCond.IVMin[k] = EncCond.IVExact ? -1 : 0; UpdateSummary(); return; }
                int.TryParse(e.NewTextValue, out var v); if (v > 31) { ivE[k].Text = "31"; return; } EncCond.IVMin[k] = v; UpdateSummary();
            };
            ivGrid.Add(T.Field(ivn[i], ivE[i]), i);
        }
        var v6 = T.Pill("6V", size: 12); v6.Clicked += (_, _) => { foreach (var e in ivE) e.Text = "31"; };
        var v0 = T.Pill("비우기", size: 12); v0.Clicked += (_, _) => { foreach (var e in ivE) e.Text = EncCond.IVExact ? "" : "0"; };
        s.Children.Add(Choice("개체값 조건", ["이상 (최솟값)", "정확히 (빈 칸 = 무관)"], () => EncCond.IVExact ? 1 : 0, v =>
        {
            EncCond.IVExact = v == 1;
            for (int i = 0; i < 6; i++) { if (EncCond.IVExact) { EncCond.IVMin[i] = -1; ivE[i].Text = ""; } else { EncCond.IVMin[i] = 0; ivE[i].Text = "0"; } }
        }));
        s.Children.Add(T.Field("개체값 (31이면 V, 1·2세대는 15까지)", ivGrid));
        s.Children.Add(new HorizontalStackLayout { Spacing = 6, Children = { v6, v0 } });

        // 시드 탐색 방식
        s.Children.Add(Choice("시드 탐색", ["무작위", "처음부터(지정 시드부터)"], () => EncCond.SeedMode, v => EncCond.SeedMode = v));
        var seedE = T.Input(placeholder: "0"); seedE.MaxLength = 16; seedE.Text = EncCond.StartSeed.ToString("X");
        seedE.TextChanged += (_, e) => { if (ulong.TryParse((e.NewTextValue ?? "").Trim(), System.Globalization.NumberStyles.HexNumber, null, out var v)) EncCond.StartSeed = v; else if (string.IsNullOrWhiteSpace(e.NewTextValue)) EncCond.StartSeed = 0; };
        Button SB(string t, Action a) { var b = T.Pill(t, size: 11); b.Padding = new Thickness(6, 0); b.Clicked += (_, _) => a(); return b; }
        var seedRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 4 };
        seedRow.Add(seedE, 0); seedRow.Add(SB("0", () => seedE.Text = "0"), 1); seedRow.Add(SB("무작위", () => seedE.Text = ((ulong)Random.Shared.NextInt64()).ToString("X")), 2); seedRow.Add(SB("이어서", () => seedE.Text = (EncCond.LastSeed + 1).ToString("X")), 3);
        s.Children.Add(T.Field("시작 시드 (16진수)", seedRow));
        var endE = T.Input(placeholder: "비우면 끝 없음"); endE.MaxLength = 16; endE.Text = EncCond.EndSeed == 0 ? "" : EncCond.EndSeed.ToString("X");
        endE.TextChanged += (_, e) => { EncCond.EndSeed = ulong.TryParse((e.NewTextValue ?? "").Trim(), System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0; };
        s.Children.Add(T.Field("끝 시드 (16진수, 이 시드까지만 탐색)", endE));
        s.Children.Add(Choice("결과", ["첫 개체", "여러 개", "전체(32비트)"], () => EncCond.ResultMode, v => EncCond.ResultMode = v));
        var maxE = T.Input(Keyboard.Numeric); maxE.Text = EncCond.MaxResults.ToString(); maxE.MaxLength = 3;
        maxE.TextChanged += (_, e) => { if (int.TryParse(e.NewTextValue, out var v)) EncCond.MaxResults = Math.Clamp(v, 1, 500); };
        s.Children.Add(T.Field("여러 개일 때 최대 개수 (1~500)", maxE));
        var seedHint = T.L("'처음부터'는 시드로 개체가 정해지는 조우(Z-A, SV 테라 레이드, 소드실드 야생·맥스 레이드, PLA, BDSP 고정, 3·4세대 PID 연동 조우)에서 시드를 순서대로 계산합니다. 그 외 조우는 자동으로 무작위 탐색을 합니다. '전체'는 시드가 32비트인 조우(테라 레이드, 소드실드 야생, BDSP 고정, 3·4세대)에서 끝까지 훑어 조건을 만족하는 개체가 없는지까지 확정합니다(수십 분 걸릴 수 있음).", 12, sub: true);
        seedHint.LineBreakMode = LineBreakMode.WordWrap; s.Children.Add(seedHint);
        if (AppState.Sav is SAV9ZA)   // ZA 시드 파인더에서 옮겨 온 옵션
        {
            View Sw(string label, bool v, Action<bool> set) { var w = new Switch { IsToggled = v, OnColor = T.Accent }; w.Toggled += (_, e) => { set(e.Value); UpdateSummary(); }; var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, HeightRequest = 44 }; g.Add(T.L(label, 15), 0); g.Add(w, 1); return g; }
            s.Children.Add(T.L("Z-A 야생 이로치 확률 (시드 '처음부터' 탐색에 반영)", 12, sub: true));
            s.Children.Add(Sw("빛나는부적", EncCond.ZACharm, v => EncCond.ZACharm = v));
            s.Children.Add(Sw("이차원 이로치 파워", EncCond.ZAPower, v => EncCond.ZAPower = v));
        }
        s.Children.Add(T.L("크기 (8세대 이상: 키·몸무게, 9세대·ZA: 배율도 적용) · 0~255", 12, sub: true));
        s.Children.Add(Range("키", () => (EncCond.HMin, EncCond.HMax), (a, b) => { EncCond.HMin = a; EncCond.HMax = b; }));
        s.Children.Add(Range("몸무게", () => (EncCond.WMin, EncCond.WMax), (a, b) => { EncCond.WMin = a; EncCond.WMax = b; }));
        s.Children.Add(Range("배율(Scale)", () => (EncCond.SMin, EncCond.SMax), (a, b) => { EncCond.SMin = a; EncCond.SMax = b; }));
        (var mkv, var mkl) = T.Chooser(() => { });
        mkl.Text = EncCond.MarkName(EncCond.Mark);
        var mkTap = new TapGestureRecognizer();
        mkTap.Tapped += (_, _) => SheetHost.Show(new PickerSheet("증표", EncCond.MarkItems(), EncCond.Mark, c => { EncCond.Mark = c.Value; mkl.Text = c.Text; UpdateSummary(); if (species != 0) _ = Run(); }, c => c.Value < 0 ? null : ("ribbon" + ((RibbonIndex)c.Value).ToString()).ToLowerInvariant() + ".png"));
        mkv.GestureRecognizers.Clear(); mkv.GestureRecognizers.Add(mkTap);
        s.Children.Add(T.Field("증표 (고르면 가능한 조우만 보이고, 만들 때 증표를 붙임)", mkv));
        var un = new Switch { IsToggled = EncCond.Unlimited, OnColor = T.Accent };
        un.Toggled += (_, e) => { EncCond.Unlimited = e.Value; Preferences.Set("enc_unlimited", e.Value); UpdateSummary(); };
        var unRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        var unLab = T.L("찾을 때까지 계속 탐색 (중지 가능)", 15); unLab.LineBreakMode = LineBreakMode.WordWrap;
        unRow.Add(unLab, 0); unRow.Add(un, 1);
        var unHint = T.L("끄면 12초까지만 찾습니다. 켜면 조건을 만족하는 개체가 나올 때까지 계속 찾고, 탐색 중에는 '중지'로 멈추거나 가장 가까운 개체를 불러올 수 있습니다. 조우에서 불가능한 조건(이로치 잠금, 고정 성격 등)이면 시작 전에 알려줍니다.", 12, sub: true);
        unHint.LineBreakMode = LineBreakMode.WordWrap;
        s.Children.Add(unRow); s.Children.Add(unHint);
        var goSearch = T.Pill("검색", primary: true); goSearch.HorizontalOptions = LayoutOptions.Fill;
        goSearch.Clicked += async (_, _) => { ShowTab(false); await Run(); };
        var reset = T.Pill("조건 초기화"); reset.HorizontalOptions = LayoutOptions.Fill;
        reset.BackgroundColor = T.Bad; reset.TextColor = Colors.White; reset.BorderWidth = 0;
        reset.Clicked += (_, _) => { EncCond.Gender = -1; EncCond.ShinyMode = 0; EncCond.Nature = -1; EncCond.Ability = -1; Array.Clear(EncCond.IVMin); EncCond.IVExact = false; EncCond.EndSeed = 0; EncCond.SeedMode = 0; EncCond.ResultMode = 0; EncCond.ZACharm = EncCond.ZAPower = false; EncCond.Mark = -1; EncCond.HMin = EncCond.WMin = EncCond.SMin = 0; EncCond.HMax = EncCond.WMax = EncCond.SMax = 255; Close(); SheetHost.Show(new EncounterSheet()); };
        s.Children.Add(T.L("조건은 결과를 눌러 개체를 만들 때 적용됩니다. 조건을 만족하는 합법 개체가 나올 때까지 다시 생성합니다.", 12, sub: true));
        s.Children.Add(goSearch);
        s.Children.Add(reset);
        return new ScrollView { Content = s };
    }

    private View Choice(string label, string[] names, Func<int> get, Action<int> set)
    {
        var flex = T.Cols(names.Length, 4);   // 한 줄에 같은 폭으로
        var btns = new List<Button>();
        for (int i = 0; i < names.Length; i++)
        {
            int k = i; var b = T.Pill(names[i], size: names.Length >= 5 ? 11 : 12); b.Padding = new Thickness(2, 0); b.HorizontalOptions = LayoutOptions.Fill;
            b.Clicked += (_, _) => { set(k); for (int j = 0; j < btns.Count; j++) Style(btns[j], j == k); UpdateSummary(); };
            btns.Add(b); flex.Add(b, i);
        }
        for (int j = 0; j < btns.Count; j++) Style(btns[j], j == get());
        return T.Field(label, flex);
    }

    private View Range(string label, Func<(int, int)> get, Action<int, int> set)
    {
        var (a0, b0) = get();
        var a = T.Input(Keyboard.Numeric); a.Text = a0.ToString(); var b = T.Input(Keyboard.Numeric); b.Text = b0.ToString();
        a.MaxLength = b.MaxLength = 3;
        void Apply()
        {
            int.TryParse(a.Text, out var x); if (!int.TryParse(b.Text, out var y)) y = 255;
            if (x > 255) { a.Text = "255"; return; }   // 큰 수는 255로 바꾸고 다시 적용
            if (y > 255) { b.Text = "255"; return; }
            set(Math.Clamp(x, 0, 255), Math.Clamp(y, 0, 255)); UpdateSummary();
        }
        a.TextChanged += (_, _) => Apply(); b.TextChanged += (_, _) => Apply();
        Button Mini(string t, Action act) { var x = new Button { Text = t, FontSize = 10, Padding = new Thickness(6, 0), HeightRequest = 28, CornerRadius = 8, BorderWidth = 1, BorderColor = T.Accent, TextColor = T.Accent, VerticalOptions = LayoutOptions.Center }; x.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); x.Clicked += (_, _) => act(); return x; }
        var mn = Mini("최소", () => { a.Text = "0"; b.Text = "0"; }); var mx = Mini("최대", () => { a.Text = "255"; b.Text = "255"; });
        var g = new Grid { ColumnDefinitions = { new(new GridLength(1.2, GridUnitType.Star)), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 4 };
        g.Add(T.L(label, 14), 0); g.Add(a, 1); g.Add(T.L("~", 14), 2); g.Add(b, 3); g.Add(mn, 4); g.Add(mx, 5);
        return g;
    }

    private void UpdateSummary()
    {
        var parts = new List<string>();
        if (EncCond.Gender >= 0) parts.Add(EncCond.Gender == 0 ? "♂" : "♀");
        if (EncCond.ShinyMode > 0) parts.Add(new[] { "", "이로치", "일반 색", "별 이로치", "네모 이로치" }[EncCond.ShinyMode]);
        if (EncCond.Nature >= 0) parts.Add(GameInfo.Strings.natures[EncCond.Nature]);
        if (EncCond.Ability >= 0) parts.Add(new[] { "특성1", "특성2", "숨특" }[EncCond.Ability]);
        if (EncCond.AnyIV) parts.Add((EncCond.IVExact ? "IV=" : "IV≥") + string.Join("/", EncCond.IVMin.Select(v => v < 0 ? "-" : v.ToString())));
        if (EncCond.SeedMode == 1 && EncCond.EndSeed != 0) parts.Add($"~{EncCond.EndSeed:X}");
        if (EncCond.Mark >= 0) parts.Add(EncCond.MarkName(EncCond.Mark));
        if (EncCond.ZACharm) parts.Add("빛나는부적"); if (EncCond.ZAPower) parts.Add("이차원 파워");
        if (EncCond.SeedMode == 1) parts.Add($"시드 {EncCond.StartSeed:X}부터 · " + new[] { "첫 개체", $"최대 {EncCond.MaxResults}개", "전체" }[EncCond.ResultMode]);
        if (EncCond.HasSize) parts.Add($"키 {EncCond.HMin}-{EncCond.HMax} 몸무게 {EncCond.WMin}-{EncCond.WMax} 배율 {EncCond.SMin}-{EncCond.SMax}");
        condSummary.Text = parts.Count == 0 ? "" : "적용 조건: " + string.Join(" · ", parts);
        condSummary.IsVisible = parts.Count > 0;
    }

    private readonly Button stopBtn = T.Pill("중지");
    private readonly Button markHelp = T.Pill("가능한 포켓몬 보기", size: 12);
    private CancellationTokenSource cts;
    private bool generating;

    public record Row(IEncounterInfo Enc, string Sprite, string Title, string Detail, string Ball, string Info = "") { public bool HasInfo => Info.Length > 0; public bool Alpha => Enc is IAlphaReadOnly { IsAlpha: true }; }

    private static string Kind(IEncounterInfo e) => e.GetType().Name switch
    {
        "EncounterTera9" => "테라 레이드",
        "EncounterMight9" => "최강 테라 레이드",
        "EncounterDist9" => "배포 테라 레이드",
        "EncounterOutbreak9" => "대량발생",
        "EncounterStatic8N" or "EncounterStatic8NC" or "EncounterStatic8ND" => "맥스 레이드",
        "EncounterStatic8U" => "다이맥스 어드벤처",
        "EncounterStatic8NDist" or "EncounterDist8" => "배포 맥스 레이드",
        _ => KindBase(e),
    };
    private static string KindBase(IEncounterInfo e) => e switch
    {
        IEncounterEgg => "알", MysteryGift => "배포",
        _ when e.GetType().Name.StartsWith("EncounterTrade") => "교환",
        _ when e.GetType().Name.StartsWith("EncounterSlot") => "야생",
        _ when e.GetType().Name.StartsWith("EncounterGift") => "배포",
        _ => "게임 내 입수",
    };

    /// <summary>만나는 장소 이름 (소드실드는 와일드에어리어 표시).</summary>
    private static string Place(IEncounterInfo e)
    {
        try
        {
            if (e is not ILocation l || l.Location == 0) return "";
            var name = GameInfo.GetLocationName(false, l.Location, e.Generation, e.Generation, e.Version);
            if (string.IsNullOrWhiteSpace(name)) return "";
            if (e is EncounterSlot8 && EncounterArea8.IsWildArea(l.Location)) name += " (와일드에어리어)";
            return "  · " + name;
        }
        catch { return ""; }
    }

    private static string SlotInfo(IEncounterInfo e)
    {
        if (e is not EncounterSlot8 s8) return "";
        var t = s8.Type.ToString();
        string type = t.StartsWith("Symbol") ? "보이는 심볼" : t.StartsWith("Hidden") ? "숨은 조우(흔들림·낚시)" : t.StartsWith("Surfing") ? "파도타기" : t.StartsWith("Sky") ? "하늘" : t.StartsWith("Ground") ? "땅" : t.Contains("Fish") ? "낚시" : t;
        var w = s8.Weather;
        string weather = w == AreaWeather8.All ? "모든 날씨" : string.Join("·", new (AreaWeather8, string)[] { (AreaWeather8.Normal, "맑음"), (AreaWeather8.Overcast, "흐림"), (AreaWeather8.Raining, "비"), (AreaWeather8.Thunderstorm, "뇌우"), (AreaWeather8.Intense_Sun, "강한 햇살"), (AreaWeather8.Snowing, "눈"), (AreaWeather8.Snowstorm, "눈보라"), (AreaWeather8.Sandstorm, "모래바람"), (AreaWeather8.Heavy_Fog, "짙은 안개") }.Where(x => (w & x.Item1) != 0).Select(x => x.Item2));
        return $" · {type} · {weather} ({t}/{w})";
    }

    private async Task Run()
    {
        if (species == 0) { status.Text = "포켓몬을 먼저 고르세요"; return; }
        busy.IsVisible = busy.IsRunning = true; status.Text = "검색 중…";
        bool egg = eggOnly.IsChecked, shiny = shinyOnly.IsChecked, final = noPreEvo.IsChecked;
        var sav = AppState.Sav; var spc = species;
        try
        {
            var rows = await Task.Run(() =>
            {
                var list = new List<IEncounterInfo>();
                var blank = sav.BlankPKM;
                var pi = sav.Personal.GetFormEntry(spc, 0);
                var versions = onlyGame != 0 ? [onlyGame] : GameUtil.GetVersionsWithinRange(blank, blank.Context).ToArray();
                for (byte f = 0; f < pi.FormCount; f++)
                {
                    if (FormInfo.IsBattleOnlyForm(spc, f, blank.Format)) continue;
                    blank.Species = spc; blank.Form = f; blank.SetGender(blank.GetSaneGender());
                    EncounterMovesetGenerator.OptimizeCriteria(blank, sav);
                    list.AddRange(EncounterMovesetGenerator.GenerateEncounters(blank, sav, ReadOnlyMemory<ushort>.Empty, versions));
                }
                return list.Distinct(new RefEq()).Where(e => (!egg || e.IsEgg) && (!shiny || e.IsShiny) && (!final || e.Species == spc))   // 미진화체 제외: 고른 포켓몬 자체의 조우만
                    .Select(e =>
                {
                    var lv = e.LevelMin == e.LevelMax ? $"Lv{e.LevelMin}" : $"Lv{e.LevelMin}-{e.LevelMax}";
                    var ball = e is IFixedBall { FixedBall: not PKHeX.Core.Ball.None } fb ? AppState.BallSprite((int)fb.FixedBall) : null;
                    var extra = e is MysteryGift mg ? mg.CardTitle : e.IsShiny ? "이로치 고정" : "";
                    var alpha = e is IAlphaReadOnly { IsAlpha: true } ? " · 우두머리" : "";
                    return new Row(e, AppState.Sprite(e.Species, e.Form, e.IsShiny), $"{Kind(e)} · {AppState.GameName(e.Version)}{alpha}", $"{lv}  {GameInfo.Strings.Species[e.Species]}  {extra}".Trim() + Place(e), ball, SlotInfo(e).TrimStart(' ', '·'));
                }).ToList();
            });
            lastKinds = rows.Select(x => Kind(x.Enc)).Distinct().ToList();
            if (onlyKind != null) rows = rows.Where(x => Kind(x.Enc) == onlyKind).ToList();
            if (EncCond.Mark >= 0) rows = rows.Where(x => EncCond.MarkPossible(x.Enc)).ToList();
            results.ItemsSource = rows;
            status.Text = rows.Count == 0 ? "조건에 맞는 인카운터가 없습니다" : $"{rows.Count}개 · 누르면 조건대로 만들어 편집기로 불러옵니다";
            markHelp.IsVisible = EncCond.Mark >= 0 && rows.Count == 0;
            if (markHelp.IsVisible)
                status.Text = $"{GameInfo.Strings.Species[species]}은(는) {EncCond.MarkName(EncCond.Mark)}을(를) 붙일 수 있는 조우가 없습니다" +
                    (EncCond.MarkIndex == RibbonIndex.MarkCurry ? " (소드실드 숨은 조우 중 와일드에어리어가 아닌 곳에서만 가능)" : EncCond.MarkIndex == RibbonIndex.MarkMightiest ? " (SV 7성 최강 레이드 포켓몬만 가능)" : "");
        }
        catch (Exception ex) { status.Text = "검색 실패: " + ex.Message; }
        finally { busy.IsVisible = busy.IsRunning = false; }
    }

    /// <summary>지정 시드부터 순서대로 계산 (코어 여러 개로 나눠서). 결과: 첫 개체 / 여러 개 / 전체(32비트).</summary>
    private async Task SeqSearch(Seeder sd, Page page)
    {
        generating = true; busy.IsVisible = busy.IsRunning = true; stopBtn.IsVisible = true;
        cts = new CancellationTokenSource(); var tok = cts.Token;
        ulong start = EncCond.StartSeed; if (sd.Is32) start &= 0xFFFFFFFF;
        ulong end = EncCond.EndSeed != 0 ? EncCond.EndSeed : sd.Is32 ? 0xFFFFFFFF : ulong.MaxValue; if (sd.Is32) end = Math.Min(end, 0xFFFFFFFF);
        if (end < start) { status.Text = "끝 시드가 시작 시드보다 작습니다"; generating = false; busy.IsVisible = busy.IsRunning = false; stopBtn.IsVisible = false; return; }
        double span = end - start + 1.0;
        int mode = EncCond.ResultMode; if (mode == 2 && !sd.Is32) { mode = 1; Note.Show("이 조우는 시드가 64비트라 '전체' 대신 여러 개 모으기로 찾습니다"); }
        int maxHits = mode == 0 ? 1 : mode == 1 ? EncCond.MaxResults : 500;
        var hits = new List<(ulong Seed, PKM Pk)>(); long checkedN = 0, matched = 0; var gate = new object();
        var stop = new CancellationTokenSource(); var link = CancellationTokenSource.CreateLinkedTokenSource(tok, stop.Token).Token;
        int workers = Math.Max(1, Environment.ProcessorCount - 1); var sw = System.Diagnostics.Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, workers).Select(w => Task.Run(() =>
        {
            var p = sd.Template.Clone();
            for (ulong i = (ulong)w; !link.IsCancellationRequested; i += (ulong)workers)
            {
                ulong seed = start + i;
                if (seed > end || seed < start) break;
                bool ok; try { ok = sd.Gen(p, seed); } catch { ok = false; }
                Interlocked.Increment(ref checkedN);
                if (!ok || !EncCond.Satisfied(p)) continue;
                var c = sd.Finish(p.Clone()); EncCond.ApplyMark(c);
                if (!AppState.IsLegal(c)) continue;
                Interlocked.Increment(ref matched);
                lock (gate)
                {
                    if (hits.Count < 500) hits.Add((seed, c));
                    if (mode != 2 && hits.Count >= maxHits) stop.Cancel();
                }
            }
        })).ToArray();
        while (!Task.WaitAll(tasks, 300))
        {
            var c = Interlocked.Read(ref checkedN); var el = sw.Elapsed; var rate = el.TotalSeconds > 0 ? c / el.TotalSeconds : 0;
            string pct = "";
            if ((sd.Is32 || EncCond.EndSeed != 0) && rate > 0) { var left = (span - c) / rate; pct = $" · {c / span:P1} · 남은 시간 약 {TimeSpan.FromSeconds(Math.Max(0, left)):hh\\:mm\\:ss}"; }
            status.Text = $"시드 {c:N0}개 확인 · {rate:N0}개/초 · 발견 {Interlocked.Read(ref matched)}{pct}";
            await Task.Delay(1);
        }
        EncCond.LastSeed = start + (ulong)Interlocked.Read(ref checkedN);
        bool exhausted = (sd.Is32 || EncCond.EndSeed != 0) && !tok.IsCancellationRequested && !stop.IsCancellationRequested;
        generating = false; busy.IsVisible = busy.IsRunning = false; stopBtn.IsVisible = false;
        var list = hits.OrderBy(h => h.Seed).ToList();
        if (list.Count == 0)
        {
            status.Text = exhausted ? $"시드 {start:X}~{end:X} 범위를 모두 확인했습니다: 이 조건을 만족하는 합법 개체는 없습니다" : $"{checkedN:N0}개 확인, 아직 없음 · 조건 탭 '이어서'로 계속 찾을 수 있습니다";
            return;
        }
        status.Text = $"발견 {matched}개{(exhausted ? " (전체 확인 완료)" : "")} · 마지막 확인 시드 {EncCond.LastSeed:X}";
        if (mode == 0) { LoadSeed(sd, list[0].Seed); return; }
        SheetHost.Show(new EncResultSheet($"결과 {list.Count}개 (시드순)", list.Select(h => (h.Pk, $"시드 {(sd.Is32 ? h.Seed.ToString("X8") : h.Seed.ToString("X16"))}")).ToList(), i => LoadSeed(sd, list[i].Seed)));
    }

    private void LoadSeed(Seeder sd, ulong seed)
    {
        var p = sd.Template.Clone(); sd.Gen(p, seed); p = sd.Finish(p); EncCond.ApplyMark(p); p = EvoUtil.ToTarget(p, species); p.ResetPartyStats(); p.RefreshChecksum();
        var conv = p.GetType() == AppState.Sav.PKMType ? p : EntityConverter.ConvertToType(p, AppState.Sav.PKMType, out _) ?? p;
        Close(); AppState.Load(conv); AppState.Edited();
        Note.Show($"시드 {seed:X} 개체를 불러왔습니다{(AppState.IsLegal(conv) ? " (합법)" : " — 합법성 표시를 확인하세요")}");
    }

    private async Task Choose(Row r)
    {
        var page = Application.Current.Windows[0].Page;
        if (AppState.Dirty && !await page.DisplayAlertAsync("저장하지 않은 변경", "편집 중인 내용을 버리고 불러올까요?", "불러오기", "취소")) return;
        if (generating) return;
        var why = EncCond.Impossible(r.Enc);
        if (why != null)
        {
            if (!await page.DisplayAlertAsync("조건 불가능", $"{why}. 조건을 만족하는 개체를 만들 수 없습니다. 조건 없이 만든 개체를 불러올까요?", "불러오기", "취소")) { status.Text = why; return; }
        }
        if (why == null && EncCond.SeedMode == 1)
        {
            var sd = Seeder.For(r.Enc, AppState.Sav);
            if (sd != null) { await SeqSearch(sd, page); return; }
            Note.Show("이 조우는 시드로 만들 수 없어 무작위로 탐색합니다");
        }
        var sav = AppState.Sav;
        // 조건이 없으면 바로 한 마리 만들어 불러오기
        if (why != null || !EncCond.HasAny)
        {
            PKM p0 = null;
            try { var t0 = r.Enc.ConvertToPKM(sav, EncounterCriteria.Unrestricted); p0 = EntityConverter.ConvertToType(t0, sav.PKMType, out _) ?? t0; if (why == null) EncCond.ApplyMark(p0); } catch { }
            if (p0 == null) { status.Text = "이 인카운터로는 개체를 만들 수 없습니다"; return; }
            var note0 = FixDate(r, p0);
            p0 = EvoUtil.ToTarget(p0, species); Close(); AppState.Load(p0); AppState.Edited();
            Note.Show((why != null ? "조건 없이 불러왔습니다" : "불러왔습니다") + note0 + (AppState.IsLegal(p0) ? "" : " · 이 게임에서는 불법으로 판정됩니다"));
            return;
        }
        generating = true; busy.IsVisible = busy.IsRunning = true; stopBtn.IsVisible = true;
        cts = new CancellationTokenSource(); var tok = cts.Token;
        bool unlimited = EncCond.Unlimited;
        var crit = EncCond.Criteria();
        int want = EncCond.ResultMode == 0 ? 1 : EncCond.MaxResults;
        status.Text = "조건에 맞는 개체 생성 중…";
        var made = await Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); PKM last = null, best = null; int bestScore = -1; long tries = 0, legalN = 0, satIllegal = 0;
            var found = new List<PKM>(); bool noLegal = false;
            var gate = new object(); var stop = new CancellationTokenSource(); var link = CancellationTokenSource.CreateLinkedTokenSource(tok, stop.Token).Token;
            int workers = Math.Max(1, Environment.ProcessorCount - 1);
            var tasks = Enumerable.Range(0, workers).Select(w => Task.Run(() =>
            {
                while (!link.IsCancellationRequested && (unlimited || sw.Elapsed.TotalSeconds < 12))
                {
                    PKM p;
                    try { p = r.Enc.ConvertToPKM(sav, crit); } catch { stop.Cancel(); break; }
                    p = EntityConverter.ConvertToType(p, sav.PKMType, out _) ?? p;
                    EncCond.ApplyMark(p);
                    var n = Interlocked.Increment(ref tries);
                    if (last == null) lock (gate) last ??= p;
                    bool ok = EncCond.Satisfied(p); int sc = EncCond.Score(p);
                    if (!ok && sc <= Volatile.Read(ref bestScore) && Interlocked.Read(ref legalN) > 0) continue;
                    if (!AppState.IsLegal(p))
                    {
                        // 조건은 맞는데 합법이 안 나오는 경우가 계속되면(다른 게임 조우·홈 트래커 등) 무한 탐색하지 않고 멈춤
                        if (Interlocked.Increment(ref satIllegal) >= 300 && Interlocked.Read(ref legalN) == 0) { noLegal = true; stop.Cancel(); }
                        continue;
                    }
                    Interlocked.Increment(ref legalN);
                    lock (gate)
                    {
                        if (ok) { if (found.Count < want) found.Add(p); if (found.Count >= want) { stop.Cancel(); break; } continue; }
                        if (sc > bestScore) { bestScore = sc; best = p; }
                    }
                }
            })).ToArray();
            while (!Task.WaitAll(tasks, 300))
            {
                var t = Interlocked.Read(ref tries); var el = sw.Elapsed; int fc; lock (gate) fc = found.Count;
                MainThread.BeginInvokeOnMainThread(() => status.Text = $"찾는 중… {t:N0}회 · {el:mm\\:ss} · {(el.TotalSeconds > 0 ? t / el.TotalSeconds : 0):N0}회/초{(want > 1 ? $" · 발견 {fc}/{want}" : "")}{(unlimited ? " (찾을 때까지)" : "")}");
            }
            return (found, best ?? last, noLegal);
        });
        generating = false; busy.IsVisible = busy.IsRunning = false; stopBtn.IsVisible = false;
        var (list, fallback, noLegalOnly) = made;
        if (list.Count > 1)
        {
            status.Text = $"조건에 맞는 개체 {list.Count}개";
            SheetHost.Show(new EncResultSheet($"결과 {list.Count}개", list.Select(x => (x, "")).ToList(), k => { var pk = EvoUtil.ToTarget(list[k], species); var nt = FixDate(r, pk); Close(); AppState.Load(pk); AppState.Edited(); Note.Show("불러왔습니다" + nt); }));
            return;
        }
        PKM pick = list.Count == 1 ? list[0] : fallback;
        if (pick == null) { status.Text = "이 인카운터로는 개체를 만들 수 없습니다"; return; }
        if (list.Count == 0)
        {
            var msg = noLegalOnly ? "이 조우로 만든 개체가 현재 게임에서 합법으로 판정되지 않습니다(다른 게임 조우·홈 트래커 등)."
                : cts.IsCancellationRequested ? "탐색을 중지했습니다." : "12초 안에 조건을 모두 만족하는 합법 개체를 찾지 못했습니다.";
            if (!await page.DisplayAlertAsync("조건 불만족", $"{msg} {(noLegalOnly ? "그래도 불러올까요?" : "지금까지 가장 가까운 개체를 불러올까요?")}", "불러오기", "취소")) { status.Text = noLegalOnly ? "설정에서 홈 트래커 검사를 끄거나 원래 게임 세이브에서 찾아 보세요" : "조건을 완화하거나 '찾을 때까지 계속'을 켜 보세요"; return; }
        }
        pick = EvoUtil.ToTarget(pick, species);
        var dateNote = FixDate(r, pick);
        Close();
        AppState.Load(pick);
        AppState.Edited();
        Note.Show((list.Count == 1 ? "조건에 맞는 개체를 만들었습니다" : "가장 가까운 개체를 불러왔습니다") + dateNote);
    }

    /// <summary>배포 개체: 불바피디아 배포 기간(개체 언어 지역) 첫날로 만난 날짜 맞춤 (5~7세대 카드).</summary>
    private static string FixDate(Row r, PKM p)
    {
        if (r.Enc is MysteryGift mg && p.Format >= 5 && AppState.DistributionDate(mg, p.Language) is { } dd)
        {
            var test = p.Clone(); test.MetDate = dd;
            if (!AppState.IsLegal(p) || AppState.IsLegal(test)) { p.MetDate = dd; return $" · 배포일 {dd:yyyy-MM-dd}"; }
        }
        return "";
    }

    private sealed class RefEq : IEqualityComparer<IEncounterInfo>
    {
        public bool Equals(IEncounterInfo x, IEncounterInfo y) => ReferenceEquals(x, y);
        public int GetHashCode(IEncounterInfo o) => RuntimeHelpers.GetHashCode(o);
    }
}


/// <summary>조우별 "시드 → 개체" 계산기. 시드로 개체가 정해지지 않는 조우는 null.</summary>
/// <summary>여러 개 결과: 포켓몬 아이콘 + 크게 성별·성격·특성, 작게 시드·개체값.</summary>
public class EncResultSheet : Sheet
{
    public record Item(int Index, string Sprite, string Main, string Sub, bool Shiny, bool Alpha = false);
    public EncResultSheet(string title, List<(PKM Pk, string Seed)> list, Action<int> pick) : base(title)
    {
        var s = GameInfo.Strings;
        string Ab(PKM p) => p.Format >= 3 && p.Ability >= 0 && p.Ability < s.abilitylist.Length ? s.abilitylist[p.Ability] : "";
        var items = list.Select((x, i) =>
        {
            var p = x.Pk;
            var main = string.Join("  ·  ", new[] { p.Gender switch { 0 => "♂", 1 => "♀", _ => "무성" }, p.Format >= 3 ? s.natures[(int)p.Nature] : "", Ab(p) }.Where(t => t.Length > 0));
            var shiny = p.IsShiny ? (p.ShinyXor == 0 ? "■ " : "★ ") : "";
            var sub = $"{(x.Seed.Length > 0 ? x.Seed + " · " : "")}IV {p.IV_HP}/{p.IV_ATK}/{p.IV_DEF}/{p.IV_SPA}/{p.IV_SPD}/{p.IV_SPE}{(p is IScaledSize3 z ? $" · 배율 {z.Scale}" : "")}";
            return new Item(i, AppState.Sprite(p), shiny + main, sub, p.IsShiny, p is IAlpha { IsAlpha: true });
        }).ToList();
        var cv = new CollectionView { SelectionMode = SelectionMode.None, ItemsSource = items };
        cv.ItemTemplate = new DataTemplate(() =>
        {
            var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 10, Padding = new Thickness(4, 6) };
            var img = new Image { WidthRequest = 52, HeightRequest = 44 }; img.SetBinding(Image.SourceProperty, "Sprite");
            var t1 = T.L("", 16, bold: true); t1.SetBinding(Label.TextProperty, "Main");
            var t2 = T.L("", 12, sub: true); t2.SetBinding(Label.TextProperty, "Sub");
            var aIco = new Image { Source = "ov_alpha.png", WidthRequest = 16, HeightRequest = 16, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.End }; aIco.SetBinding(IsVisibleProperty, "Alpha");
            g.Add(new Grid { Children = { img, aIco } }, 0); g.Add(new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Children = { t1, t2 } }, 1);
            var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => { if (g.BindingContext is Item it) { Close(); pick(it.Index); } }; g.GestureRecognizers.Add(tap);
            return g;
        });
        Body.Add(cv);
    }
}
