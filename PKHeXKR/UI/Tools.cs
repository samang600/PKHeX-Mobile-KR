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
    /// <summary>
    /// 1.4.4 이하 버그로 복호화된 채 저장된 스위치 세이브(SV·소드실드·Z-A 등 main) 복구.
    /// PKHeX는 main을 읽으며 고정 XOR 패드로 제자리 복호화함 → 같은 패드를 한 번 더 씌우면 원본과 똑같아짐 (끝의 SHA-256 해시로 확인).
    /// </summary>
    public static byte[] TryRepair(byte[] data)
    {
        try
        {
            if (data == null || data.Length < 0x1000 || SwishCrypto.GetIsHashValid(data)) return null;
            var c = data.ToArray();
            SwishCrypto.CryptStaticXorpadBytes(c.AsSpan(0, c.Length - 0x20));
            return SwishCrypto.GetIsHashValid(c) ? c : null;
        }
        catch { return null; }
    }
    /// <summary>보관된 사본 읽기: 복호화된 채 저장된 파일이면 복구해서 파일도 고쳐 둠.</summary>
    public static byte[] ReadStored(string path)
    {
        var b = File.ReadAllBytes(path);
        if (TryRepair(b) is { } f) { try { File.WriteAllBytes(path, f); } catch { } return f; }
        return b;
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
                if (pick == "열기") { Close(); await MainPage.Instance.OpenBytes(SaveStore.ReadStored(it.Path), it.Name, remember: false); }
                else if (pick == "공유") await ShareUtil.ShareBytes(SaveStore.ReadStored(it.Path), it.Name, it.Name);
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
        var swf = Tog("스위치판 FRLG 기준 검사 (FRLG 단독으로 얻을 수 없는 포켓몬 불법)", SwitchFrlg.Enabled, v => SwitchFrlg.Enabled = v);
        var swfHint = T.L("파이어레드·리프그린 세이브에만 적용. 루비·사파이어·에메랄드·콜로세움/XD 출신, 옛 배포, 배꼽바위·탄생의섬, FRLG에 나오지 않는 호연 포켓몬 등을 불법으로 봅니다. 끄면 GBA판 기준(교환·배포 허용).", 12, sub: true); swfHint.LineBreakMode = LineBreakMode.WordWrap;
        var chkHint = T.L("합법성 검사 항목을 켜고 끕니다. 끈 항목만 문제인 개체는 합법으로 표시됩니다.", 12, sub: true); chkHint.LineBreakMode = LineBreakMode.WordWrap;
        var upd = Tog("업데이트 알림 (GitHub 새 버전 확인)", UpdateCheck.Enabled, v => UpdateCheck.Enabled = v);
        var keep = Tog("마지막 세이브 그대로 열기 (앱을 끌 때 편집 상태 보존)", LastSession.Enabled, v => { LastSession.Enabled = v; if (!v) LastSession.Clear(); });
        var updNow = T.Pill("지금 업데이트 확인"); updNow.Clicked += async (_, _) => await UpdateCheck.Run(true);
        var mg = Tog("배포 데이터 자동 업데이트 (하루 한 번)", EventData.Enabled, v => EventData.Enabled = v);
        string MgInfo() => $"앱에 들어 있지 않은 새 배포 카드를 PKHeX 최신 배포 DB에서 받아 합법성 검사·자동 합법화·인카운터 검색에 씁니다. 받은 배포: {EventData.Count}개" + (EventData.LastCheck > DateTime.MinValue ? $" · 마지막 확인 {EventData.LastCheck:yyyy-MM-dd HH:mm}" : "");
        var mgHint = T.L(MgInfo(), 12, sub: true); mgHint.LineBreakMode = LineBreakMode.WordWrap;
        var mgNow = T.Pill("배포 데이터 지금 업데이트");
        mgNow.Clicked += async (_, _) => { mgNow.IsEnabled = false; mgNow.Text = "확인 중…"; var m = await Task.Run(() => EventData.Run(true)); mgNow.Text = "배포 데이터 지금 업데이트"; mgNow.IsEnabled = true; mgHint.Text = MgInfo(); Note.Show(m); };
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
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 12, Children = { T.L("화면 모드", 13, sub: true), row, T.Field("데이터 언어 (종·기술 이름 등)", lv), hcRow, hcHint, trkS, trkO, htf, swf, swfHint, chkHint, upd, updNow, mg, mgHint, mgNow, keep, bkRow, bkHint, list } } });
    }
}

// ======================= 리빙덱스 채우기 =======================
public class LivingDexSheet : Sheet
{
    /// <summary>우두머리 조우(진화 전 포켓몬 포함)로 합법 개체 만들기. 없으면 null.</summary>
    private static PKM DexAlpha(SaveFile sav, ushort species, byte form, bool shiny)
    {
        try
        {
            var b = sav.BlankPKM; b.Species = species; b.Form = form; b.SetGender(b.GetSaneGender());
            var encs = EncounterMovesetGenerator.GenerateEncounters(b, sav, ReadOnlyMemory<ushort>.Empty, GameUtil.GetVersionsWithinRange(b, b.Context).ToArray())
                .Where(e => e is IAlphaReadOnly { IsAlpha: true } && e.Context == sav.Context).OrderBy(e => e.Species == species ? 0 : 1).Take(6).ToList();
            if (shiny)   // 이로치: 무작위 생성으로는 거의 안 나옴 → 조우의 시드를 돌려 이로치가 나오는 시드를 찾음 (PLA·Z-A 시드 방식)
                foreach (var e in encs)
                {
                    Seeder sd = null; try { sd = Seeder.For(e, sav); } catch { }
                    if (sd == null) continue;
                    ulong seed = (ulong)Random.Shared.NextInt64();
                    for (int n = 0; n < 300_000; n++, seed = seed * 6364136223846793005UL + 1442695040888963407UL)
                    {
                        var p = sd.Template.Clone();
                        try { if (!sd.Gen(p, seed)) continue; } catch { break; }
                        if (!p.IsShiny) continue;
                        try { p = sd.Finish(p) ?? p; } catch { }
                        p = EntityConverter.ConvertToType(p, sav.PKMType, out _) ?? p;
                        if (p.Species != species) p = EvoUtil.Evolve(p, species, form);
                        if (p.Species == species && p.IsShiny && p is IAlpha { IsAlpha: true } && new LegalityAnalysis(p).Valid) return p;
                        break;
                    }
                }
            foreach (var e in encs)
                foreach (var wantShiny in shiny ? new[] { true, false } : new[] { false })
                {
                    var crit = EncounterCriteria.Unrestricted with { Shiny = wantShiny ? Shiny.Always : Shiny.Random };
                    for (int t = 0; t < 3; t++)
                    {
                        PKM p; try { p = e.ConvertToPKM(sav, crit); } catch { break; }
                        p = EntityConverter.ConvertToType(p, sav.PKMType, out _) ?? p;
                        if (wantShiny && !p.IsShiny) continue;
                        if (p.Species != species) p = EvoUtil.Evolve(p, species, form);
                        if (p.Species == species && p is IAlpha { IsAlpha: true } && new LegalityAnalysis(p).Valid) return p;
                    }
                }
        }
        catch { }
        return null;
    }

    private CancellationTokenSource cts;
    public LivingDexSheet() : base("리빙덱스 채우기", 0.8)
    {
        var sav = AppState.Sav;
        View Sw(string label, bool v, out Switch s) { s = new Switch { IsToggled = v, OnColor = T.Accent }; var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, HeightRequest = 44 }; g.Add(T.L(label, 15), 0); g.Add(s, 1); return g; }
        var shinyRow = Sw("이로치로 만들기 (불가능하면 일반 색)", false, out var shiny);
        var formRow = Sw("모든 폼 포함", false, out var forms);
        var skipRow = Sw("이미 박스에 있는 포켓몬 건너뛰기", true, out var skip);
        bool alphaGame = sav is SAV8LA or SAV9ZA;
        var alphaRow = Sw("우두머리로 만들기 (우두머리 조우가 없으면 일반)", false, out var alpha); alphaRow.IsVisible = alphaGame;
        var status = T.L("", 13, sub: true); status.LineBreakMode = LineBreakMode.WordWrap;
        var bar = new ProgressBar { ProgressColor = T.Accent, IsVisible = false };
        var dFrom = T.Input(placeholder: "예: 2025-10-16"); var dTo = T.Input(placeholder: "예: 2026-09-30");
        var dateRow = T.Cols(2, 6); dateRow.Add(T.Field("만난 날짜 시작 (비우면 그대로)", dFrom), 0); dateRow.Add(T.Field("만난 날짜 끝", dTo), 1);
        var ots = new Editor { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 70, FontSize = 14, Placeholder = "어버이 여러 명 (한 줄에 '이름 TID'), 비우면 세이브 트레이너\n예) 새아 468686\n하늘 123456" };
        ots.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
        var go = T.Pill("채우기 시작", primary: true); var stop = T.Pill("중지"); stop.IsVisible = false;
        stop.Clicked += (_, _) => cts?.Cancel();
        go.Clicked += async (_, _) =>
        {
            go.IsEnabled = false; stop.IsVisible = true; bar.IsVisible = true; cts = new CancellationTokenSource(); var tok = cts.Token;
            bool sh = shiny.IsToggled, fm = forms.IsToggled, sk = skip.IsToggled, al = alphaGame && alpha.IsToggled; int startBox = AppState.Box; int alphaMade = 0;
            DateOnly? from = DateOnly.TryParse(dFrom.Text, out var df) ? df : null, to = DateOnly.TryParse(dTo.Text, out var dt) ? dt : null;
            if (from != null && to == null) to = from; if (to != null && from == null) from = to;
            if (from > to) (from, to) = (to, from);
            var otList = (ots.Text ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(a => a.Length >= 1)
                .Select(a => (Name: a[0], TID: a.Length > 1 && int.TryParse(a[1], out var tid) ? tid : -1)).ToList();
            int nth = 0;
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
                    if (al) { pk = DexAlpha(sav, s, f, sh); if (pk != null) alphaMade++; }   // 우두머리 조우(진화 전 포함)로 만들기
                    if (pk == null) try
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
                    // 어버이 여러 명이면 돌아가며, 날짜 구간이면 그 안의 무작위 날짜 (합법이 유지될 때만)
                    if (otList.Count > 0)
                    {
                        var (on, otid) = otList[nth++ % otList.Count];
                        bool six = pk.Format >= 7; int tid = otid >= 0 ? otid : (six ? (int)(sav.ID32 % 1_000_000) : sav.TID16);
                        uint id32 = six ? (uint)(Random.Shared.Next(4295) * 1_000_000L + Math.Min(tid, 999_999)) : (uint)((Random.Shared.Next(65536) << 16) | Math.Min(tid, 65535));
                        var t2 = pk.Clone(); AppState.AutoOT(t2, new AppState.Partner(on, id32, six, sav.Gender, sav.Language > 0 ? sav.Language : pk.Language));
                        if (new LegalityAnalysis(t2).Valid || !new LegalityAnalysis(pk).Valid) pk = t2;
                    }
                    if (from != null && pk.Format >= 4)
                    {
                        var span = to.Value.DayNumber - from.Value.DayNumber;
                        var t3 = pk.Clone(); t3.MetDate = from.Value.AddDays(Random.Shared.Next(span + 1)); t3.RefreshChecksum();
                        if (new LegalityAnalysis(t3).Valid || !new LegalityAnalysis(pk).Valid) pk = t3;
                    }
                    sav.SetBoxSlotAtIndex(pk, box, slot); made++;
                    if (new LegalityAnalysis(pk).Valid) legal++;
                    if (++slot >= sav.BoxSlotCount) { slot = 0; box++; }
                    int done = i + 1, total = targets.Count;
                    MainThread.BeginInvokeOnMainThread(() => { bar.Progress = (double)done / total; status.Text = $"{done}/{total} · {GameInfo.Strings.Species[s]}"; });
                }
                return (made, legal, fail, targets.Count, box >= sav.BoxCount);
            });
            AppState.NotifyBox();
            status.Text = $"넣음 {result.made}마리 (합법 {result.legal}{(alphaGame && alpha.IsToggled ? $", 우두머리 {alphaMade}" : "")}) · 실패 {result.fail} · 대상 {result.Item4}{(result.Item5 ? " · 박스가 가득 차서 멈춤" : "")}{(cts.IsCancellationRequested ? " · 중지함" : "")}";
            go.IsEnabled = true; stop.IsVisible = false;
        };
        var hint = T.L($"{AppState.GameName(sav.Version)}에 나오는 포켓몬을 도감 순서대로 자동 합법화(ALM)로 만들어 {AppState.BoxName(AppState.Box)}부터 빈 칸에 채웁니다. 게임과 포켓몬 수에 따라 몇 분 걸릴 수 있습니다.", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, shinyRow, formRow, skipRow, alphaRow, dateRow, ots, new HorizontalStackLayout { Spacing = 8, Children = { go, stop } }, bar, status } } });
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


/// <summary>압축(zip) 세이브: 안의 세이브 파일 꺼내기 / 원래 구성 그대로 세이브만 바꿔 다시 묶기 (DBI 백업 등).</summary>
public static class ZipSave
{
    private static byte[] Read(System.IO.Compression.ZipArchiveEntry e) { using var r = e.Open(); using var ms = new MemoryStream(); r.CopyTo(ms); return ms.ToArray(); }
    private static bool IsSave(byte[] b, string name) { try { return FileUtil.GetSupportedFile(b.ToArray(), Path.GetExtension(name), AppState.Sav) is SaveFile; } catch { return false; } }   // 사본으로 검사 (읽기만 해도 바이트가 바뀌는 세이브가 있음)

    /// <summary>세이브 파일 찾기: 이름이 main인 것 → 없으면 PKHeX가 세이브로 알아보는 가장 큰 파일.</summary>
    public static (string Entry, byte[] Data)? Extract(byte[] zip)
    {
        try
        {
            using var za = new System.IO.Compression.ZipArchive(new MemoryStream(zip), System.IO.Compression.ZipArchiveMode.Read);
            var files = za.Entries.Where(e => e.Length > 0 && !e.FullName.EndsWith('/')).ToList();
            var main = files.FirstOrDefault(e => e.Name.Equals("main", StringComparison.OrdinalIgnoreCase));
            if (main != null) return (main.FullName, Read(main));
            foreach (var e in files.OrderByDescending(e => e.Length)) { var b = Read(e); if (IsSave(b, e.Name)) return (e.FullName, b); }
        }
        catch { }
        return null;
    }

    /// <summary>원래 zip을 같은 순서·경로·시간·압축 방식으로 다시 묶되, entry 하나만 새 내용으로.</summary>
    public static byte[] Replace(byte[] zip, string entry, byte[] data)
    {
        using var src = new System.IO.Compression.ZipArchive(new MemoryStream(zip), System.IO.Compression.ZipArchiveMode.Read);
        // DBI·JKSV 백업의 "backup"은 main과 같은 사본 → 원래 main과 같았으면 함께 바꿔 둘을 일치시킴
        string syncBackup = null;
        try
        {
            var me = src.GetEntry(entry);
            var be = src.Entries.FirstOrDefault(e => e.Name.Equals("backup", StringComparison.OrdinalIgnoreCase) && e.FullName != entry);
            if (me != null && be != null && be.Length == me.Length && Read(be).AsSpan().SequenceEqual(Read(me))) syncBackup = be.FullName;
        }
        catch { }
        var outMs = new MemoryStream();
        using (var dst = new System.IO.Compression.ZipArchive(outMs, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var e in src.Entries)
            {
                bool stored = e.CompressedLength == e.Length;
                var ne = dst.CreateEntry(e.FullName, stored ? System.IO.Compression.CompressionLevel.NoCompression : System.IO.Compression.CompressionLevel.Optimal);
                ne.LastWriteTime = e.LastWriteTime;
                try { ne.ExternalAttributes = e.ExternalAttributes; } catch { }   // 원본 속성 그대로 (DBI: 폴더 0x10, 파일 0)
                if (e.FullName.EndsWith('/')) continue;
                using var w = ne.Open();
                if (e.FullName == entry || e.FullName == syncBackup) w.Write(data); else { using var r = e.Open(); r.CopyTo(w); }
            }
        }
        return outMs.ToArray();
    }
}

/// <summary>GitHub 릴리스로 새 버전 확인 (하루 한 번, 설정에서 끄기·지금 확인).</summary>
public static class UpdateCheck
{
    public const string Repo = "samang600/PKHeX-Mobile-KR";
    public static bool Enabled { get => Preferences.Get("upd_on", true); set => Preferences.Set("upd_on", value); }
    private static Version Parse(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
        return m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0) : null;
    }
    public static async Task Run(bool manual)
    {
        try
        {
            if (!manual)
            {
                if (!Enabled) return;
                var last = Preferences.Get("upd_last", DateTime.MinValue);
                if ((DateTime.Now - last).TotalHours < 20) return;
            }
            Preferences.Set("upd_last", DateTime.Now);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PKHeX-Mobile-KR");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : "", name = root.TryGetProperty("name", out var n) ? n.GetString() : "";
            string url = root.TryGetProperty("html_url", out var u) ? u.GetString() : $"https://github.com/{Repo}/releases";
            string body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            var latest = Parse(tag) ?? Parse(name); var mine = Parse(AppInfo.Current.VersionString);
            var page = Application.Current?.Windows[0].Page;
            if (latest == null || mine == null || page == null) { if (manual) Note.Show("최신 버전 정보를 읽지 못했습니다 (릴리스 태그를 v1.4.0 형식으로)"); return; }
            if (latest <= mine) { if (manual) Note.Show($"최신 버전입니다 (v{mine.ToString(3)})"); return; }
            if (!manual && Preferences.Get("upd_skip", "") == latest.ToString()) return;
            var excerpt = body.Length > 300 ? body[..300] + "…" : body;
            var pick = await page.DisplayActionSheetAsync($"새 버전 v{latest.ToString(3)}이 있습니다 (지금 v{mine.ToString(3)})\n\n{excerpt}", "나중에", null, "릴리스 페이지 열기", "이 버전 건너뛰기");
            if (pick == "릴리스 페이지 열기") await Browser.Default.OpenAsync(url, BrowserLaunchMode.External);
            else if (pick == "이 버전 건너뛰기") Preferences.Set("upd_skip", latest.ToString());
        }
        catch { if (manual) Note.Show("업데이트를 확인하지 못했습니다 (인터넷 연결 확인)"); }
    }
}

/// <summary>플레이어 의상·꾸미기 한 번에 얻기 (PKHeX가 지원하는 게임만).</summary>
public static class Cosmetics
{
    public static string UnlockAll(SaveFile sav)
    {
        var done = new List<string>();
        object Get(object o, string p) { try { return o?.GetType().GetProperty(p)?.GetValue(o); } catch { return null; } }
        var owners = new List<object> { sav }; if (Get(sav, "Blocks") is { } blocks) owners.Add(blocks);
        foreach (var o in owners)
            foreach (var pr in o.GetType().GetProperties().Where(p => p.PropertyType.Name.Contains("Fashion") || p.Name.Contains("Fashion")))
            {
                object f; try { f = pr.GetValue(o); } catch { continue; }
                if (f == null) continue;
                foreach (var m in new[] { "UnlockAllLegal", "UnlockAllAccessoriesPlayer", "UnlockAllAccessories", "UnlockAll" })
                {
                    var mi = f.GetType().GetMethod(m, Type.EmptyTypes);
                    if (mi == null) continue;
                    try { mi.Invoke(f, null); done.Add("의상"); } catch { }
                    break;
                }
            }
        foreach (var (m, label) in new[] { ("UnlockAllThrowStyles", "던지기 동작") })
        {
            var mi = sav.GetType().GetMethod(m, Type.EmptyTypes);
            if (mi != null) try { mi.Invoke(sav, null); done.Add(label); } catch { }
        }
        if (done.Count == 0) return null;
        sav.State.Edited = true;
        return string.Join(", ", done.Distinct());
    }
}

// ======================= 이상한 소포 받기함 =======================
/// <summary>세이브의 이상한 소포(배포 카드) 받기함: 카드 보기·넣기(파일/배포 목록)·내보내기·비우기, 받은 기록 초기화. PKHeX 지원: 4~7세대·레츠고.</summary>
public class MysteryGiftSheet : Sheet
{
    private readonly VerticalStackLayout list = new() { Spacing = 4 };
    private readonly Label status = T.L("", 13, sub: true);
    public static bool Supported(SaveFile s) => s is IMysteryGiftStorageProvider;

    public MysteryGiftSheet() : base("이상한 소포 받기함")
    {
        status.LineBreakMode = LineBreakMode.WordWrap;
        var hint = T.L("카드를 누르면 넣기(파일·배포 목록)·내보내기·비우기를 할 수 있습니다. 바꾼 뒤에는 세이브 내보내기로 저장하세요. 소드실드·SV·Z-A 등 8세대 이후는 PKHeX가 받기함 편집을 지원하지 않습니다.", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        var clearFlags = T.Pill("받은 기록 모두 지우기");
        clearFlags.IsVisible = AppState.Sav is IMysteryGiftFlags;
        clearFlags.Clicked += async (_, _) =>
        {
            if (AppState.Sav is not IMysteryGiftFlags f) return;
            if (!await Application.Current.Windows[0].Page.DisplayAlertAsync("받은 기록", "이 세이브의 '이미 받은 배포' 기록을 모두 지웁니다. 같은 배포를 다시 받을 수 있게 됩니다.", "지우기", "취소")) return;
            f.ClearReceivedFlags(); AppState.Sav.State.Edited = true; Note.Show("받은 기록을 지웠습니다");
        };
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, clearFlags, status, list } } });
        Fill();
    }

    private static IMysteryGiftStorage Store => (AppState.Sav as IMysteryGiftStorageProvider)?.MysteryGiftStorage;

    private void Fill()
    {
        list.Children.Clear();
        var st = Store;
        if (st == null) { status.Text = "이 게임은 이상한 소포 받기함 편집을 지원하지 않습니다"; return; }
        int used = 0;
        for (int i = 0; i < st.GiftCountMax; i++)
        {
            DataMysteryGift g; try { g = st.GetMysteryGift(i); } catch { continue; }
            int k = i;
            bool empty = g == null || g.IsEmpty;
            if (!empty) used++;
            var title = empty ? "(비어 있음)" : $"{g.CardTitle}".Trim();
            var sub = empty ? $"{k + 1}번 칸" : $"{k + 1}번 칸 · 카드 {g.CardID} · {(g.IsEntity ? GameInfo.Strings.Species[g.Species] : g.IsItem ? "도구" : "기타")}";
            var t1 = T.L(title.Length == 0 ? "(제목 없음)" : title, 14, bold: !empty); t1.LineBreakMode = LineBreakMode.WordWrap;
            var img = new Image { WidthRequest = 40, HeightRequest = 34, Source = !empty && g.IsEntity ? AppState.Sprite(g.Species, g.Form, g.IsShiny) : null };
            var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 8, Padding = new Thickness(8, 6) };
            row.Add(img, 0); row.Add(new VerticalStackLayout { Spacing = 1, Children = { t1, T.L(sub, 11, sub: true) } }, 1);
            var b = new Border { Content = row, StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
            b.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F9FAFB"), Color.FromArgb("#22262F"));
            var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => await OnSlot(k, g, empty); b.GestureRecognizers.Add(tap);
            list.Children.Add(b);
        }
        status.Text = $"{used}/{st.GiftCountMax}칸 사용 중";
    }

    private static IEnumerable<DataMysteryGift> Database(SaveFile s) => s switch
    {
        SAV4 => EncounterEvent.MGDB_G4, SAV5 => EncounterEvent.MGDB_G5, SAV6 => EncounterEvent.MGDB_G6,
        SAV7b => EncounterEvent.MGDB_G7GG, SAV7 => EncounterEvent.MGDB_G7, _ => [],
    };

    private async Task OnSlot(int index, DataMysteryGift cur, bool empty)
    {
        var page = Application.Current.Windows[0].Page; var st = Store; var sav = AppState.Sav;
        const string file = "카드 파일 넣기", db = "배포 목록에서 넣기", export = "파일로 내보내기", clear = "비우기";
        var pick = empty ? await page.DisplayActionSheetAsync($"{index + 1}번 칸", "취소", null, db, file)
                         : await page.DisplayActionSheetAsync(cur.CardTitle, "취소", clear, db, file, export);
        try
        {
            switch (pick)
            {
                case file:
                    var f = await FilePicker.PickAsync(); if (f == null) return;
                    byte[] data; await using (var s = await f.OpenReadAsync()) { using var ms = new MemoryStream(); await s.CopyToAsync(ms); data = ms.ToArray(); }
                    var g = MysteryGift.GetMysteryGift(data, Path.GetExtension(f.FileName));
                    if (g == null) { Note.Show("배포 카드 파일이 아닙니다"); return; }
                    Put(index, g); break;
                case db:
                    var all = Database(sav).Where(x => cur == null || x.GetType() == cur.GetType()).Reverse().ToList();
                    if (all.Count == 0) { Note.Show("이 게임에 맞는 배포 목록이 없습니다"); return; }
                    var items = all.Select((x, i) => new ComboItem($"{x.CardID} · {x.CardTitle} · {(x.IsEntity ? GameInfo.Strings.Species[x.Species] : x.IsItem ? "도구" : "기타")}", i)).ToList();
                    SheetHost.Show(new PickerSheet("배포 카드 (최신순)", items, -1, c => Put(index, all[c.Value]),
                        c => all[c.Value].IsEntity ? AppState.Sprite(all[c.Value].Species, all[c.Value].Form, all[c.Value].IsShiny) : null));
                    break;
                case export:
                    await using (var ms = new MemoryStream(cur.Write().ToArray()))
                    { var r = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync($"{cur.CardID:0000} - {cur.CardTitle}.{cur.Extension}", ms, CancellationToken.None); Note.Show(r.IsSuccessful ? "저장했습니다" : "취소했습니다"); }
                    break;
                case clear:
                    var blank = (DataMysteryGift)Activator.CreateInstance(cur.GetType());
                    st.SetMysteryGift(index, blank); sav.State.Edited = true; Fill(); Note.Show("칸을 비웠습니다"); break;
            }
        }
        catch (Exception ex) { status.Text = "오류: " + ex.Message; }
    }

    private void Put(int index, DataMysteryGift g)
    {
        try
        {
            var st = Store; var cur = st.GetMysteryGift(index);
            if (cur != null && cur.GetType() != g.GetType()) { Note.Show($"이 칸에는 {cur.GetType().Name} 형식만 넣을 수 있습니다 (고른 카드: {g.GetType().Name})"); return; }
            st.SetMysteryGift(index, (DataMysteryGift)g.Clone()); AppState.Sav.State.Edited = true;
            Fill(); Note.Show($"{index + 1}번 칸에 넣었습니다: {g.CardTitle}");
        }
        catch (Exception ex) { Note.Show("넣지 못했습니다: " + ex.Message); }
    }
}


/// <summary>
/// 마지막 세이브 보존: 앱이 백그라운드로 가거나 꺼질 때 지금 열린 세이브(편집 내용 포함)를 앱 안에 저장하고,
/// 다음 실행 때 그대로 엽니다. 원본 파일은 건드리지 않음 (내보내기 전까지 원본은 그대로).
/// </summary>
public static class LastSession
{
    public static bool Enabled { get => Preferences.Get("keep_last", false); set => Preferences.Set("keep_last", value); }
    private static string Dir => Path.Combine(FileSystem.AppDataDirectory, "last_session");
    public static void Clear() { try { if (Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { } }
    public static void Save()
    {
        if (!Enabled) return;
        try
        {
            var sav = AppState.Sav; if (sav == null || string.IsNullOrEmpty(sav.Metadata.FileName)) return;
            Directory.CreateDirectory(Dir);
            var data = sav.Write(sav.Metadata.GetSuggestedFlags(sav.Metadata.GetSuggestedExtension())).ToArray();
            File.WriteAllBytes(Path.Combine(Dir, "save.bin"), data);
            File.WriteAllText(Path.Combine(Dir, "name.txt"), sav.Metadata.FileName + "\n" + AppState.Box);
        }
        catch { }
    }
    public static async Task<bool> Restore()
    {
        if (!Enabled) return false;
        try
        {
            var f = Path.Combine(Dir, "save.bin"); if (!File.Exists(f)) return false;
            var lines = File.ReadAllLines(Path.Combine(Dir, "name.txt"));
            await MainPage.Instance.OpenBytes(File.ReadAllBytes(f), lines[0], remember: false);
            if (lines.Length > 1 && int.TryParse(lines[1], out var b) && b < AppState.Sav.BoxCount) { AppState.Box = b; AppState.NotifyBox(); }
            Note.Show("마지막 세이브를 그대로 열었습니다 (내보내기 전까지 원본 파일은 그대로)");
            return true;
        }
        catch { return false; }
    }
}
