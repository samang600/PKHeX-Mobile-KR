using PKHeX.Core;
using PKHeX.Core.Injection;
using CommunityToolkit.Maui.Behaviors;
namespace PKHeXKR;

/// <summary>접이식 박스 패널. 기본은 접힘(한 줄), 펼치면 6열 격자.</summary>
public class BoxPanel : ContentView
{
    public static bool Expanded = Preferences.Get("box_expanded", false);
    public static event Action ExpandedChanged;   // 세로 화면 배치(상단 고정 여부) 전환용
    private bool pickMode;   // "칸에 저장" 대상 고르기
    public static bool SelectMode; public static readonly HashSet<(int Box, int Index)> Selected = new();
    private readonly Label filterInfo = T.L("", 12);
    private (int Box, int Index)? moving;   // 꾹 눌러 옮기는 중인 칸
    private readonly Label arrow = T.L("", 14, sub: true), title = T.L("", 15, bold: true), hint = T.L("", 12);
    private readonly Button modeBtn = T.Pill("파티");
    private readonly CollectionView grid = new() { SelectionMode = SelectionMode.None };
    private readonly Grid gridHost = new();

    public BoxPanel()
    {
        arrow.WidthRequest = 18;
        var prev = T.Pill("‹", size: 16); prev.WidthRequest = 40; prev.Padding = 0;
        var next = T.Pill("›", size: 16); next.WidthRequest = 40; next.Padding = 0;
        prev.Clicked += (_, _) => Step(-1); next.Clicked += (_, _) => Step(1);
        modeBtn.Clicked += (_, _) => { AppState.PartyMode = !AppState.PartyMode; AppState.NotifyBox(); };

        var jump = T.Pill("⇵", size: 15); jump.WidthRequest = 40; jump.Padding = 0;
        jump.Clicked += (_, _) =>
        {
            var sav = AppState.Sav; if (sav.BoxCount <= 0) return;
            var list = Enumerable.Range(0, sav.BoxCount).Select(b => { var d = sav.GetBoxData(b); var m = BoxFilter.Active ? $"  · 일치 {d.Count(BoxFilter.Match)}" : ""; return new ComboItem($"{b + 1}. {AppState.BoxName(b)}  ({d.Count(p => p.Species != 0)}/{sav.BoxSlotCount}){m}", b); }).ToList();
            SheetHost.Show(new PickerSheet("박스로 이동", list, AppState.Box, c =>
            {
                AppState.PartyMode = false; AppState.Box = c.Value;
                if (AppState.LiveConnected && Preferences.Get("live_read_on_change", true)) ReadLiveBox();
                if (!Expanded) Toggle(true); else AppState.NotifyBox();
            }));
        };
        var head = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 6 };
        head.Add(arrow, 0); head.Add(title, 1); head.Add(prev, 2); head.Add(jump, 3); head.Add(next, 4); head.Add(modeBtn, 5);
        foreach (var v in new View[] { arrow, title })
        { var t = new TapGestureRecognizer(); t.Tapped += (_, _) => Toggle(); v.GestureRecognizers.Add(t); }
        hint.TextColor = T.Accent; hint.IsVisible = false;

        grid.ItemsLayout = new GridItemsLayout(6, ItemsLayoutOrientation.Vertical) { VerticalItemSpacing = 4, HorizontalItemSpacing = 4 };
        grid.ItemTemplate = new DataTemplate(SlotTemplate);
        grid.HeightRequest = 5 * 58 + 20;
        gridHost.Children.Add(grid);

        // 두 번째 줄: 검색·필터 / 여러 칸 선택
        var filterBtn = T.Pill("검색", size: 12); filterBtn.HeightRequest = 32; filterBtn.Padding = new Thickness(10, 0);
        filterBtn.Clicked += (_, _) => SheetHost.Show(new FilterSheet(() => { if (!Expanded) Toggle(true); else Refresh(); }));
        var selBtn = T.Pill("여러 칸 선택", size: 12); selBtn.HeightRequest = 32; selBtn.Padding = new Thickness(10, 0);
        selBtn.Clicked += (_, _) => { SelectMode = !SelectMode; if (!SelectMode) Selected.Clear(); selBtn.Text = SelectMode ? "선택 끝내기" : "여러 칸 선택"; moving = null; if (!Expanded) Toggle(true); else Refresh(); };
        var actBtn = T.Pill("선택 작업", primary: true, size: 12); actBtn.HeightRequest = 32; actBtn.Padding = new Thickness(10, 0);
        actBtn.Clicked += async (_, _) => await SelectionActions();
        var clearBtn = T.Pill("비우기", size: 12); clearBtn.HeightRequest = 32; clearBtn.Padding = new Thickness(10, 0);
        var fillBtn = T.Pill("채우기", size: 12); fillBtn.HeightRequest = 32; fillBtn.Padding = new Thickness(10, 0);
        fillBtn.Clicked += async (_, _) => await FillBoxes();
        var sortBtn = T.Pill("정렬", size: 12); sortBtn.HeightRequest = 32; sortBtn.Padding = new Thickness(10, 0);
        sortBtn.Clicked += async (_, _) => await SortBoxes();
        var nameBtn = T.Pill("이름", size: 12); nameBtn.HeightRequest = 32; nameBtn.Padding = new Thickness(10, 0);
        nameBtn.Clicked += async (_, _) => await RenameBox();
        clearBtn.Clicked += async (_, _) => await ClearBoxes();
        actRow = new HorizontalStackLayout { Spacing = 6, Children = { filterBtn, selBtn, actBtn, sortBtn, nameBtn, clearBtn, fillBtn } };
        this.actBtn = actBtn;
        filterInfo.TextColor = T.Good;
        Content = T.Card(new VerticalStackLayout { Spacing = 6, Children = { head, new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = actRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Never }, filterInfo, hint, gridHost } }, 10);
        AppState.BoxChanged += () => MainThread.BeginInvokeOnMainThread(Refresh);
        AppState.PkLoaded += () => MainThread.BeginInvokeOnMainThread(Refresh);
        Refresh();
    }

    private HorizontalStackLayout actRow; private Button actBtn;

    private View SlotTemplate()
    {
        var g = new Grid { HeightRequest = 56 };
        var bg = new Border { StrokeThickness = 1.5, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
        bg.SetBinding(Border.StrokeProperty, "Stroke");
        bg.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F9FAFB"), Color.FromArgb("#22262F"));
        var img = new Image { WidthRequest = 52, HeightRequest = 44, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        img.SetBinding(Image.SourceProperty, "Sprite");
        var star = new Image { Source = "rare_icon.png", WidthRequest = 13, HeightRequest = 13, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Margin = 3 };
        star.SetBinding(IsVisibleProperty, "Shiny");
        var bad = new BoxView { WidthRequest = 8, HeightRequest = 8, CornerRadius = 4, Color = T.Bad, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Start, Margin = 4 };
        bad.SetBinding(IsVisibleProperty, "Illegal");
        var item = new Image { WidthRequest = 16, HeightRequest = 16, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End, Margin = 2 };
        item.SetBinding(Image.SourceProperty, "Item");
        var alpha = new Label { Text = "Ⓐ", FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#DC2626"), HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.End, Margin = new Thickness(3, 0, 0, 1) };
        alpha.SetBinding(IsVisibleProperty, "Alpha");
        var check = new Label { Text = "✓", FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, BackgroundColor = T.Good, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End, Padding = new Thickness(3, 0), Margin = 2 };
        check.SetBinding(IsVisibleProperty, "Checked");
        g.SetBinding(OpacityProperty, "Dim");
        g.Add(bg); g.Add(img); g.Add(star); g.Add(bad); g.Add(item); g.Add(alpha); g.Add(check);
        // 누르기 = 메뉴, 꾹 누르기 = 옮기기
        var touch = new TouchBehavior
        {
            LongPressDuration = 450,
            Command = new Command(() => { if (g.BindingContext is Slot s) OnSlot(s); }),
            LongPressCommand = new Command(() => { if (g.BindingContext is Slot s) OnLongPress(s); }),
        };
        g.Behaviors.Add(touch);
        return g;
    }

    public record Slot(int Index, PKM Pk, string Sprite, bool Shiny, bool Illegal, string Item, Color Stroke, double Dim = 1, bool Checked = false) { public bool Alpha => Pk is IAlpha { IsAlpha: true }; }

    public void Toggle(bool? force = null)
    {
        Expanded = force ?? !Expanded;
        Preferences.Set("box_expanded", Expanded);
        Refresh();
        ExpandedChanged?.Invoke();
    }

    private void Step(int d)
    {
        var sav = AppState.Sav;
        if (AppState.PartyMode || sav.BoxCount <= 0) return;
        AppState.Box = (AppState.Box + d + sav.BoxCount) % sav.BoxCount;
        if (AppState.LiveConnected && Preferences.Get("live_read_on_change", true)) ReadLiveBox();
        AppState.NotifyBox();
    }

    public static void ReadLiveBox()
    {
        try
        {
            var r = AppState.Remote; var sav = AppState.Sav;
            var len = sav.BoxSlotCount * (RamOffsets.GetSlotSize(r.Version) + RamOffsets.GetGapSize(r.Version));
            lock (AppState.LiveLock) sav.SetBoxBinary(r.ReadBox(AppState.Box, len), AppState.Box);
        }
        catch { AppState.MarkLiveLost(); }
    }

    public void Refresh()
    {
        try { RefreshCore(); } catch (Exception ex) { Note.Show("박스 표시 오류: " + ex.Message); }
    }
    private void RefreshCore()
    {
        var sav = AppState.Sav;
        var slots = AppState.CurrentSlots();
        int filled = slots.Count(p => p.Species != 0);
        title.Text = AppState.PartyMode ? $"파티  ·  {filled}/6" : $"{AppState.BoxName(AppState.Box)}  ·  {filled}/{slots.Length}";
        modeBtn.Text = AppState.PartyMode ? "박스" : "파티";
        arrow.Text = Expanded ? "▾" : "▸";
        gridHost.IsVisible = Expanded;
        hint.IsVisible = pickMode || moving != null;
        hint.Text = moving is { } mv ? $"옮길 자리를 누르세요 ({AppState.BoxName(mv.Box)} {mv.Index + 1}번 → ?) · 다른 박스로도 옮길 수 있습니다 · 취소: 같은 칸을 다시 꾹" : "저장할 칸을 누르세요";
        if (actBtn != null) { actBtn.IsVisible = SelectMode; actBtn.Text = $"선택 작업 ({Selected.Count})"; }
        filterInfo.IsVisible = BoxFilter.Active;
        if (BoxFilter.Active) filterInfo.Text = $"검색: {BoxFilter.Describe()} · 이 박스 일치 {slots.Count(BoxFilter.Match)}";
        if (!Expanded) return;
        var src = AppState.Source;
        grid.HeightRequest = (AppState.PartyMode ? 1 : 5) * 60 + 16;
        grid.ItemsSource = slots.Select((p, i) =>
        {
            bool current = src is { } s && s.Party == AppState.PartyMode && (AppState.PartyMode || s.Box == AppState.Box) && s.Index == i;
            bool isMoving = moving is { } m && !AppState.PartyMode && m.Box == AppState.Box && m.Index == i;
            bool sel = SelectMode && !AppState.PartyMode && Selected.Contains((AppState.Box, i));
            bool fm = BoxFilter.Active && BoxFilter.Match(p);
            var stroke = sel ? T.Good : isMoving ? T.Warn : pickMode || moving != null ? T.Accent.WithAlpha(0.4f) : fm ? T.Good : current ? T.Accent : Colors.Transparent;
            double dim = BoxFilter.Active && !fm ? 0.3 : 1;
            return new Slot(i, p, AppState.Sprite(p), p.Species != 0 && p.IsShiny, p.Species != 0 && !AppState.IsLegal(p), AppState.ItemSprite(p.HeldItem), stroke, dim, sel);
        }).ToList();
    }

    /// <summary>하단 "칸에 저장"에서 호출: 칸 고르기 모드로 펼침.</summary>
    public void BeginPick() { pickMode = true; Toggle(true); }
    public void CancelPick() { pickMode = false; Refresh(); }

    private void OnLongPress(Slot s)
    {
        if (AppState.PartyMode) { Note.Show("파티에서는 옮기기를 지원하지 않습니다"); return; }
        if (moving is { } m && m.Box == AppState.Box && m.Index == s.Index) { moving = null; Refresh(); Note.Show("옮기기를 취소했습니다"); return; }
        if (s.Pk.Species == 0) return;
        try { HapticFeedback.Default.Perform(HapticFeedbackType.LongPress); } catch { }
        moving = (AppState.Box, s.Index);
        Refresh();
    }

    private async void OnSlot(Slot s)
    {
        if (SelectMode && !AppState.PartyMode)
        {
            var key = (AppState.Box, s.Index);
            if (s.Pk.Species == 0) return;
            if (!Selected.Remove(key)) Selected.Add(key);
            Refresh(); return;
        }
        var where = AppState.PartyMode ? $"파티 {s.Index + 1}번" : $"{AppState.BoxName(AppState.Box)} {s.Index + 1}번 칸";
        if (moving is { } mv && !AppState.PartyMode)
        {
            moving = null;
            if (mv.Box == AppState.Box && mv.Index == s.Index) { Refresh(); return; }
            AppState.MoveSlot(mv.Box, mv.Index, AppState.Box, s.Index);
            Note.Show(s.Pk.Species != 0 ? "두 포켓몬의 자리를 바꿨습니다" : "옮겼습니다");
            return;
        }
        if (pickMode)
        {
            if (s.Pk.Species != 0 && !await Page().DisplayAlertAsync("덮어쓰기", $"{where}의 포켓몬을 덮어쓸까요?", "덮어쓰기", "취소")) return;
            pickMode = false;
            AppState.WriteSlot(s.Index, AppState.Pk.Clone());
            Note.Show($"{where}에 저장했습니다");
            return;
        }
        if (s.Pk.Species == 0)
        {
            if (AppState.Pk.Species == 0) return;
            if (await Page().DisplayAlertAsync("저장", $"편집 중인 포켓몬을 {where}에 저장할까요?", "저장", "취소"))
            { AppState.WriteSlot(s.Index, AppState.Pk.Clone()); Note.Show("저장했습니다"); }
            return;
        }
        const string view = "정보 보기 (편집기로 불러오기)", over = "편집 중인 포켓몬으로 덮어쓰기", fav = "즐겨찾기에 저장", del = "삭제";
        var name = GameInfo.Strings.Species[s.Pk.Species];
        var pick = AppState.Pk.Species == 0
            ? await Page().DisplayActionSheetAsync($"{where} · {name}", "취소", null, view, fav, del)
            : await Page().DisplayActionSheetAsync($"{where} · {name}", "취소", null, view, over, fav, del);
        switch (pick)
        {
            case view:
                if (AppState.Dirty && !await Page().DisplayAlertAsync("저장하지 않은 변경", "편집 중인 내용이 저장되지 않았습니다. 버리고 불러올까요?", "불러오기", "취소")) return;
                AppState.Load(s.Pk, (AppState.PartyMode, AppState.Box, s.Index));
                break;
            case over:
                AppState.WriteSlot(s.Index, AppState.Pk.Clone()); Note.Show($"{where}에 덮어썼습니다");
                break;
            case fav:
                Favorites.Add(s.Pk); Note.Show($"{name}을(를) 즐겨찾기에 저장했습니다"); break;
            case del:
                if (await Page().DisplayAlertAsync("삭제", $"{where}의 {name}을(를) 삭제할까요?", "삭제", "취소")) { AppState.DeleteSlot(s.Index); Note.Show("삭제했습니다 (실행 취소 가능)"); }
                break;
        }
    }

    /// <summary>여러 칸 선택 후 작업: 다른 박스로 옮기기, 삭제, 박스 데이터로 내보내기, 공유.</summary>
    private async Task SelectionActions()
    {
        var sav = AppState.Sav;
        var picks = Selected.Where(k => sav.GetBoxSlotAtIndex(k.Box, k.Index).Species != 0).OrderBy(k => k.Box).ThenBy(k => k.Index).ToList();
        if (picks.Count == 0) { Note.Show("선택한 칸이 없습니다"); return; }
        const string move = "다른 박스로 옮기기", del = "삭제", export = "박스 데이터(.bin)로 내보내기", fav = "즐겨찾기 등록", clear = "선택 해제";
        var a = await Page().DisplayActionSheetAsync($"{picks.Count}칸 선택", "취소", null, move, fav, export, del, clear);
        byte[] Bin() { int size = sav.SIZE_BOXSLOT; var buf = new byte[picks.Count * size]; for (int i = 0; i < picks.Count; i++) { var p = sav.GetBoxSlotAtIndex(picks[i].Box, picks[i].Index); p.WriteEncryptedDataStored(buf.AsSpan(i * size, Math.Min(size, p.SIZE_STORED))); } return buf; }
        switch (a)
        {
            case move:
                var list = Enumerable.Range(0, sav.BoxCount).Select(b => new ComboItem($"{b + 1}. {AppState.BoxName(b)}  (빈 칸 {sav.GetBoxData(b).Count(p => p.Species == 0)})", b)).ToList();
                SheetHost.Show(new PickerSheet("옮길 박스", list, AppState.Box, c =>
                {
                    int moved = 0, b = c.Value;
                    foreach (var k in picks)
                    {
                        int slot = -1; for (int i = 0; i < sav.BoxSlotCount; i++) if (sav.GetBoxSlotAtIndex(b, i).Species == 0) { slot = i; break; }
                        if (slot < 0) break;
                        if (k.Box == b) continue;
                        sav.SetBoxSlotAtIndex(sav.GetBoxSlotAtIndex(k.Box, k.Index), b, slot); sav.SetBoxSlotAtIndex(sav.BlankPKM, k.Box, k.Index); moved++;
                    }
                    Selected.Clear(); AppState.NotifyBox(); Note.Show(moved < picks.Count ? $"{moved}마리 옮김 (빈 칸이 부족해 나머지는 그대로)" : $"{moved}마리를 옮겼습니다");
                }));
                break;
            case del:
                if (!await Page().DisplayAlertAsync("삭제", $"선택한 {picks.Count}마리를 삭제할까요? (실행 취소 불가)", "삭제", "취소")) return;
                foreach (var k in picks) sav.SetBoxSlotAtIndex(sav.BlankPKM, k.Box, k.Index);
                Selected.Clear(); AppState.NotifyBox(); Note.Show("삭제했습니다"); break;
            case export:
                await using (var ms = new MemoryStream(Bin())) { var r = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync($"selected_{picks.Count}.bin", ms, CancellationToken.None); Note.Show(r.IsSuccessful ? "저장했습니다" : "취소했습니다"); }
                break;
            case fav:
                foreach (var k in picks) Favorites.Add(sav.GetBoxSlotAtIndex(k.Box, k.Index));
                Note.Show($"{picks.Count}마리를 즐겨찾기에 등록했습니다"); break;
            case clear: Selected.Clear(); Refresh(); break;
        }
    }

    /// <summary>박스 정렬: 현재 박스 또는 모든 박스.</summary>
    private async Task SortBoxes()
    {
        if (AppState.PartyMode) { Note.Show("파티는 정렬하지 않습니다"); return; }
        const string dex = "도감 번호순", dexR = "도감 번호 역순", shiny = "이로치 먼저 (도감순)", lvDesc = "레벨 높은 순", lv = "레벨 낮은 순", ball = "볼 종류순", legal = "불법 먼저";
        var how = await Page().DisplayActionSheetAsync("정렬 방식", "취소", null, dex, dexR, shiny, lvDesc, lv, ball, legal);
        Func<IEnumerable<PKM>, int, IEnumerable<PKM>> m = how switch
        {
            dex => (l, _) => l.OrderBySpecies(),
            dexR => (l, _) => l.OrderByDescendingSpecies(),
            shiny => (l, _) => l.OrderByDescending(p => p.IsShiny).ThenBy(p => p.Species).ThenBy(p => p.Form),
            lvDesc => (l, _) => l.OrderByDescendingLevel(),
            lv => (l, _) => l.OrderByLevel(),
            ball => (l, _) => l.OrderBy(p => p.Ball).ThenBy(p => p.Species),
            legal => (l, _) => l.OrderBy(p => AppState.IsLegal(p)).ThenBy(p => p.Species),
            _ => null,
        };
        if (m == null) return;
        var where = await Page().DisplayActionSheetAsync("정렬 범위", "취소", null, $"현재 박스 ({AppState.BoxName(AppState.Box)})", "모든 박스");
        if (where == null || where == "취소") return;
        var sav = AppState.Sav;
        if (where.StartsWith("현재")) sav.SortBoxes(AppState.Box, AppState.Box, m); else sav.SortBoxes(0, -1, m);
        AppState.Source = null; Selected.Clear(); AppState.NotifyBox(); Note.Show("정렬했습니다 (빈 칸은 뒤로 모임)");
    }

    /// <summary>현재 박스 이름 바꾸기.</summary>
    private async Task RenameBox()
    {
        if (AppState.PartyMode || AppState.Sav is not IBoxDetailName n) { Note.Show("이 게임은 박스 이름을 바꿀 수 없습니다"); return; }
        var name = await Page().DisplayPromptAsync("박스 이름", $"{AppState.Box + 1}번 박스의 새 이름", "변경", "취소", initialValue: AppState.BoxName(AppState.Box), maxLength: 16);
        if (string.IsNullOrWhiteSpace(name)) return;
        try { n.SetBoxName(AppState.Box, name.Trim()); AppState.NotifyBox(); Note.Show("박스 이름을 바꿨습니다"); }
        catch (Exception ex) { Note.Show("바꾸지 못했습니다: " + ex.Message); }
    }

    /// <summary>편집기 포켓몬으로 채우기: 현재 박스 또는 모든 박스, 빈 칸만 또는 전부.</summary>
    private async Task FillBoxes()
    {
        var pk = AppState.Pk; var sav = AppState.Sav;
        if (pk.Species == 0) { Note.Show("편집기에 포켓몬이 없습니다"); return; }
        var where = await Page().DisplayActionSheetAsync($"{AppState.SpeciesName(pk)}(으)로 채우기", "취소", null, $"현재 박스 ({AppState.BoxName(AppState.Box)})", "모든 박스");
        if (where == null || where == "취소") return;
        var how = await Page().DisplayActionSheetAsync("채울 칸", "취소", null, "빈 칸만", "전부 덮어쓰기");
        if (how == null || how == "취소") return;
        bool all = where == "모든 박스", over = how == "전부 덮어쓰기";
        if (over && !await Page().DisplayAlertAsync("채우기", "기존 포켓몬을 덮어씁니다. 되돌릴 수 없습니다.", "채우기", "취소")) return;
        int n = 0;
        for (int bx = all ? 0 : AppState.Box; bx <= (all ? sav.BoxCount - 1 : AppState.Box); bx++)
            for (int i = 0; i < sav.BoxSlotCount; i++)
            {
                if (!over && sav.GetBoxSlotAtIndex(bx, i).Species != 0) continue;
                sav.SetBoxSlotAtIndex(pk.Clone(), bx, i); n++;
            }
        AppState.Source = null; AppState.NotifyBox(); Note.Show($"{n}칸을 채웠습니다");
    }

    /// <summary>한 번에 비우기: 현재 박스 또는 모든 박스.</summary>
    private async Task ClearBoxes()
    {
        var what = await Page().DisplayActionSheetAsync("비우기", "취소", null, $"현재 박스 ({AppState.BoxName(AppState.Box)})", "모든 박스");
        if (what == null || what == "취소") return;
        if (!await Page().DisplayAlertAsync("비우기", $"{what}의 포켓몬을 모두 지웁니다. 되돌릴 수 없습니다.", "비우기", "취소")) return;
        if (what.StartsWith("현재")) AppState.Sav.ClearBoxes(AppState.Box, AppState.Box + 1); else AppState.Sav.ClearBoxes();
        AppState.Source = null; Selected.Clear(); AppState.NotifyBox(); Note.Show("비웠습니다");
    }

    private static Page Page() => Application.Current.Windows[0].Page;
}
