using PKHeX.Core;
using PKHeX.Core.AutoMod;
using PKHeX.Core.Injection;
using System.Runtime.CompilerServices;
namespace PKHeXKR;

// ======================= 쇼다운 =======================
public class ShowdownSheet : Sheet
{
    private readonly Editor text = new() { AutoSize = EditorAutoSizeOption.Disabled, FontSize = 14, FontFamily = "monospace", Placeholder = "쇼다운 세트를 붙여넣으세요\n\nPikachu @ Light Ball\nAbility: Static\n..." };
    public ShowdownSheet() : base("쇼다운 세트")
    {
        text.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
        if (AppState.Pk.Species != 0) text.Text = ShowdownParsing.GetShowdownText(AppState.Pk);
        var paste = T.Pill("붙여넣기"); paste.Clicked += async (_, _) => { if (Clipboard.HasText) text.Text = await Clipboard.GetTextAsync(); };
        var copy = T.Pill("복사"); copy.Clicked += async (_, _) => { await Clipboard.SetTextAsync(text.Text ?? ""); Note.Show("복사했습니다"); };
        var box = T.Pill("현재 박스 전체"); box.Clicked += (_, _) => text.Text = ShowdownParsing.GetShowdownSets(AppState.CurrentSlots().Where(p => p.Species != 0), Environment.NewLine + Environment.NewLine);
        var make = T.Pill("합법 개체로 만들기", primary: true);
        make.Clicked += async (_, _) => await Make();
        var team = T.Pill("팀 전부 박스에 넣기", primary: true);
        team.Clicked += async (_, _) => await ImportTeam();
        var btns = new VerticalStackLayout { Spacing = 6, Children = {
            new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 6, Children = { paste, copy, box } } },
            new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 6, Children = { make, team } } } } };
        var frame = new Border { Content = text, StrokeThickness = 1, Padding = 6, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
        frame.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D1D5DB"), Color.FromArgb("#374151"));
        Body.RowDefinitions.Add(new(GridLength.Star)); Body.RowDefinitions.Add(new(GridLength.Auto));
        Body.Add(frame, 0, 0); Body.Add(btns, 0, 1);
    }

    /// <summary>세트 여러 개를 각각 합법화해 현재 박스부터 빈 칸에 차례로 넣음.</summary>
    private async Task ImportTeam()
    {
        var raw = (text.Text ?? "").Trim(); if (raw.Length == 0) return;
        var sets = ShowdownParsing.GetShowdownSets(raw).Where(x => x.Species != 0).ToList();
        if (sets.Count == 0) { Note.Show("세트를 찾지 못했습니다 (영어 쇼다운 형식)"); return; }
        var sav = AppState.Sav; int start = AppState.Box;
        var res = await Task.Run(() =>
        {
            int put = 0, legal = 0, box = start, slot = 0;
            foreach (var set in sets)
            {
                while (box < sav.BoxCount && sav.GetBoxSlotAtIndex(box, slot).Species != 0) { if (++slot >= sav.BoxSlotCount) { slot = 0; box++; } }
                if (box >= sav.BoxCount) break;
                PKM pk; try { pk = sav.GetLegalFromSet(set).Created; } catch { continue; }
                sav.SetBoxSlotAtIndex(pk, box, slot); put++; if (new LegalityAnalysis(pk).Valid) legal++;
            }
            return (put, legal);
        });
        Close(); AppState.NotifyBox();
        Note.Show($"{sets.Count}개 중 {res.put}마리를 넣었습니다 (합법 {res.legal})");
    }

    private async Task Make()
    {
        var page = Application.Current.Windows[0].Page;
        var raw = (text.Text ?? "").Trim();
        if (raw.Length == 0) return;
        try
        {
            var set = new ShowdownSet(raw);
            if (set.Species == 0) { await page.DisplayAlertAsync("쇼다운", "세트를 해석하지 못했습니다. 영어 쇼다운 형식인지 확인하세요.", "확인"); return; }
            var result = await Task.Run(() => AppState.Sav.GetLegalFromSet(set));
            var pkm = result.Created;
            Close();
            AppState.Load(pkm);
            bool ok = new LegalityAnalysis(pkm).Valid;
            Note.Show(ok ? "합법 개체를 만들었습니다" : "합법으로 만들지 못했습니다 (합법성 표시 확인)");
        }
        catch (Exception ex) { await page.DisplayAlertAsync("쇼다운", ex.Message, "확인"); }
    }
}

// ======================= 합법성 =======================
public class LegalitySheet : Sheet
{
    public LegalitySheet() : base("합법성 검사", 0.7)
    {
        var la = AppState.Analyze(AppState.Pk, out var waived);
        bool ev = AppState.EffectiveValid(la);
        var badge = T.L(la.Valid ? (waived ? "✔ 합법 (리본용 홈 트래커 생략 허용)" : "✔ 합법") : ev ? "✔ 합법 (끈 검사 항목만 해당)" : "✖ 불법", 20, bold: true);
        badge.LineBreakMode = LineBreakMode.WordWrap;
        badge.TextColor = ev ? T.Good : T.Bad;
        string report;
        try { report = la.Report(AppState.LangCode, true); } catch { report = la.Report(true); }
        if (AppState.LangCode == "ko") report = KoFix.Fix(report);
        var body = T.L(report, 13); body.LineBreakMode = LineBreakMode.WordWrap;
        var fix = T.Pill("자동 합법화 (ALM)", primary: true);
        fix.IsVisible = !la.Valid;
        fix.Clicked += async (_, _) => { Close(); await MainPage.Legalize(); };
        Body.RowDefinitions.Add(new(GridLength.Auto)); Body.RowDefinitions.Add(new(GridLength.Star)); Body.RowDefinitions.Add(new(GridLength.Auto));
        Body.Add(badge, 0, 0); Body.Add(new ScrollView { Content = body }, 0, 1); Body.Add(fix, 0, 2);
    }
}

// ======================= 라이브헥스 =======================
public class LiveHexSheet : Sheet
{
    private readonly Entry ip = T.Input(Keyboard.Url, "192.168.0.10"), port = T.Input(Keyboard.Numeric);
    private readonly Label state = T.L("", 14, bold: true);
    private readonly Button connect = T.Pill("연결", primary: true);
    private readonly Switch readOnChange = new() { OnColor = T.Accent }, injectOnSave = new() { OnColor = T.Accent };

    public LiveHexSheet() : base("라이브헥스", 0.75)
    {
        bool sw = RamOffsets.IsSwitchTitle(AppState.Sav);
        // 스위치·3DS는 IP·포트를 따로 기억 (3DS NTR 기본 포트 8000)
        ip.Text = Preferences.Get(sw ? "live_ip" : "live_ip_3ds", ""); port.Text = Preferences.Get(sw ? "live_port" : "live_port_3ds", sw ? "6000" : "8000");
        readOnChange.IsToggled = Preferences.Get("live_read_on_change", true);
        readOnChange.Toggled += (_, e) => Preferences.Set("live_read_on_change", e.Value);
        connect.Clicked += async (_, _) => await Toggle();
        var readBox = T.Pill("현재 박스 읽어오기"); readBox.Clicked += (_, _) => { if (!AppState.LiveConnected) return; BoxPanel.ReadLiveBox(); AppState.NotifyBox(); Note.Show("게임에서 박스를 읽었습니다"); };
        var hint = T.L((sw ? "스위치: sys-botbase가 실행 중이어야 합니다 (기본 포트 6000)." : "3DS: Luma3DS에서 BootNTR Selector로 NTR을 켠 뒤 게임을 실행하고, 3DS IP와 포트 8000으로 연결합니다. 지원: XY v1.5, ORAS v1.4, 썬문 v1.2, 울트라썬문 v1.2 (게임을 최신 패치로). Old 3DS·2DS는 연결이 느리거나 불안정할 수 있습니다.") + "\n라이브헥스는 동시에 1개만 연결됩니다. 여러 인스턴스를 쓰는 경우 연결한 인스턴스에서만 게임과 주고받습니다.", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, HeightRequest = 44 };
        g.Add(T.L("박스를 넘길 때 게임에서 읽기", 14), 0); g.Add(readOnChange, 1);
        var s = new VerticalStackLayout { Spacing = 12, Children = { state, new Grid { ColumnDefinitions = { new(new GridLength(2, GridUnitType.Star)), new(GridLength.Star) }, ColumnSpacing = 8, Children = { } }, hint, g, T.L("칸에 저장하면 게임에도 바로 기록됩니다.", 12, sub: true), new HorizontalStackLayout { Spacing = 8, Children = { connect, readBox } } } };
        var row = (Grid)s.Children[1]; row.Add(T.Field("IP 주소", ip), 0); row.Add(T.Field("포트", port), 1);
        Body.Add(new ScrollView { Content = s });
        Update();
    }

    private void Update()
    {
        state.Text = AppState.LiveConnected ? "● 연결됨" : "○ 연결 안 됨";
        state.TextColor = AppState.LiveConnected ? T.Good : Colors.Gray;
        connect.Text = AppState.LiveConnected ? "연결 끊기" : "연결";
        MainPage.Instance?.UpdateLive();
    }

    private async Task Toggle()
    {
        var page = Application.Current.Windows[0].Page;
        var sav = AppState.Sav;
        if (AppState.LiveConnected) { try { AppState.Remote.com.Disconnect(); } catch { } Update(); return; }
        bool sw0 = RamOffsets.IsSwitchTitle(sav);
        Preferences.Set(sw0 ? "live_ip" : "live_ip_3ds", ip.Text ?? ""); Preferences.Set(sw0 ? "live_port" : "live_port_3ds", port.Text ?? "");
        try
        {
            var versions = RamOffsets.GetValidVersions(sav);
            if (versions.Length == 0) { await page.DisplayAlertAsync("라이브헥스", "현재 세이브의 게임은 라이브헥스를 지원하지 않습니다.", "확인"); return; }
            ICommunicator com = RamOffsets.IsSwitchTitle(sav) ? new SysBotMini() : new NTRClient();
            com.IP = ip.Text?.Trim() ?? ""; com.Port = int.TryParse(port.Text, out var p) ? p : (sw0 ? 6000 : 8000);
            var conn = Task.Run(() => com.Connect());
            if (await Task.WhenAny(conn, Task.Delay(15000)) != conn) { try { com.Disconnect(); } catch { } await page.DisplayAlertAsync("연결 실패", sw0 ? "스위치가 응답하지 않습니다. IP와 sys-botbase를 확인하세요." : "3DS가 응답하지 않습니다. BootNTR Selector로 NTR을 켠 뒤 게임을 실행했는지, IP·포트(8000)를 확인하세요.", "확인"); return; }
            await conn;
            if (!com.Connected) { await page.DisplayAlertAsync("연결 실패", sw0 ? "연결하지 못했습니다." : "NTR에 연결하지 못했습니다. 3DS에서 BootNTR Selector를 다시 실행해 보세요.", "확인"); return; }
            PokeSysBotMini remote = null;
            if (com is ICommunicatorNX nx)
            {
                var title = nx.GetTitleID(); var gv = nx.GetGameInfo("version").Trim();
                if (!InjectionBase.SaveCompatibleWithTitle(sav, title)) { com.Disconnect(); await page.DisplayAlertAsync("라이브헥스", "게임과 세이브의 버전이 맞지 않습니다.", "확인"); return; }
                remote = new PokeSysBotMini(InjectionBase.GetVersionFromTitle(title, gv), com);
            }
            else
            {
                PokeSysBotMini emptyOk = null;
                foreach (var v in versions.Reverse())
                {
                    try
                    {
                        var r = new PokeSysBotMini(v, com);
                        bool anyMon = false, allOk = true;
                        for (int i = 0; i < 6 && allOk; i++)
                        {
                            var d = sav.GetDecryptedPKM(r.ReadSlot(0, i).ToArray());
                            if (!d.ChecksumValid || d.Species > d.MaxSpeciesID) allOk = false; else if (d.Species != 0) anyMon = true;
                        }
                        if (allOk && anyMon) { remote = r; break; }
                        if (allOk) emptyOk ??= r;   // 박스 1 앞쪽이 비어 있으면 일단 후보
                    }
                    catch { }
                }
                remote ??= emptyOk;
            }
            if (remote == null) { com.Disconnect(); await page.DisplayAlertAsync("라이브헥스", "게임 버전을 확인하지 못했습니다.", "확인"); return; }
            AppState.Remote = remote; AppState.LiveSav = AppState.Sav;
            bool tr = AppState.ReadLiveTrainer();   // 게임 속 트레이너 이름·ID·성별 자동 반영
            BoxPanel.ReadLiveBox(); AppState.NotifyBox();
            Note.Show(tr ? $"연결했습니다 · 트레이너 {AppState.Sav.OT} 불러옴" : "연결했습니다 (트레이너 정보는 이 게임에서 읽지 못했습니다)");
        }
        catch (Exception ex) { await page.DisplayAlertAsync("연결 실패", ex.Message, "확인"); }
        Update();
    }
}
