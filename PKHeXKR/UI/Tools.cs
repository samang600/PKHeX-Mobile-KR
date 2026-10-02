using CommunityToolkit.Maui.Storage;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
namespace PKHeXKR;

/// <summary>박스 검색 조건 (칸 강조용).</summary>
public static class BoxFilter
{
    public static ushort Species; public static bool Shiny, Alpha, Illegal;
    public static bool Active => Species != 0 || Shiny || Alpha || Illegal;
    public static bool Match(PKM p)
    {
        if (p.Species == 0) return false;
        if (Species != 0 && p.Species != Species) return false;
        if (Shiny && !p.IsShiny) return false;
        if (Alpha && p is not IAlpha { IsAlpha: true }) return false;
        if (Illegal && AppState.IsLegal(p)) return false;
        return true;
    }
    public static string Describe()
    {
        var l = new List<string>();
        if (Species != 0) l.Add(GameInfo.Strings.Species[Species]);
        if (Shiny) l.Add("이로치"); if (Alpha) l.Add("우두머리"); if (Illegal) l.Add("불법");
        return string.Join(" · ", l);
    }
}

/// <summary>최근 연 세이브와 자동 백업 (앱 전용 폴더에 사본 보관).</summary>
public static class SaveStore
{
    public static bool AutoBackup { get => Preferences.Get("auto_backup", true); set => Preferences.Set("auto_backup", value); }
    public static string RecentDir => Dir("recent");
    public static string BackupDir => Dir("backup");
    private static string Dir(string n) { var d = Path.Combine(FileSystem.AppDataDirectory, n); Directory.CreateDirectory(d); return d; }
    private static string Safe(string n) => string.Concat((n ?? "save").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>세이브를 열 때: 최근 목록(10개)에 사본, 자동 백업이 켜져 있으면 백업(30개)에도 사본.</summary>
    public static void Remember(byte[] data, string name)
    {
        try
        {
            var n = Safe(name);
            foreach (var old in Directory.GetFiles(RecentDir).Where(f => Path.GetFileName(f).EndsWith("__" + n))) File.Delete(old);   // 같은 이름은 최신 1개만
            File.WriteAllBytes(Path.Combine(RecentDir, $"{DateTime.Now:yyyyMMdd_HHmmss}__{n}"), data);
            Trim(RecentDir, 10);
            if (AutoBackup) Backup(data, name, "열기");
        }
        catch { }
    }
    public static void Backup(byte[] data, string name, string tag)
    {
        try { File.WriteAllBytes(Path.Combine(BackupDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{tag}__{Safe(name)}"), data); Trim(BackupDir, 30); } catch { }
    }
    private static void Trim(string dir, int keep) { foreach (var f in Directory.GetFiles(dir).OrderByDescending(x => x).Skip(keep)) File.Delete(f); }
    public static List<(string Path, string Name, string When)> List(string dir) => Directory.GetFiles(dir).OrderByDescending(x => x).Select(f =>
    {
        var fn = Path.GetFileName(f); var i = fn.IndexOf("__"); var name = i >= 0 ? fn[(i + 2)..] : fn;
        var stamp = fn.Length >= 15 && DateTime.TryParseExact(fn[..15], "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd HH:mm") : "";
        var tag = i > 16 ? fn[16..i] : "";
        return (f, name, (stamp + (tag.Length > 0 ? " · " + tag : "")).Trim());
    }).ToList();
}

public static class ShareUtil
{
    public static async Task ShareBytes(byte[] data, string fileName, string title)
    {
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllBytesAsync(path, data);
        await Share.Default.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path) });
    }
    public static byte[] Stored(PKM p) { var b = new byte[p.SIZE_STORED]; p.WriteDecryptedDataStored(b); return b; }
}

// ======================= 박스 검색·필터 =======================
public class FilterSheet : Sheet
{
    public FilterSheet(Action changed) : base("박스 검색 · 필터", 0.7)
    {
        (var sv, var sl) = T.Chooser(() => { });
        sl.Text = BoxFilter.Species == 0 ? "모든 포켓몬" : GameInfo.Strings.Species[BoxFilter.Species];
        var all = new List<ComboItem> { new("모든 포켓몬", 0) }; all.AddRange(AppState.Src.Species.Where(x => x.Value > 0));
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => SheetHost.Show(new PickerSheet("포켓몬", all, BoxFilter.Species, c => { BoxFilter.Species = (ushort)c.Value; sl.Text = c.Text; }, c => c.Value == 0 ? null : AppState.Sprite((ushort)c.Value, 0, false)));
        sv.GestureRecognizers.Clear(); sv.GestureRecognizers.Add(tap);
        View Sw(string label, bool v, Action<bool> set) { var s = new Switch { IsToggled = v, OnColor = T.Accent }; s.Toggled += (_, e) => set(e.Value); var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, HeightRequest = 44 }; g.Add(T.L(label, 15), 0); g.Add(s, 1); return g; }
        var apply = T.Pill("적용", primary: true); apply.Clicked += (_, _) => { Close(); changed(); };
        var clear = T.Pill("필터 해제"); clear.Clicked += (_, _) => { BoxFilter.Species = 0; BoxFilter.Shiny = BoxFilter.Alpha = BoxFilter.Illegal = false; Close(); changed(); };
        var hint = T.L("조건에 맞는 칸은 초록 테두리로 강조되고, 나머지는 흐리게 표시됩니다. 박스 이동 목록에도 박스별 일치 수가 나옵니다.", 12, sub: true); hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, T.Field("포켓몬", sv), Sw("이로치만", BoxFilter.Shiny, v => BoxFilter.Shiny = v), Sw("우두머리만", BoxFilter.Alpha, v => BoxFilter.Alpha = v), Sw("불법만", BoxFilter.Illegal, v => BoxFilter.Illegal = v), new HorizontalStackLayout { Spacing = 8, Children = { apply, clear } } } } });
    }
}

// ======================= 최근 연 세이브 / 백업 목록 =======================
public class SaveListSheet : Sheet
{
    public SaveListSheet(bool backup) : base(backup ? "백업 목록" : "최근 연 세이브", 0.75)
    {
        var dir = backup ? SaveStore.BackupDir : SaveStore.RecentDir;
        var list = new VerticalStackLayout { Spacing = 6 };
        var items = SaveStore.List(dir);
        if (items.Count == 0) list.Children.Add(T.L(backup ? "백업이 없습니다 (설정에서 자동 백업을 켜면 세이브를 열 때 원본이 보관됩니다)" : "최근에 연 세이브가 없습니다", 13, sub: true));
        foreach (var it in items)
        {
            var name = T.L(it.Name, 15, bold: true); name.LineBreakMode = LineBreakMode.WordWrap;
            var row = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(10, 8), Children = { name, T.L(it.When, 12, sub: true) } };
            var cell = new Border { Content = row, StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
            cell.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F9FAFB"), Color.FromArgb("#22262F"));
            var t = new TapGestureRecognizer();
            t.Tapped += async (_, _) =>
            {
                var page = Application.Current.Windows[0].Page;
                var pick = await page.DisplayActionSheetAsync(it.Name, "취소", null, "열기", "공유", "삭제");
                if (pick == "열기") { Close(); await MainPage.Instance.OpenBytes(File.ReadAllBytes(it.Path), it.Name, remember: false); }
                else if (pick == "공유") await ShareUtil.ShareBytes(File.ReadAllBytes(it.Path), it.Name, it.Name);
                else if (pick == "삭제") { File.Delete(it.Path); Close(); SheetHost.Show(new SaveListSheet(backup)); }
            };
            cell.GestureRecognizers.Add(t);
            list.Children.Add(cell);
        }
        var note = T.L("앱 안에 보관된 사본을 엽니다. 편집 후에는 '세이브 내보내기'로 원하는 위치에 저장하세요.", 12, sub: true); note.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { note, list } } });
    }
}

// ======================= 설정 =======================
public class SettingsSheet : Sheet
{
    public static void ApplyTheme() => Application.Current.UserAppTheme = (AppTheme)Preferences.Get("theme_mode", 0);
    public SettingsSheet() : base("설정", 0.7)
    {
        int mode = Preferences.Get("theme_mode", 0);
        var btns = new List<Button>(); var row = T.Cols(3, 6);
        string[] names = ["자동 (기기 설정)", "라이트", "다크"];
        void Style() { for (int i = 0; i < 3; i++) { var b = btns[i]; if (i == mode) { b.BackgroundColor = T.Accent; b.TextColor = Colors.White; } else { b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); b.SetAppThemeColor(Button.TextColorProperty, T.Accent, Color.FromArgb("#A5B4FC")); } } }
        for (int i = 0; i < 3; i++) { int k = i; var b = T.Pill(names[i], size: 12); b.Padding = new Thickness(4, 0); b.Clicked += (_, _) => { mode = k; Preferences.Set("theme_mode", k); ApplyTheme(); Style(); }; btns.Add(b); row.Add(b, i); }
        Style();
        var hc = new Switch { IsToggled = AppState.HandlerCheck, OnColor = T.Accent }; hc.Toggled += (_, e) => { AppState.HandlerCheck = e.Value; AppState.RefreshSummary(); AppState.NotifyBox(); };
        var hcRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, HeightRequest = 44 };
        var hcLab = T.L("현재 트레이너를 세이브 주인과 대조", 15); hcLab.LineBreakMode = LineBreakMode.WordWrap; hcRow.Add(hcLab, 0); hcRow.Add(hc, 1);
        var hcHint = T.L("끄면(기본) 교환받은 포켓몬의 '현재 트레이너 이름·성별이 예상과 다름' 검사를 하지 않습니다. 다른 세이브에서 가져온 합법 개체가 불법으로 표시되는 문제를 막습니다.", 12, sub: true); hcHint.LineBreakMode = LineBreakMode.WordWrap;
        View Tog(string label, bool v, Action<bool> set) { var w = new Switch { IsToggled = v, OnColor = T.Accent }; w.Toggled += (_, e) => { set(e.Value); AppState.RefreshSummary(); AppState.NotifyBox(); }; var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, MinimumHeightRequest = 44 }; var l = T.L(label, 15); l.LineBreakMode = LineBreakMode.WordWrap; g.Add(l, 0); g.Add(w, 1); return g; }
        var trkS = Tog("홈 트래커 없음 검사 (소드실드)", AppState.TrackerCheckSWSH, v => AppState.TrackerCheckSWSH = v);
        var trkO = Tog("홈 트래커 없음 검사 (다른 게임)", AppState.TrackerCheckOther, v => AppState.TrackerCheckOther = v);
        var htf = Tog("'현재 트레이너는 어버이가 될 수 없음' 검사", AppState.HTFlagCheck, v => AppState.HTFlagCheck = v);
        var chkHint = T.L("합법성 검사 항목을 켜고 끕니다. 끈 항목만 문제인 개체는 합법으로 표시됩니다.", 12, sub: true); chkHint.LineBreakMode = LineBreakMode.WordWrap;
        var bk = new Switch { IsToggled = SaveStore.AutoBackup, OnColor = T.Accent }; bk.Toggled += (_, e) => SaveStore.AutoBackup = e.Value;
        var bkRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, HeightRequest = 44 }; bkRow.Add(T.L("자동 백업", 15), 0); bkRow.Add(bk, 1);
        var bkHint = T.L("켜면 세이브를 열 때 원본을, 내보낼 때 저장본을 앱 안에 보관합니다(최근 30개).", 12, sub: true); bkHint.LineBreakMode = LineBreakMode.WordWrap;
        var list = T.Pill("백업 목록 보기"); list.Clicked += (_, _) => { Close(); SheetHost.Show(new SaveListSheet(true)); };
        (var lv, var ll) = T.Chooser(() => { });
        ll.Text = AppState.Languages.FirstOrDefault(x => x.Code == AppState.LangCode).Name ?? AppState.LangCode;
        var tapL = new TapGestureRecognizer();
        tapL.Tapped += (_, _) => SheetHost.Show(new PickerSheet("데이터 언어 (재시작 후 적용)", AppState.Languages.Select((x, i) => new ComboItem(x.Name, i)).ToList(), Array.FindIndex(AppState.Languages, x => x.Code == AppState.LangCode),
            c =>
            {
                Preferences.Set("data_lang", AppState.Languages[c.Value].Code); ll.Text = c.Text;
                Dispatcher.Dispatch(async () =>
                {
                    if (await Application.Current.Windows[0].Page.DisplayAlertAsync("데이터 언어", $"{c.Text}(으)로 바꿨습니다. 지금 다시 불러와 적용할까요?", "지금 적용", "다음 실행 때"))
                    { AppState.ReloadLanguage(); Note.Show($"{c.Text}(으)로 적용했습니다"); }
                    else ll.Text = c.Text + " (다음 실행 때 적용)";
                });
            }));
        lv.GestureRecognizers.Clear(); lv.GestureRecognizers.Add(tapL);
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 12, Children = { T.L("화면 모드", 13, sub: true), row, T.Field("데이터 언어 (종·기술 이름 등)", lv), hcRow, hcHint, trkS, trkO, htf, chkHint, bkRow, bkHint, list } } });
    }
}

// ======================= 리빙덱스 채우기 =======================
public class LivingDexSheet : Sheet
{
    private CancellationTokenSource cts;
    public LivingDexSheet() : base("리빙덱스 채우기", 0.8)
    {
        var sav = AppState.Sav;
        View Sw(string label, bool v, out Switch s) { s = new Switch { IsToggled = v, OnColor = T.Accent }; var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, HeightRequest = 44 }; g.Add(T.L(label, 15), 0); g.Add(s, 1); return g; }
        var shinyRow = Sw("이로치로 만들기 (불가능하면 일반 색)", false, out var shiny);
        var formRow = Sw("모든 폼 포함", false, out var forms);
        var skipRow = Sw("이미 박스에 있는 포켓몬 건너뛰기", true, out var skip);
        var status = T.L("", 13, sub: true); status.LineBreakMode = LineBreakMode.WordWrap;
        var bar = new ProgressBar { ProgressColor = T.Accent, IsVisible = false };
        var go = T.Pill("채우기 시작", primary: true); var stop = T.Pill("중지"); stop.IsVisible = false;
        stop.Clicked += (_, _) => cts?.Cancel();
        go.Clicked += async (_, _) =>
        {
            go.IsEnabled = false; stop.IsVisible = true; bar.IsVisible = true; cts = new CancellationTokenSource(); var tok = cts.Token;
            bool sh = shiny.IsToggled, fm = forms.IsToggled, sk = skip.IsToggled; int startBox = AppState.Box;
            var result = await Task.Run(() =>
            {
                // 대상 목록 (도감 순)
                var have = new HashSet<(ushort, byte)>();
                if (sk) for (int b = 0; b < sav.BoxCount; b++) foreach (var p in sav.GetBoxData(b)) if (p.Species != 0) have.Add((p.Species, fm ? p.Form : (byte)0));
                var targets = new List<(ushort, byte)>();
                for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
                {
                    if (!sav.Personal.IsSpeciesInGame(s)) continue;
                    var fc = fm ? sav.Personal.GetFormEntry(s, 0).FormCount : (byte)1;
                    for (byte f = 0; f < fc; f++)
                    {
                        if (fm && (FormInfo.IsBattleOnlyForm(s, f, sav.Generation) || FormInfo.IsTotemForm(s, f, sav.Context) || !sav.Personal.IsPresentInGame(s, f))) continue;
                        if (!have.Contains((s, f))) targets.Add((s, f));
                    }
                }
                int made = 0, legal = 0, fail = 0, box = startBox, slot = 0;
                for (int i = 0; i < targets.Count && !tok.IsCancellationRequested; i++)
                {
                    // 다음 빈칸
                    while (box < sav.BoxCount && sav.GetBoxSlotAtIndex(box, slot).Species != 0) { if (++slot >= sav.BoxSlotCount) { slot = 0; box++; } }
                    if (box >= sav.BoxCount) break;
                    var (s, f) = targets[i];
                    PKM pk = null;
                    try
                    {
                        var blank = sav.BlankPKM; blank.Species = s; blank.Form = f; blank.SetGender(blank.GetSaneGender());
                        // 빈 개체의 전체 세트(레벨 1·개체값 0·특성 없음)를 그대로 쓰면 ALM이 실패 → 종·폼 줄만 사용
                        var first = ShowdownParsing.GetShowdownText(blank).Split('\n')[0].Trim();
                        first = System.Text.RegularExpressions.Regex.Replace(first, @"\s*\((M|F)\)\s*$", "");
                        pk = sav.GetLegalFromSet(new ShowdownSet(first + (sh ? "\nShiny: Yes" : ""))).Created;
                        if (sh && (!pk.IsShiny || !new LegalityAnalysis(pk).Valid)) pk = sav.GetLegalFromSet(new ShowdownSet(first)).Created;
                    }
                    catch { }
                    if (pk == null || pk.Species != s) { fail++; continue; }
                    sav.SetBoxSlotAtIndex(pk, box, slot); made++;
                    if (new LegalityAnalysis(pk).Valid) legal++;
                    if (++slot >= sav.BoxSlotCount) { slot = 0; box++; }
                    int done = i + 1, total = targets.Count;
                    MainThread.BeginInvokeOnMainThread(() => { bar.Progress = (double)done / total; status.Text = $"{done}/{total} · {GameInfo.Strings.Species[s]}"; });
                }
                return (made, legal, fail, targets.Count, box >= sav.BoxCount);
            });
            AppState.NotifyBox();
            status.Text = $"넣음 {result.made}마리 (합법 {result.legal}) · 실패 {result.fail} · 대상 {result.Item4}{(result.Item5 ? " · 박스가 가득 차서 멈춤" : "")}{(cts.IsCancellationRequested ? " · 중지함" : "")}";
            go.IsEnabled = true; stop.IsVisible = false;
        };
        var hint = T.L($"{AppState.GameName(sav.Version)}에 나오는 포켓몬을 도감 순서대로 자동 합법화(ALM)로 만들어 {AppState.BoxName(AppState.Box)}부터 빈 칸에 채웁니다. 게임과 포켓몬 수에 따라 몇 분 걸릴 수 있습니다.", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, shinyRow, formRow, skipRow, new HorizontalStackLayout { Spacing = 8, Children = { go, stop } }, bar, status } } });
    }
}


/// <summary>즐겨찾기: 만든 포켓몬을 원래 게임 형식 그대로 앱 안에 보관.</summary>
public static class Favorites
{
    public static string Dir { get { var d = Path.Combine(FileSystem.AppDataDirectory, "favorites"); Directory.CreateDirectory(d); return d; } }
    public static void Add(PKM p)
    {
        var data = ShareUtil.Stored(p);
        var name = $"{DateTime.Now:yyyyMMdd_HHmmssfff}_{p.Species:0000}.{p.Extension}";
        File.WriteAllBytes(Path.Combine(Dir, name), data);
    }
    public static List<(string Path, PKM Pk)> All()
    {
        var list = new List<(string, PKM)>();
        foreach (var f in Directory.GetFiles(Dir).OrderBy(x => x))
        {
            try { if (FileUtil.GetSupportedFile(File.ReadAllBytes(f), Path.GetExtension(f), AppState.Sav) is PKM p) list.Add((f, p)); } catch { }
        }
        return list;
    }
}

// ======================= 즐겨찾기 (박스 모양으로 보기) =======================
public class FavoritesSheet : Sheet
{
    public record Cell(string Path, PKM Pk, string Sprite, bool Shiny, string Info);
    private readonly CollectionView grid = new() { SelectionMode = SelectionMode.None };
    private readonly Label status = T.L("", 12, sub: true);
    private int page;

    public FavoritesSheet() : base("즐겨찾기", 0.85)
    {
        var prev = T.Pill("‹", size: 16); prev.WidthRequest = 44; prev.Padding = 0;
        var next = T.Pill("›", size: 16); next.WidthRequest = 44; next.Padding = 0;
        prev.Clicked += (_, _) => { if (page > 0) { page--; Fill(); } };
        next.Clicked += (_, _) => { page++; Fill(); };
        var head = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 6 };
        head.Add(status, 0); head.Add(prev, 1); head.Add(next, 2);
        grid.ItemsLayout = new GridItemsLayout(6, ItemsLayoutOrientation.Vertical) { VerticalItemSpacing = 4, HorizontalItemSpacing = 4 };
        grid.ItemTemplate = new DataTemplate(() =>
        {
            var g = new Grid { HeightRequest = 56 };
            var bg = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
            bg.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F9FAFB"), Color.FromArgb("#22262F"));
            var img = new Image { WidthRequest = 52, HeightRequest = 44 }; img.SetBinding(Image.SourceProperty, "Sprite");
            var star = new Image { Source = "rare_icon.png", WidthRequest = 13, HeightRequest = 13, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Margin = 3 };
            star.SetBinding(IsVisibleProperty, "Shiny");
            var ext = new Label { FontSize = 9, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.End, Margin = new Thickness(3, 0, 0, 1) }; T.Sub(ext); ext.SetBinding(Label.TextProperty, "Info");
            g.Add(bg); g.Add(img); g.Add(star); g.Add(ext);
            var t = new TapGestureRecognizer(); t.Tapped += async (_, _) => { if (g.BindingContext is not Cell c) return; if (c.Pk != null) await OnCell(c); else await AddCurrent(); }; g.GestureRecognizers.Add(t);
            return g;
        });
        grid.HeightRequest = 5 * 60 + 16;
        var hint = T.L("만든 포켓몬을 원래 게임 형식 그대로 보관합니다. 빈 칸을 누르면 편집 중인 포켓몬을 등록하고, 편집기로 불러오면 현재 게임 형식으로 바뀝니다.", 12, sub: true); hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 8, Children = { hint, head, grid } } });
        Fill();
    }

    private void Fill()
    {
        var all = Favorites.All();
        int pages = Math.Max(1, (all.Count + 29) / 30); page = Math.Clamp(page, 0, pages - 1);
        var cells = all.Skip(page * 30).Take(30).Select(x => new Cell(x.Path, x.Pk, AppState.Sprite(x.Pk), x.Pk.IsShiny, x.Pk.Extension.ToUpperInvariant())).ToList();
        while (cells.Count < 30) cells.Add(new Cell(null, null, null, false, ""));
        grid.ItemsSource = cells;
        status.Text = $"{page + 1}/{pages}쪽 · 총 {all.Count}마리";
    }

    private async Task AddCurrent()
    {
        var pk = AppState.Pk;
        if (pk.Species == 0) { Note.Show("편집기에 포켓몬이 없습니다"); return; }
        var name = AppState.SpeciesName(pk);
        if (!await Application.Current.Windows[0].Page.DisplayAlertAsync("즐겨찾기 등록", $"편집 중인 {name}({pk.Extension})을(를) 즐겨찾기에 등록할까요?", "등록", "취소")) return;
        Favorites.Add(pk); Fill(); Note.Show("즐겨찾기에 등록했습니다");
    }

    private async Task OnCell(Cell c)
    {
        var p = Application.Current.Windows[0].Page;
        var name = GameInfo.Strings.Species[c.Pk.Species];
        var pick = await p.DisplayActionSheetAsync($"{name} ({c.Pk.Extension})", "취소", null, "편집기로 불러오기", "현재 박스 빈 칸에 넣기", "파일로 받기", "공유", "즐겨찾기에서 삭제");
        switch (pick)
        {
            case "편집기로 불러오기": Close(); AppState.Load(c.Pk); Note.Show("즐겨찾기에서 불러왔습니다"); break;
            case "현재 박스 빈 칸에 넣기":
                var sav = AppState.Sav; int slot = -1;
                for (int i = 0; i < sav.BoxSlotCount; i++) if (sav.GetBoxSlotAtIndex(AppState.Box, i).Species == 0) { slot = i; break; }
                if (slot < 0) { Note.Show("현재 박스에 빈 칸이 없습니다"); break; }
                var conv = EntityConverter.ConvertToType(c.Pk, sav.PKMType, out _);
                if (conv == null) { Note.Show("이 게임 형식으로 바꿀 수 없습니다"); break; }
                sav.SetBoxSlotAtIndex(conv, AppState.Box, slot); AppState.NotifyBox(); Note.Show($"{slot + 1}번 칸에 넣었습니다"); break;
            case "파일로 받기":
                await using (var ms = new MemoryStream(File.ReadAllBytes(c.Path)))
                { var r = await FileSaver.Default.SaveAsync(c.Pk.FileName, ms, CancellationToken.None); Note.Show(r.IsSuccessful ? $"{c.Pk.FileName} 저장했습니다" : "취소했습니다"); }
                break;
            case "공유": await ShareUtil.ShareBytes(File.ReadAllBytes(c.Path), c.Pk.FileName, c.Pk.FileName); break;
            case "즐겨찾기에서 삭제": File.Delete(c.Path); Fill(); break;
        }
    }
}


// ======================= 세이브 도감 일괄 등록 =======================
public static class DexFill
{
    /// <summary>세이브의 도감 객체(Zukan)를 찾음: sav.Zukan 또는 sav.Blocks.Zukan.</summary>
    public static object FindZukan(SaveFile sav)
    {
        object Get(object o, string n) { try { return o?.GetType().GetProperty(n)?.GetValue(o); } catch { return null; } }
        return Get(sav, "Zukan") ?? Get(Get(sav, "Blocks"), "Zukan");
    }
    private static bool Call(object o, string m, params object[] args)
    {
        var mi = o?.GetType().GetMethods().FirstOrDefault(x => x.Name == m && x.GetParameters().Length == args.Length);
        if (mi == null) return false; mi.Invoke(o, args); return true;
    }

    public static void Apply(int mode, Action<string> done)
    {
        var sav = AppState.Sav;
        try
        {
            // 최신 게임: 도감 객체의 일괄 기능 사용 (폼·성별 기록까지 채움)
            var z = FindZukan(sav);
            bool ok = z != null && mode switch
            {
                0 => Call(z, "SeenAll", false),
                1 => Call(z, "CompleteDex", false),
                _ => Call(z, "CaughtNone") & Call(z, "SeenNone"),
            };
            int n = 0;
            if (!ok)   // 구세대: 종별 기록 (도감 객체의 SetSeen/SetCaught가 있으면 그쪽, 없으면 세이브 공통 기능)
            {
                var zs = z?.GetType().GetMethods().FirstOrDefault(x => x.Name == "SetSeen" && x.GetParameters().Length == 2 && x.GetParameters()[0].ParameterType == typeof(ushort));
                var zc = z?.GetType().GetMethods().FirstOrDefault(x => x.Name == "SetCaught" && x.GetParameters().Length == 2 && x.GetParameters()[0].ParameterType == typeof(ushort));
                for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
                {
                    if (!sav.Personal.IsSpeciesInGame(s)) continue;
                    bool seen = mode != 2, caught = mode == 1;
                    if (zs != null && zc != null) { zs.Invoke(z, new object[] { s, seen }); zc.Invoke(z, new object[] { s, caught }); }
                    else { sav.SetSeen(s, seen); sav.SetCaught(s, caught); }
                    n++;
                }
            }
            bool check = mode == 2 || sav.GetSeen(25) || sav.GetSeen(1) || sav.GetSeen(sav.MaxSpeciesID);
            done(!check ? "이 게임은 도감 일괄 등록을 지원하지 않습니다" : ok ? "도감을 일괄 처리했습니다 · 세이브 내보내기로 저장하세요" : $"{n}종을 처리했습니다 · 세이브 내보내기로 저장하세요");
        }
        catch (Exception ex) { done("이 게임에서는 일괄 등록을 지원하지 않거나 실패했습니다: " + (ex.InnerException ?? ex).Message); }
    }

    public static async Task Run(Page page)
    {
        var sav = AppState.Sav;
        const string seen = "모두 본 것으로", caught = "모두 잡은 것으로 (본 것 포함)", clear = "모두 해제";
        var pick = await page.DisplayActionSheetAsync($"도감 일괄 등록 · {AppState.GameName(sav.Version)}", "취소", null, seen, caught, clear);
        if (pick == null || pick == "취소") return;
        int n = 0;
        try
        {
            for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
            {
                if (!sav.Personal.IsSpeciesInGame(s)) continue;
                if (pick == clear) { sav.SetCaught(s, false); sav.SetSeen(s, false); }
                else { sav.SetSeen(s, true); if (pick == caught) sav.SetCaught(s, true); }
                n++;
            }
            Note.Show($"{n}종을 처리했습니다 · 세이브 내보내기로 저장하세요");
        }
        catch (Exception ex) { await page.DisplayAlertAsync("도감", "이 게임에서는 일괄 등록을 지원하지 않거나 실패했습니다: " + ex.Message, "확인"); }
    }
}


public static class UiFold
{
    /// <summary>머리글을 눌러 접고 펴는 카드 (상태 저장).</summary>
    public static View Card(string title, string key, bool def, View inner)
    {
        bool open = Preferences.Get(key, def);
        var head = T.L((open ? "▾  " : "▸  ") + title, 15, bold: true);
        inner.IsVisible = open;
        var t = new TapGestureRecognizer();
        t.Tapped += (_, _) => { open = !open; Preferences.Set(key, open); inner.IsVisible = open; head.Text = (open ? "▾  " : "▸  ") + title; };
        head.GestureRecognizers.Add(t);
        return T.Card(new VerticalStackLayout { Spacing = 10, Children = { head, inner } });
    }
}
