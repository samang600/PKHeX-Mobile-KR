using CommunityToolkit.Maui.Storage;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using Microsoft.Maui.Controls.Shapes;
namespace PKHeXKR;

/// <summary>
/// 단일 화면: [상단 바] [접이식 박스] [개체 요약] [섹션 탭] [편집 섹션(스크롤)] [하단 작업 막대] + 시트 레이어
/// </summary>
public class MainPage : ContentPage
{
    public static MainPage Instance;
    private readonly BoxPanel box = new();
    private readonly Label saveTitle = T.L("", 17, bold: true), saveSub = T.L("", 12, sub: true);
    private Grid mainGrid; private ScrollView leftScroll, rightScroll; private VerticalStackLayout body, leftStack; private View boxP, summaryP, segP, hostP; private bool? wide;
    private readonly Button liveBtn = T.Pill("라이브헥스");
    private readonly Image sprite = new() { WidthRequest = 84, HeightRequest = 70 };
    private readonly Label name = T.L("", 19, bold: true), info = T.L("", 13, sub: true);
    private readonly Image shinyIcon = new() { Source = "rare_icon.png", WidthRequest = 16, HeightRequest = 16 }, ballIcon = new() { WidthRequest = 26, HeightRequest = 26 }, itemIcon = new() { WidthRequest = 22, HeightRequest = 22 };
    private readonly Label natureLabel = new() { FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#4F46E5"), TextDecorations = TextDecorations.Underline, VerticalOptions = LayoutOptions.Center, Padding = new Thickness(4, 0) };
    private readonly Label mintLabel = new() { FontSize = 13, VerticalOptions = LayoutOptions.Center, TextColor = Color.FromArgb("#6B7280"), Padding = new Thickness(0, 0, 4, 0) };   // (민트 성격)
    private readonly Label alphaLabel = new() { Text = "Ⓐ", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#DC2626"), VerticalOptions = LayoutOptions.Center, IsVisible = false };
    private readonly Label genderLabel = new() { FontSize = 20, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center, Padding = new Thickness(4, 0) };
    private readonly Button legalBtn = new() { FontSize = 12, CornerRadius = 14, HeightRequest = 28, Padding = new Thickness(10, 0), TextColor = Colors.White, VerticalOptions = LayoutOptions.Start, HorizontalOptions = LayoutOptions.End };   // 우측 상단
    private readonly Section[] sections = [new MainSection(), new StatsSection(), new MovesSection(), new MetSection(), new OTSection(), new RibbonSection(), new MiscSection()];
    private readonly string[] tabNames = ["기본", "능력치", "기술", "만남", "어버이", "리본", "기타"];
    private readonly List<Button> tabs = [];
    private readonly ContentView host = new();
    private readonly ScrollView scroll = new();
    private int tab;
    private View undoView, redoView;
    private bool summaryQueued;

    public MainPage()
    {
        Instance = this;
        T.Bg(this);
        Shell.SetNavBarIsVisible(this, false);

        // 상단 바
        var menu = T.Pill("⋮", size: 18); menu.WidthRequest = 44; menu.Padding = 0;
        menu.Clicked += async (_, _) => await Menu();
        liveBtn.Clicked += (_, _) => SheetHost.Show(new LiveHexSheet());
        var gameBtn = T.Pill("🎮", size: 18); gameBtn.WidthRequest = 44; gameBtn.Padding = 0;
        gameBtn.Clicked += async (_, _) => await ChangeGame();
        var top = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 8, Padding = new Thickness(16, 10, 12, 4) };
        top.Add(new VerticalStackLayout { Spacing = 0, Children = { saveTitle, saveSub } }, 0); top.Add(gameBtn, 1); top.Add(liveBtn, 2); top.Add(menu, 3);

        // 요약 카드
        legalBtn.Clicked += async (_, _) =>
        {
            if (AppState.Pk.Species == 0) return;
            if (AppState.HaX) { if (await DisplayAlertAsync("PKHaX 모드", "합법성 검사를 하지 않는 모드입니다. 검사 결과를 한 번 보시겠습니까?", "보기", "닫기")) SheetHost.Show(new LegalitySheet()); return; }
            SheetHost.Show(new LegalitySheet());
        };
        Tap(saveTitle, () => SheetHost.Show(new InstanceSheet()));
        Tap(saveSub, () => SheetHost.Show(new InstanceSheet()));
        Tap(name, async () =>
        {
            var p = AppState.Pk; if (p.Species == 0) return;
            var input = await DisplayPromptAsync("닉네임", "비우면 원래 이름으로 돌아갑니다", "확인", "취소", initialValue: p.IsNicknamed ? p.Nickname : "", maxLength: p.Format >= 8 ? 12 : p.Format >= 6 ? 12 : 10);
            if (input == null) return;
            AppState.Checkpoint(true);
            var def = PKHeX.Core.SpeciesName.GetSpeciesNameGeneration(p.Species, p.Language, p.Format);
            if (string.IsNullOrWhiteSpace(input) || input == def) { p.Nickname = def; p.IsNicknamed = false; } else { p.Nickname = input.Trim(); p.IsNicknamed = true; }
            AppState.Edited(); AppState.ReloadEditors();
        });
        // 요약 줄(레벨·언어·성격) 누르면 성격 선택 바로 열기
        Tap(info, () =>
        {
            var p0 = AppState.Pk; if (p0.Species == 0 || p0.Format < 3) return;
            SheetHost.Show(new PickerSheet("성격", AppState.NatureItems(), (int)p0.Nature, c => Quick(p => { p.Nature = (Nature)c.Value; if (p.Format < 8) p.StatAlignment = p.Nature; }, p => $"성격을 {GameInfo.Strings.natures[(int)p.Nature]}(으)로 바꿨습니다")));
        });
        var icons = new HorizontalStackLayout { Spacing = 8, Children = { alphaLabel, genderLabel, natureLabel, mintLabel, ballIcon, itemIcon, shinyIcon } };
        Tap(mintLabel, () => SheetHost.Show(new PickerSheet("민트 (능력 성격)", AppState.NatureItems(), (int)AppState.Pk.StatAlignment,
            c => Quick(p => p.StatAlignment = (Nature)c.Value, p => $"민트 성격을 {GameInfo.Strings.natures[(int)p.StatAlignment]}(으)로 바꿨습니다"))));
        // 요약 카드: 그림 = 이로치 전환, 성별 기호 = 성별 전환, 볼 = 볼 변경
        Tap(sprite, () => Quick(p => { if (p.IsShiny) p.SetUnshiny(); else p.SetShiny(); }, p => p.IsShiny ? "이로치로 바꿨습니다" : "일반 색으로 바꿨습니다"));
        Tap(genderLabel, () => Quick(p =>
        {
            var pi = p.PersonalInfo;
            if (pi.Genderless || pi.OnlyMale || pi.OnlyFemale) p.SetGender(p.GetSaneGender()); else p.SetGender((byte)(p.Gender == 0 ? 1 : 0));
        }, p => p.Gender switch { 0 => "수컷으로 바꿨습니다", 1 => "암컷으로 바꿨습니다", _ => "성별이 없는 포켓몬입니다" }));
        Tap(natureLabel, () =>
        {
            var p0 = AppState.Pk; if (p0.Species == 0 || p0.Format < 3) return;
            SheetHost.Show(new PickerSheet("성격", AppState.NatureItems(), (int)p0.Nature, c => Quick(p => { p.Nature = (Nature)c.Value; if (p.Format < 8) p.StatAlignment = p.Nature; }, p => $"성격을 {GameInfo.Strings.natures[(int)p.Nature]}(으)로 바꿨습니다")));
        });
        Tap(ballIcon, () =>
        {
            var p0 = AppState.Pk; if (p0.Species == 0 || p0.Format < 3) return;
            var okBalls = LegalTools.LegalBalls(p0); SheetHost.Show(new PickerSheet("볼", AppState.Src.Balls, p0.Ball, c => Quick(p => p.Ball = (byte)c.Value, p => "볼을 바꿨습니다"), c => AppState.BallSprite(c.Value), c => okBalls.Contains(c.Value) ? 0 : 2));
        });
        var sumGrid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 };
        sumGrid.Add(sprite, 0);
        // 요약 카드 왼쪽 위 작은 마법봉: 자동 합법화
        var wand = new Button { Text = "🪄", FontSize = 13, Padding = 0, WidthRequest = 24, HeightRequest = 24, CornerRadius = 0, BorderWidth = 0, BackgroundColor = Colors.Transparent, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start };
        wand.Clicked += async (_, _) => { if (await DisplayAlertAsync("자동 합법화", "이 포켓몬을 자동 합법화하시겠습니까?", "합법화", "취소")) await Legalize(); };
        sumGrid.Add(wand, 0);
        sumGrid.Add(new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { name, info, icons } }, 1);
        sumGrid.Add(legalBtn, 2);
        var summary = T.Card(sumGrid, 10);

        // 섹션 탭 (세그먼트)
        var seg = new Grid { ColumnSpacing = 4, Padding = 4 };
        for (int i = 0; i < tabNames.Length; i++)
        {
            int k = i;
            seg.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var b = new Button { Text = tabNames[i], FontSize = 11, CornerRadius = 10, HeightRequest = 36, Padding = 0, BorderWidth = 0 };
            b.Clicked += (_, _) => SelectTab(k);
            tabs.Add(b); seg.Add(b, i);
        }
        var segCard = new Border { Content = seg, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 12 } };
        segCard.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#E5E7EB"), Color.FromArgb("#1F232B"));


        // 하단 작업 막대
        var bar = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star) }, Padding = new Thickness(4, 2, 4, 6) };
        bar.Add(T.Action("⌕", "인카운터", () => SheetHost.Show(new EncounterSheet())), 0);
        bar.Add(T.Action("✍", "빠른 생성", () => SheetHost.Show(new QuickSheet())), 1);
        undoView = T.Action("↶", "실행 취소", () => { AppState.Undo(); Note.Show("실행 취소"); }); bar.Add(undoView, 2);
        redoView = T.Action("↷", "다시 실행", () => { AppState.Redo(); Note.Show("다시 실행"); }); bar.Add(redoView, 3);
        bar.Add(T.Action("💾", "저장·내보내기", async () => await SaveMenu()), 4);
        AppState.HistoryChanged += () => MainThread.BeginInvokeOnMainThread(() => { undoView.Opacity = AppState.CanUndo ? 1 : 0.35; redoView.Opacity = AppState.CanRedo ? 1 : 0.35; });
        undoView.Opacity = redoView.Opacity = 0.35;
        var barCard = new Border { Content = bar, StrokeThickness = 0 }; T.CardBg(barCard);
        var barLine = new BoxView { HeightRequest = 1 }; T.Line(barLine);

        var main = new Grid
        {
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) },
            RowSpacing = 0,
        };
        // 박스를 펼쳐도 편집기까지 내려갈 수 있게 상단 바·하단 막대를 뺀 전체를 한 번에 스크롤
        boxP = Pad(box); summaryP = Pad(summary); segP = Pad(segCard); hostP = Pad(host);
        body = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 4, 0, 12) };
        leftStack = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 4, 0, 12) };
        leftScroll = new ScrollView { Content = leftStack }; rightScroll = new ScrollView();
        scroll.Content = body;
        main.Add(top, 0, 0);
        main.Add(scroll, 0, 4);
        mainGrid = main;
        ApplyLayout(false);
        SizeChanged += (_, _) => { if (Width > 0 && Height > 0) ApplyLayout(Width > Height * 1.1); };
        BoxPanel.ExpandedChanged += () => MainThread.BeginInvokeOnMainThread(() => ApplyLayout(wide == true));
        main.Add(barLine, 0, 5);
        main.Add(barCard, 0, 6);

        var overlay = new Grid { InputTransparent = true, CascadeInputTransparent = false };
        SheetHost.Attach(overlay);
        var root = new Grid(); root.Add(main); root.Add(overlay);
        Content = root;

        AppState.PkLoaded += () => MainThread.BeginInvokeOnMainThread(() => { foreach (var s in sections) s.Reload(); UpdateSummary(); });
        AppState.PkEdited += QueueSummary;
        AppState.SaveChanged += () => MainThread.BeginInvokeOnMainThread(() => { UpdateSaveTitle(); UpdateLive(); });
        UpdateSaveTitle();
        SelectTab(0);
        foreach (var s in sections) s.Reload();
        UpdateSummary(); UpdateLive();
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(3), async () => await UpdateCheck.Run(false));
        AppState.LiveLost += () => MainThread.BeginInvokeOnMainThread(() => { UpdateLive(); Note.Show("라이브헥스 연결이 끊어졌습니다"); });
    }

    private static void Tap(View v, Action a) { var t = new TapGestureRecognizer(); t.Tapped += (_, _) => a(); v.GestureRecognizers.Add(t); }
    private static void Quick(Action<PKM> change, Func<PKM, string> msg)
    {
        var p = AppState.Pk; if (p.Species == 0) { Note.Show("먼저 포켓몬을 불러오세요"); return; }
        AppState.Checkpoint(true);
        try { change(p); } catch (Exception ex) { Note.Show("바꾸지 못했습니다: " + ex.Message); return; }
        AppState.Edited(); AppState.ReloadEditors();
        Note.Show(msg(p));
    }

    /// <summary>세로: 위에서 아래로 한 줄. 가로: 원본 PKHeX처럼 왼쪽 편집기, 오른쪽 박스.</summary>
    private Grid split;
    private Grid leftGrid, stickyGrid; private ScrollView hostScroll, stickyScroll;
    private VerticalStackLayout stickyStack, header; private BoxView spacer;
    /// <summary>
    /// 세로: 박스·요약·탭·편집 내용이 함께 스크롤되다가, 요약+탭이 화면 위에 닿으면 그 자리에 고정(스티키).
    /// 가로: 왼쪽 [요약][탭] 고정 + 편집 내용 스크롤, 오른쪽 박스.
    /// </summary>
    private void ApplyLayout(bool isWide)
    {
        if (wide == isWide) return;
        try
        {
            wide = isWide;
            static void Detach(View v)
            {
                switch (v?.Parent)
                {
                    case Layout l: l.Remove(v); break;
                    case ScrollView sv when sv.Content == v: sv.Content = null; break;
                    case ContentView cv when cv.Content == v: cv.Content = null; break;
                }
            }
            foreach (var v in new View[] { boxP, summaryP, segP, hostP }) Detach(v);
            hostScroll ??= new ScrollView();
            leftGrid ??= new Grid { RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) }, RowSpacing = 8, Padding = new Thickness(0, 4, 0, 0) };
            if (stickyGrid == null)
            {
                spacer = new BoxView { Color = Colors.Transparent };
                stickyStack = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(0, 4, 0, 12) };
                stickyScroll = new ScrollView { Content = stickyStack };
                header = new VerticalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Start, Padding = new Thickness(0, 0, 0, 6) };
                T.Bg(header);
                stickyGrid = new Grid(); stickyGrid.Add(stickyScroll); stickyGrid.Add(header);
                stickyScroll.Scrolled += (_, _) => SyncSticky();
                header.SizeChanged += (_, _) => SyncSticky();
                stickyStack.SizeChanged += (_, _) => SyncSticky();
                boxP.SizeChanged += (_, _) => SyncSticky();
                boxP.PropertyChanged += (_, e) => { if (e.PropertyName is "Height" or "Y") SyncSticky(); };
                spacer.PropertyChanged += (_, e) => { if (e.PropertyName is "Y") SyncSticky(); };
                AppState.BoxChanged += () => { Dispatcher.Dispatch(SyncSticky); Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), SyncSticky); };
            }
            split ??= new Grid { ColumnDefinitions = { new(new GridLength(1.25, GridUnitType.Star)), new(GridLength.Star) }, ColumnSpacing = 4 };
            if (split.Children.Count == 0) { split.Add(leftGrid, 0); split.Add(rightScroll, 1); }
            foreach (var old in new View[] { scroll, split, stickyGrid }) mainGrid.Remove(old);
            var pg = mainGrid.Children.OfType<Grid>().FirstOrDefault(g => g.RowDefinitions.Count == 4); if (pg != null) mainGrid.Remove(pg);
            Detach(hostScroll);
            if (!isWide)
            {
                hostScroll.Content = null;
                stickyStack.Children.Clear();
                stickyStack.Children.Add(boxP); stickyStack.Children.Add(spacer); stickyStack.Children.Add(hostP);
                header.Children.Clear(); header.Children.Add(summaryP); header.Children.Add(segP);
                mainGrid.Add(stickyGrid, 0, 4);
                BoxPanel.Expanded = Preferences.Get("box_expanded", false);
            }
            else
            {
                header.Children.Clear();
                hostScroll.Content = hostP;
                leftGrid.Add(summaryP, 0, 0); leftGrid.Add(segP, 0, 1); leftGrid.Add(hostScroll, 0, 2);
                rightScroll.Content = boxP;
                mainGrid.Add(split, 0, 4);
                BoxPanel.Expanded = true;   // 가로 화면에서는 박스를 항상 펼침 (저장하지 않음)
            }
            AppState.NotifyBox();
            Dispatcher.Dispatch(SyncSticky);
        }
        catch (Exception ex) { Note.Show("화면 배치 전환 오류: " + ex.Message); }
    }

    /// <summary>요약+탭 머리글: 박스 아래에서 시작해 스크롤로 올라가다 화면 위에 닿으면 고정.</summary>
    private void SyncSticky()
    {
        if (wide == true || header == null || spacer == null) return;
        if (Math.Abs(spacer.HeightRequest - header.Height) > 0.5 && header.Height > 0) spacer.HeightRequest = header.Height;
        header.TranslationY = Math.Max(0, StickyTop() - stickyScroll.ScrollY);
    }
    /// <summary>머리글이 놓일 위치 = 박스 아래. 박스를 접고 펴는 순간 크기로 바로 계산 (spacer 위치는 다음 배치 때 갱신되므로 쓰지 않음).</summary>
    private double StickyTop() => boxP.Y + boxP.Height + stickyStack.Spacing;

    private static View Pad(View v) { v.Margin = new Thickness(12, 0); return v; }

    private void SelectTab(int i)
    {
        tab = i; host.Content = sections[i];
        if (wide == true) _ = hostScroll?.ScrollToAsync(0, 0, false);
        else if (stickyScroll != null && spacer != null && stickyScroll.ScrollY > StickyTop()) _ = stickyScroll.ScrollToAsync(0, StickyTop(), false);
        for (int k = 0; k < tabs.Count; k++)
        {
            var sel = k == i;
            tabs[k].FontAttributes = sel ? FontAttributes.Bold : FontAttributes.None;
            if (sel) { tabs[k].SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#3730A3")); tabs[k].SetAppThemeColor(Button.TextColorProperty, T.Accent, Colors.White); }
            else { tabs[k].BackgroundColor = Colors.Transparent; tabs[k].SetAppThemeColor(Button.TextColorProperty, Color.FromArgb("#4B5563"), Color.FromArgb("#9CA3AF")); }
        }
    }

    private void QueueSummary()
    {
        if (summaryQueued) return; summaryQueued = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () => { summaryQueued = false; UpdateSummary(); });
    }

    private void UpdateSummary()
    {
        try { UpdateSummaryCore(); } catch (Exception ex) { Note.Show("요약 표시 오류: " + ex.Message); }
    }
    private void UpdateSummaryCore()
    {
        var pk = AppState.Pk;
        if (pk.Species == 0)
        {
            sprite.Source = null; name.Text = "편집할 포켓몬이 없습니다"; info.FormattedText = null; info.Text = "박스에서 고르거나, 인카운터·쇼다운으로 만들어 보세요";
            shinyIcon.IsVisible = false; ballIcon.Source = itemIcon.Source = null; genderLabel.Text = ""; legalBtn.IsVisible = false; return;
        }
        sprite.Source = AppState.Sprite(pk);
        var sp = AppState.SpeciesName(pk);   // 개체 언어 기준
        name.Text = (pk.IsNicknamed ? $"{pk.Nickname} ({sp})" : sp);
        alphaLabel.IsVisible = pk is IAlpha { IsAlpha: true };
        genderLabel.Text = pk.Gender switch { 0 => "♂", 1 => "♀", _ => "–" };
        natureLabel.Text = pk.Format >= 3 ? GameInfo.Strings.natures[(int)pk.Nature] : ""; natureLabel.IsVisible = pk.Format >= 3;
        mintLabel.IsVisible = pk.Format >= 8; if (pk.Format >= 8) mintLabel.Text = $"({GameInfo.Strings.natures[(int)pk.StatAlignment]})";
        genderLabel.TextColor = pk.Gender switch { 0 => Color.FromArgb("#2563EB"), 1 => Color.FromArgb("#DB2777"), _ => Colors.Gray };
        {
            var lvSpan = new Span { Text = $"Lv.{pk.CurrentLevel}", TextDecorations = TextDecorations.Underline };
            var lvTap = new TapGestureRecognizer(); lvTap.Tapped += (_, _) => { if (AppState.Pk.CurrentLevel < 100) Quick(p => { p.CurrentLevel = 100; p.ResetPartyStats(); }, p => "레벨 100으로 바꿨습니다"); };
            lvSpan.GestureRecognizers.Add(lvTap);
            var lgSpan = new Span { Text = AppState.LangName(pk.Language), TextDecorations = TextDecorations.Underline };
            var lgTap = new TapGestureRecognizer(); lgTap.Tapped += (_, _) => SheetHost.Show(new PickerSheet("언어", AppState.Src.Languages, AppState.Pk.Language, c => Quick(p => p.Language = c.Value, p => $"언어를 {AppState.LangName(p.Language)}(으)로 바꿨습니다")));
            lgSpan.GestureRecognizers.Add(lgTap);
            var fs = new FormattedString(); fs.Spans.Add(lvSpan); fs.Spans.Add(new Span { Text = "  ·  " }); fs.Spans.Add(lgSpan); fs.Spans.Add(new Span { Text = $"  ·  {(AppState.Dirty ? "변경됨" : "저장됨")}" });
            info.FormattedText = fs;
        }
        shinyIcon.IsVisible = pk.IsShiny;
        ballIcon.Source = pk.Format >= 3 ? AppState.BallSprite(pk.Ball) : null;
        itemIcon.Source = AppState.ItemSprite(pk.HeldItem);
        legalBtn.IsVisible = true;
        if (AppState.HaX) { legalBtn.Text = "PKHaX"; legalBtn.BackgroundColor = T.Warn; return; }
        bool ok = AppState.IsLegal(pk);
        legalBtn.Text = ok ? "✔ 합법" : "✖ 불법";
        legalBtn.BackgroundColor = ok ? T.Good : T.Bad;
    }

    private void UpdateSaveTitle()
    {
        var s = AppState.Sav;
        var tid = s.Generation >= 7 ? $"{s.ID32 % 1_000_000:000000}" : $"{s.TID16:00000}";
        saveTitle.Text = (AppState.Instances.Count > 1 ? $"[{AppState.Active + 1}/{AppState.Instances.Count}] " : "") + AppState.GameName(s.Version);   // 1줄: (인스턴스) 게임 이름
        var file = string.IsNullOrEmpty(s.Metadata.FileName) ? "" : $"  ·  {s.Metadata.FileName}";
        saveSub.Text = $"{s.OT}  ·  TID {tid}{file}{(AppState.HaX ? "  ·  ⚠ PKHaX" : "")}";   // 2줄: 트레이너 정보
    }

    public void UpdateLive()
    {
        liveBtn.Text = AppState.LiveConnected ? "● 라이브헥스" : "라이브헥스";
        liveBtn.TextColor = AppState.LiveConnected ? T.Good : null;
        if (!AppState.LiveConnected) liveBtn.SetAppThemeColor(Button.TextColorProperty, T.Accent, Color.FromArgb("#A5B4FC"));
    }

    public static async Task Legalize()
    {
        var page = Application.Current.Windows[0].Page;
        var pk = AppState.Pk;
        if (pk.Species == 0) return;
        try
        {
            var src = AppState.Source;
            bool movesReset = false;
            var result = await Task.Run(() =>
            {
                var r = AppState.Sav.Legalize(pk);
                if (new LegalityAnalysis(r).Valid) return r;
                var c = pk.Clone(); c.SetMoveset();   // 불가능한 기술 때문이면 추천 기술로 다시 시도
                var r2 = AppState.Sav.Legalize(c);
                if (new LegalityAnalysis(r2).Valid) { movesReset = true; return r2; }
                return r;
            });
            AppState.Load(result, src);
            AppState.Edited();
            Note.Show(new LegalityAnalysis(result).Valid ? (movesReset ? "합법으로 만들었습니다 (기술을 추천 기술로 변경)" : "합법으로 만들었습니다") : "합법으로 만들지 못했습니다");
        }
        catch (Exception ex) { await page.DisplayAlertAsync("자동 합법화 실패", ex.Message, "확인"); }
    }

    private async Task SaveToSlot()
    {
        if (AppState.Pk.Species == 0) { Note.Show("저장할 포켓몬이 없습니다"); return; }
        if (AppState.Source is { } s)
        {
            var where = s.Party ? $"파티 {s.Index + 1}번" : $"{AppState.BoxName(s.Box)} {s.Index + 1}번 칸";
            var pick = await DisplayActionSheetAsync("칸에 저장", "취소", null, $"원래 자리({where})에 덮어쓰기", "다른 칸 고르기");
            if (pick?.StartsWith("원래") == true)
            {
                AppState.PartyMode = s.Party; AppState.Box = s.Box;
                AppState.WriteSlot(s.Index, AppState.Pk.Clone());
                Note.Show($"{where}에 저장했습니다"); return;
            }
            if (pick != "다른 칸 고르기") return;
        }
        box.BeginPick();
        Note.Show("저장할 칸을 누르세요");
    }

    private Task Menu()
    {
        const string favs = "즐겨찾기", quick = "텍스트로 빠른 생성", ftp = "스위치 세이브 (무선 FTP)", mgift = "이상한 소포 받기함";
        const string dexFill = "세이브 도감 일괄 등록";
        const string recent = "최근 연 세이브", sharePk = "편집 중인 포켓몬 공유", dex = "리빙덱스 채우기", settings = "설정";
        const string boxOne = "박스 내보내기", boxAll = "모든 박스 내보내기", batch = "일괄 수정", player = "플레이어 편집", inst = "인스턴스 관리", copyPk = "포켓몬 복사", pastePk = "포켓몬 붙여넣기";
        const string open = "파일 열기 (세이브·포켓몬·박스)", export = "세이브 내보내기", exportPk = "편집 중인 포켓몬 파일로 저장", showdown = "쇼다운 세트 (가져오기·내보내기)",
            trainer = "트레이너 설정 (게임별)", game = "게임 변경", seed = "ZA 시드 파인더", home = "HOME Live (HOME 박스 보기)", autoOT = "자동 어버이작", clearAll = "한 번에 비우기", lang = "데이터 언어", about = "정보", haxOn = "PKHaX 모드 켜기", haxOff = "PKHaX 모드 끄기";
        var groups = new (string, (string Glyph, string Key)[])[]
        {
            ("파일", [("📂", open), ("🕘", recent), ("⭐", favs), ("💾", export), ("🧬", exportPk), ("📤", sharePk), ("📦", boxOne), ("⇄", showdown)]),
            ("게임 · 트레이너", [("🎮", game), ("👤", trainer), ("🤝", autoOT)]),
            ("인스턴스", [("🗂", inst), ("📋", copyPk), ("📥", pastePk)]),
            ("도구", [("✍", quick), ("📶", ftp), .. (MysteryGiftSheet.Supported(AppState.Sav) ? new (string, string)[] { ("🎁", mgift) } : []), ("🛠", batch), ("🎒", player), ("🏠", home), ("📚", dex)]),
            ("설정", [("⚠", AppState.HaX ? haxOff : haxOn), ("⚙", settings), ("ⓘ", about)]),
        };
        SheetHost.Show(new MenuSheet(groups, k => Dispatcher.Dispatch(async () => await RunMenu(k))));
        return Task.CompletedTask;
    }

    private async Task RunMenu(string pick)
    {
        const string favs = "즐겨찾기", quick = "텍스트로 빠른 생성", ftp = "스위치 세이브 (무선 FTP)", mgift = "이상한 소포 받기함";
        const string dexFill = "세이브 도감 일괄 등록";
        const string recent = "최근 연 세이브", sharePk = "편집 중인 포켓몬 공유", dex = "리빙덱스 채우기", settings = "설정";
        const string boxOne = "박스 내보내기", boxAll = "모든 박스 내보내기", batch = "일괄 수정", player = "플레이어 편집", inst = "인스턴스 관리", copyPk = "포켓몬 복사", pastePk = "포켓몬 붙여넣기";
        const string open = "파일 열기 (세이브·포켓몬·박스)", export = "세이브 내보내기", exportPk = "편집 중인 포켓몬 파일로 저장", showdown = "쇼다운 세트 (가져오기·내보내기)",
            trainer = "트레이너 설정 (게임별)", game = "게임 변경", seed = "ZA 시드 파인더", home = "HOME Live (HOME 박스 보기)", autoOT = "자동 어버이작", clearAll = "한 번에 비우기", lang = "데이터 언어", about = "정보", haxOn = "PKHaX 모드 켜기", haxOff = "PKHaX 모드 끄기";
        switch (pick)
        {
            case open: await OpenFile(); break;
            case recent: SheetHost.Show(new SaveListSheet(false)); break;
            case favs: SheetHost.Show(new FavoritesSheet()); break;
            case quick: SheetHost.Show(new QuickSheet()); break;
            case ftp: SheetHost.Show(new FtpSheet()); break;
            case mgift: SheetHost.Show(new MysteryGiftSheet()); break;
            case sharePk: if (AppState.Pk.Species == 0) { Note.Show("공유할 포켓몬이 없습니다"); break; } await ShareUtil.ShareBytes(ShareUtil.Stored(AppState.Pk), AppState.Pk.FileName, AppState.Pk.FileName); break;
            case dex: SheetHost.Show(new LivingDexSheet()); break;
            case dexFill: await DexFill.Run(this); break;
            case settings: SheetHost.Show(new SettingsSheet()); break;
            case export: await ExportSave(); break;
            case exportPk: await ExportPk(); break;
            case boxOne:
                var which = await DisplayActionSheetAsync("박스 내보내기", "취소", null, $"현재 박스 ({AppState.BoxName(AppState.Box)})", "모든 박스");
                if (which != null && which != "취소") await ExportBoxes(!which.StartsWith("현재"));
                break;
            case batch: SheetHost.Show(new BatchSheet()); break;
            case player: SheetHost.Show(new PlayerSheet()); break;
            case inst: SheetHost.Show(new InstanceSheet()); break;
            case copyPk:
                if (AppState.Pk.Species == 0) { Note.Show("복사할 포켓몬이 없습니다"); break; }
                AppState.Clipboard = AppState.Pk.Clone(); Note.Show("복사했습니다 · 다른 인스턴스에서 붙여넣기 하세요"); break;
            case pastePk:
                if (AppState.Clipboard == null) { Note.Show("복사한 포켓몬이 없습니다"); break; }
                if (AppState.Dirty && !await DisplayAlertAsync("저장하지 않은 변경", "편집 중인 내용을 버리고 붙여넣을까요?", "붙여넣기", "취소")) break;
                var conv = AppState.Clipboard.GetType() == AppState.Sav.PKMType ? AppState.Clipboard.Clone() : EntityConverter.ConvertToType(AppState.Clipboard, AppState.Sav.PKMType, out _);
                if (conv == null) { Note.Show("이 게임 형식으로 변환할 수 없습니다"); break; }
                AppState.Load(conv); AppState.Edited(); Note.Show("붙여넣었습니다 (박스 칸을 눌러 저장)"); break;
            case boxAll: await ExportBoxes(true); break;
            case showdown: SheetHost.Show(new ShowdownSheet()); break;
            case trainer: SheetHost.Show(new TrainerSheet()); break;
            case game: await ChangeGame(); break;
            case home: SheetHost.Show(new HomeLiveSheet()); break;
            case autoOT: SheetHost.Show(new AutoOTSheet()); break;
            case haxOn:
                if (await DisplayAlertAsync("PKHaX 모드 주의",
                    "PKHaX 모드는 개조롬·테스트용 데이터 편집을 위한 모드입니다.\n\n· 기술·특성·도구·종을 제한 없이 고를 수 있습니다\n· 합법성 검사를 하지 않으며, 합법 표시가 뜨지 않습니다\n· 만든 데이터는 정식 게임에서 불법이며, 온라인 대전·교환에 쓰면 이용 제한 등의 불이익을 받을 수 있습니다\n· 세이브가 손상될 수 있으니 반드시 백업한 뒤 사용하세요\n\n켜시겠습니까?", "켜기", "취소"))
                { AppState.SetHaX(true); Note.Show("PKHaX 모드를 켰습니다"); }
                break;
            case haxOff: AppState.SetHaX(false); Note.Show("PKHaX 모드를 껐습니다"); break;
            case lang:
                var l = await DisplayActionSheetAsync("데이터 언어 (재시작 후 적용)", "취소", null, AppState.Languages.Select(x => x.Name).ToArray());
                var sel = AppState.Languages.FirstOrDefault(x => x.Name == l);
                if (sel.Code != null) { Preferences.Set("data_lang", sel.Code); await DisplayAlertAsync("데이터 언어", $"{sel.Name}(으)로 설정했습니다. 앱을 다시 시작하면 적용됩니다.", "확인"); }
                break;
            case about:
                await DisplayAlertAsync("PKHeX 모바일", $"버전 {AppInfo.Current.VersionString}\nPKHeX.Core {typeof(PKM).Assembly.GetName().Version}\nAutoLegalityMod {typeof(APILegality).Assembly.GetName().Version}\n\nPKHeX(kwsch)·ALM(santacrab2 외) 기반, GPLv3\n시드 계산 참고: SWSHSeedFinderPlugin(hexbyt3), sv-research(MewTracker)", "확인");
                break;
        }
    }

    private async Task SaveMenu()
    {
        const string a = "세이브 내보내기", b = "편집 중인 포켓몬 파일로 저장", c = "현재 박스 데이터(.bin) 내보내기", fav = "편집 중인 포켓몬 즐겨찾기 등록";
        var pick = await DisplayActionSheetAsync("저장 · 내보내기", "취소", null, a, b, fav, c);
        if (pick == fav) { if (AppState.Pk.Species == 0) { Note.Show("등록할 포켓몬이 없습니다"); return; } Favorites.Add(AppState.Pk); Note.Show("즐겨찾기에 등록했습니다"); return; }
        if (pick == a) await ExportSave();
        else if (pick == b) await ExportPk();
        else if (pick == c)
        {
            var sav = AppState.Sav; if (!sav.HasBox) return;
            await using var ms = new MemoryStream(sav.GetBoxBinary(AppState.Box).ToArray());
            var r = await FileSaver.Default.SaveAsync($"box{AppState.Box + 1:00}.bin", ms, CancellationToken.None);
            Note.Show(r.IsSuccessful ? "박스를 저장했습니다" : "취소했습니다");
        }
    }

    /// <summary>박스 데이터(.bin) 내보내기: 현재 박스 한 개 또는 모든 박스를 한 파일로.</summary>
    private async Task ExportBoxes(bool all)
    {
        var sav = AppState.Sav; if (!sav.HasBox) { Note.Show("이 게임은 박스가 없습니다"); return; }
        try
        {
            var data = all ? sav.GetPCBinary().ToArray() : sav.GetBoxBinary(AppState.Box).ToArray();
            var name = all ? $"{sav.Version}_all_boxes.bin" : $"{sav.Version}_box{AppState.Box + 1:00}.bin";
            await using var ms = new MemoryStream(data);
            var r = await FileSaver.Default.SaveAsync(name, ms, CancellationToken.None);
            Note.Show(r.IsSuccessful ? (all ? $"모든 박스({sav.BoxCount}개)를 저장했습니다" : "현재 박스를 저장했습니다") : "취소했습니다");
        }
        catch (Exception ex) { await DisplayAlertAsync("내보내기 실패", ex.Message, "확인"); }
    }

    private async Task ChangeGame()
    {
        var names = AppState.Games.Select(AppState.GameName).ToArray();
        var pick = await DisplayActionSheetAsync($"게임 변경 (현재: {AppState.GameName(AppState.Sav.Version)})", "취소", null, names);
        int i = Array.IndexOf(names, pick); if (i < 0) return;
        var v = AppState.Games[i];
        var loaded = !string.IsNullOrEmpty(AppState.Sav.Metadata.FileName);
        if (!await DisplayAlertAsync("게임 변경", $"{pick} 빈 세이브로 바꿉니다.{(loaded ? "\n열려 있는 세이브는 닫힙니다(내보내지 않은 변경은 사라집니다)." : "")}\n기본 게임으로도 저장할까요?", "바꾸기", "취소")) return;
        AppState.DefGame = v;
        AppState.SetSave(AppState.NewBlankSave(v));
        Note.Show($"{pick}(으)로 바꿨습니다");
    }

    protected override bool OnBackButtonPressed() { HandleBack(); return true; }
    private bool exitAsking;
    /// <summary>뒤로가기: 열린 시트가 있으면 닫고, 없으면 종료 확인.</summary>
    public void HandleBack()
    {
        if (SheetHost.CloseTop()) return;
        if (exitAsking) return; exitAsking = true;
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                var msg = AppState.Dirty ? "편집 중인 포켓몬이 저장되지 않았습니다.\n그래도 종료할까요?" : "PKHeX 모바일을 종료할까요?";
                if (await DisplayAlertAsync("종료", msg, "종료", "취소")) { if (DeviceInfo.Platform == DevicePlatform.Android) Application.Current.Quit(); }
            }
            finally { exitAsking = false; }
        });
    }
    private bool OldBack()
    {
        if (SheetHost.CloseTop()) return true;
        Dispatcher.Dispatch(async () =>
        {
            var msg = AppState.Dirty ? "편집 중인 포켓몬이 저장되지 않았습니다.\n그래도 종료할까요?" : "PKHeX 모바일을 종료할까요?";
            if (await DisplayAlertAsync("종료", msg, "종료", "취소")) { if (DeviceInfo.Platform == DevicePlatform.Android) Application.Current.Quit(); }
        });
        return true;
    }

    private async Task OpenFile()
    {
        try
        {
            var f = await FilePicker.PickAsync();
            if (f == null) return;
            byte[] data;
            await using (var st = await f.OpenReadAsync()) { using var ms = new MemoryStream(); await st.CopyToAsync(ms); data = ms.ToArray(); }
            await OpenBytes(data, f.FileName);
        }
        catch (Exception ex) { await DisplayAlertAsync("열기 실패", ex.Message, "확인"); }
    }

    /// <summary>바이트로 열기 (파일 선택·다른 앱에서 공유/열기·최근 목록 공통).</summary>
    public async Task OpenBytes(byte[] data, string fileName, bool remember = true)
    {
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))   // DBI 등 압축 세이브: 안의 세이브 파일(main 등)을 꺼내 엶
        {
            if (ZipSave.Extract(data) is not { } z) { await DisplayAlertAsync("열기 실패", "zip 안에서 세이브 파일을 찾지 못했습니다", "확인"); return; }
            data = z.Data; fileName = System.IO.Path.GetFileName(z.Entry);
        }
        try
        {
            var obj = FileUtil.GetSupportedFile(data, System.IO.Path.GetExtension(fileName), AppState.Sav);
            if (remember && obj is SaveFile) SaveStore.Remember(data, fileName);
            switch (obj)
            {
                case SaveFile s:
                    s.Metadata.SetExtraInfo(fileName);
                    if (!string.IsNullOrEmpty(AppState.Sav.Metadata.FileName))   // 다른 세이브를 편집 중이면 새 인스턴스로
                    { AppState.AddInstance(s); Note.Show($"새 인스턴스로 열었습니다 ({AppState.Instances.Count}개)"); }
                    else { AppState.SetSave(s); Note.Show("세이브를 열었습니다"); }
                    break;
                case PKM p: AppState.Load(p); Note.Show("포켓몬을 불러왔습니다"); break;
                case MysteryGift g when g.IsEntity: AppState.Load(g.ConvertToPKM(AppState.Sav)); break;
                case ConcatenatedEntitySet set:
                    var sav = AppState.Sav; int n = 0;
                    var start = await DisplayAlertAsync("박스 데이터", $"{set.Count}칸 분량입니다. {AppState.BoxName(AppState.Box)}부터 채울까요?", "현재 박스부터", "1번 박스부터") ? AppState.Box : 0;
                    for (int i = 0; i < set.Count; i++)
                    {
                        int b = start + i / sav.BoxSlotCount; if (b >= sav.BoxCount) break;
                        var raw = set.Data.Span.Slice(i * set.SlotSize, set.SlotSize).ToArray();   // set.GetSlot(i)는 PKHeX 쪽 범위 검사 오류로 쓰지 않음
                        PKM p2; try { p2 = set.SlotSize == sav.SIZE_BOXSLOT ? sav.GetDecryptedPKM(raw) : EntityFormat.GetFromBytes(raw); } catch { p2 = null; }
                        if (p2 == null) continue;
                        if (p2.Species == 0) continue;
                        var c = EntityConverter.ConvertToType(p2, sav.PKMType, out _) ?? p2;
                        sav.SetBoxSlotAtIndex(c, b, i % sav.BoxSlotCount); n++;
                    }
                    AppState.NotifyBox(); Note.Show($"{n}마리를 넣었습니다"); break;
                default: await DisplayAlertAsync("열기", "지원하지 않는 파일입니다.", "확인"); break;
            }
        }
        catch (Exception ex) { await DisplayAlertAsync("열기 실패", ex.Message, "확인"); }
    }

    private async Task ExportSave()
    {
        var sav = AppState.Sav;
        try
        {
            if (sav.HasBox) sav.CurrentBox = AppState.Box;
            var ext = sav.Metadata.GetSuggestedExtension();
            var flags = sav.Metadata.GetSuggestedFlags(ext);
            await using var ms = new MemoryStream(sav.Write(flags).ToArray());
            var fileName = string.IsNullOrEmpty(sav.Metadata.FileName) ? "main" : sav.Metadata.FileName;
            if (SaveStore.AutoBackup) SaveStore.Backup(ms.ToArray(), fileName, "내보내기");
            var r = await FileSaver.Default.SaveAsync(fileName, ms, CancellationToken.None);
            Note.Show(r.IsSuccessful ? "세이브를 저장했습니다" : "저장을 취소했습니다");
        }
        catch (Exception ex) { await DisplayAlertAsync("저장 실패", ex.Message, "확인"); }
    }

    private async Task ExportPk()
    {
        var pk = AppState.Pk; if (pk.Species == 0) return;
        var data = new byte[pk.SIZE_STORED]; pk.WriteDecryptedDataStored(data);
        await using var ms = new MemoryStream(data);
        var r = await FileSaver.Default.SaveAsync(pk.FileName, ms, CancellationToken.None);
        Note.Show(r.IsSuccessful ? "저장했습니다" : "취소했습니다");
    }
}
