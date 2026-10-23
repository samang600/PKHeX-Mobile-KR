using PKHeX.Core;
using PKHeX.Core.AutoMod;
using PKHeX.Core.Injection;
using System.Runtime.CompilerServices;
namespace PKHeXKR;

// ======================= 쇼다운 =======================
public class ShowdownSheet : Sheet
{
    /// <summary>쇼다운 내보내기를 시스봇·ALM 형식으로 (Ball·Language·OT·TID·SID·증표·크기 줄 추가)</summary>
    public static bool SysBot { get => Preferences.Get("sd_sysbot", true); set => Preferences.Set("sd_sysbot", value); }
    private readonly Editor text = new() { AutoSize = EditorAutoSizeOption.Disabled, FontSize = 14, FontFamily = "monospace", Placeholder = "쇼다운 세트를 붙여넣으세요\n\nPikachu @ Light Ball\nAbility: Static\n..." };
    public ShowdownSheet() : base("쇼다운 세트")
    {
        text.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
        var sys = new Switch { IsToggled = SysBot, OnColor = T.Accent };
        string One(PKM p) => SysBot ? SysBotText.Get(p) : ShowdownParsing.GetShowdownText(p);
        if (AppState.Pk.Species != 0) text.Text = One(AppState.Pk);
        sys.Toggled += (_, e) => { SysBot = e.Value; if (AppState.Pk.Species != 0) text.Text = One(AppState.Pk); };
        var sysLab = T.L("시스봇 형식 (볼·언어·어버이·크기·증표 줄 포함)", 13); sysLab.VerticalOptions = LayoutOptions.Center;
        var sysRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } }; sysRow.Add(sysLab, 0); sysRow.Add(sys, 1);
        var paste = T.Pill("붙여넣기"); paste.Clicked += async (_, _) => { if (Clipboard.HasText) text.Text = await Clipboard.GetTextAsync(); };
        var copy = T.Pill("복사"); copy.Clicked += async (_, _) => { await Clipboard.SetTextAsync(text.Text ?? ""); Note.Show("복사했습니다"); };
        var box = T.Pill("현재 박스"); box.Clicked += (_, _) => text.Text = SysBot ? SysBotText.GetMany(AppState.CurrentSlots().Where(p => p.Species != 0)) : ShowdownParsing.GetShowdownSets(AppState.CurrentSlots().Where(p => p.Species != 0), Environment.NewLine + Environment.NewLine);
        var party = T.Pill("파티"); party.Clicked += (_, _) =>
        {
            var sav = AppState.Sav; var mons = Enumerable.Range(0, sav.PartyCount).Select(sav.GetPartySlotAtIndex).Where(p => p.Species != 0).ToList();
            if (mons.Count == 0) { Note.Show("파티가 비어 있습니다"); return; }
            text.Text = SysBot ? SysBotText.GetMany(mons) : ShowdownParsing.GetShowdownSets(mons, Environment.NewLine + Environment.NewLine);
        };
        var share = T.Pill("공유"); share.Clicked += async (_, _) => { if (string.IsNullOrWhiteSpace(text.Text)) return; try { await Share.Default.RequestAsync(new ShareTextRequest { Text = text.Text, Title = "쇼다운 세트" }); } catch (Exception ex) { Note.Show("공유 실패: " + ex.Message); } };
        var make = T.Pill("합법으로 만들기", primary: true);
        make.Clicked += async (_, _) => await Make();
        var team = T.Pill("팀 → 박스", primary: true);
        team.Clicked += async (_, _) => { try { await ImportTeam(false); } catch (Exception ex) { Note.Show("오류: " + ex.Message); } };
        var teamParty = T.Pill("팀 → 파티", primary: true);
        teamParty.Clicked += async (_, _) => { try { await ImportTeam(true); } catch (Exception ex) { Note.Show("오류: " + ex.Message); } };
        var btns = new VerticalStackLayout { Spacing = 6, Children = {
            sysRow,
            T.Btns(3, paste, copy, share, box, party),
            T.Btns(3, make, team, teamParty) } };
        var frame = new Border { Content = text, StrokeThickness = 1, Padding = 6, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
        frame.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D1D5DB"), Color.FromArgb("#374151"));
        Body.RowDefinitions.Add(new(GridLength.Star)); Body.RowDefinitions.Add(new(GridLength.Auto));
        Body.Add(frame, 0, 0); Body.Add(btns, 0, 1);
    }

    /// <summary>세트 여러 개를 각각 합법화해 현재 박스부터 빈 칸에 차례로 넣음 (toParty: 파티 1번부터 최대 6마리, 기존 파티는 덮어씀).</summary>
    private async Task ImportTeam(bool toParty)
    {
        var raw = (text.Text ?? "").Trim(); if (raw.Length == 0) return;
        var sets = ShowdownParsing.GetShowdownSets(raw).Where(x => x.Species != 0).ToList();
        if (sets.Count == 0) { Note.Show("세트를 찾지 못했습니다 (영어 쇼다운 형식)"); return; }
        var sav = AppState.Sav; int start = AppState.Box;
        if (toParty)
        {
            if (sets.Count > 6) { Note.Show("파티에는 6마리까지만 넣습니다 (앞의 6개)"); sets = sets.Take(6).ToList(); }
            if (sav.PartyCount > 0 && !await Application.Current.Windows[0].Page.DisplayAlertAsync("파티에 넣기", $"지금 파티({sav.PartyCount}마리)를 이 팀으로 바꿉니다. 계속할까요?", "넣기", "취소")) return;
        }
        var made = await Task.Run(() =>
        {
            var list = new List<PKM>();
            foreach (var set in sets) { try { list.Add(sav.GetLegalFromSet(set).Created); } catch { list.Add(null); } }
            return list;
        });
        int put = 0, legal = 0;
        if (toParty)
        {
            SaveStore.Snapshot("파티교체");
            for (int i = sav.PartyCount - 1; i >= 0; i--) sav.DeletePartySlot(i);
            foreach (var pk in made.Where(x => x != null)) { sav.SetPartySlotAtIndex(pk, sav.PartyCount); put++; if (AppState.IsLegal(pk)) legal++; }
            AppState.AfterBulk();
            AppState.PartyMode = true;
        }
        else
        {
            int box = start, slot = 0;
            foreach (var pk in made.Where(x => x != null))
            {
                while (box < sav.BoxCount && sav.GetBoxSlotAtIndex(box, slot).Species != 0) { if (++slot >= sav.BoxSlotCount) { slot = 0; box++; } }
                if (box >= sav.BoxCount) break;
                sav.SetBoxSlotAtIndex(pk, box, slot); AppState.LiveSync(box, slot); put++; if (AppState.IsLegal(pk)) legal++;
            }
        }
        Close(); AppState.NotifyBox();
        Note.Show($"{sets.Count}개 중 {put}마리를 {(toParty ? "파티" : "박스")}에 넣었습니다 (합법 {legal})");
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
        if (SwitchFrlg.Active(AppState.Sav)) report = "스위치판 FRLG 기준 (루비·사파이어·에메랄드·XD 출신과 옛 배포는 불법)\n\n" + report;
        var body = T.L(report, 13); body.LineBreakMode = LineBreakMode.WordWrap;
        var fix = T.Pill("자동 합법화 (ALM)", primary: true);
        fix.IsVisible = !la.Valid;
        fix.Clicked += async (_, _) => { Close(); await MainPage.Legalize(); };
        // 불법 사유마다 쉬운 설명 + 그 항목만 고치는 버튼
        var tips = new VerticalStackLayout { Spacing = 8 };
        if (!la.Valid)
        {
            var bad = la.Results.Where(r => r.Judgement == Severity.Invalid).ToList();
            var seen = new HashSet<LegalityCheckResultCode>();
            var items = new List<(LegalHelp.Tip tip, string why)>();
            foreach (var t in LegalHelp.ForMoves(la, AppState.Pk)) items.Add((t, t.Why));
            foreach (var r in bad)
            {
                if (!seen.Add(r.Result)) continue;
                var t = LegalHelp.For(r);
                items.Add((t, t?.Why ?? LegalHelp.ForIdentifier(r.Identifier)));
            }
            foreach (var (tip, whyText) in items)
            {
                var col = new VerticalStackLayout { Spacing = 4 };
                var why = T.L(whyText ?? "", 13, bold: true); why.LineBreakMode = LineBreakMode.WordWrap;
                if (why.Text.Length == 0) continue;
                col.Children.Add(why);
                if (tip != null) { var how = T.L("→ " + tip.How, 12, sub: true); how.LineBreakMode = LineBreakMode.WordWrap; col.Children.Add(how); }
                if (tip?.Fix != null)
                {
                    var b = T.Pill(tip.FixLabel ?? "고치기", size: 12);
                    b.Clicked += (_, _) =>
                    {
                        var pk = AppState.Pk; AppState.Checkpoint(true);
                        bool changed; try { changed = tip.Fix(pk); } catch (Exception ex) { Note.Show("고치지 못했습니다: " + ex.Message); return; }
                        if (!changed) { Note.Show("바뀐 것이 없습니다"); return; }
                        AppState.Edited(); AppState.ReloadEditors();
                        bool nowOk = AppState.IsLegal(pk);
                        Close(); SheetHost.Show(new LegalitySheet());
                        Note.Show(nowOk ? "고쳤습니다 · 이제 합법" : "이 항목은 고쳤습니다 · 다른 사유가 남아 있습니다");
                    };
                    col.Children.Add(b);
                }
                tips.Children.Add(T.Card(col, 10));
            }
            if (tips.Children.Count > 0 && ev) tips.Children.Insert(0, T.L("꺼 둔 검사라 무시된 항목", 12, sub: true));
        }
        var all = new VerticalStackLayout { Spacing = 10, Children = { tips, T.L("PKHeX 보고서", 13, sub: true), body } };
        Body.RowDefinitions.Add(new(GridLength.Auto)); Body.RowDefinitions.Add(new(GridLength.Star)); Body.RowDefinitions.Add(new(GridLength.Auto));
        Body.Add(badge, 0, 0); Body.Add(new ScrollView { Content = all }, 0, 1); Body.Add(fix, 0, 2);
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
        var readBox = T.Pill("박스 읽기"); readBox.Clicked += (_, _) => { if (!AppState.LiveConnected) return; BoxPanel.ReadLiveBox(); AppState.NotifyBox(); Note.Show("게임에서 박스를 읽었습니다"); };
        var writeBox = T.Pill("박스 쓰기"); writeBox.Clicked += async (_, _) =>
        {
            if (!AppState.LiveConnected) return;
            if (AppState.Sav is SAV7b) { Note.Show("레츠고는 쓰기가 게임에 반영되지 않습니다"); return; }
            if (!await Application.Current.Windows[0].Page.DisplayAlertAsync("박스 쓰기", $"{AppState.BoxName(AppState.Box)} 30칸을 게임 메모리에 통째로 씁니다. 게임 쪽 그 박스는 앱 내용으로 바뀝니다.", "쓰기", "취소")) return;
            bool whole = await Task.Run(() => AppState.LiveSyncBox(AppState.Box));
            if (!whole && AppState.LiveConnected) await Task.Run(() => { for (int i = 0; i < AppState.Sav.BoxSlotCount; i++) AppState.LiveSync(AppState.Box, i); });
            Note.Show(AppState.LiveConnected ? (whole ? "박스를 통째로 썼습니다" : "칸마다 썼습니다 (이 게임은 통째 쓰기 불가)") : "연결이 끊겼습니다");
        };
        var readAll = T.Pill("모든 박스 읽기"); readAll.Clicked += async (_, _) =>
        {
            if (!AppState.LiveConnected) return;
            var sav = AppState.Sav;
            if (!await Application.Current.Windows[0].Page.DisplayAlertAsync("모든 박스 읽기", $"게임의 박스 {sav.BoxCount}개를 모두 읽어 앱 세이브에 넣습니다 (앱 박스 내용은 게임 것으로 바뀜). 와이파이로 1~2분.", "읽기", "취소")) return;
            SaveStore.Snapshot("모든박스읽기");
            int cur = AppState.Box; int done = 0;
            using var bg = BgTask.Begin("라이브헥스 모든 박스 읽기");
            await Task.Run(() =>
            {
                var r = AppState.Remote; var len = sav.BoxSlotCount * (RamOffsets.GetSlotSize(r.Version) + RamOffsets.GetGapSize(r.Version));
                for (int b = 0; b < sav.BoxCount && AppState.LiveConnected; b++)
                {
                    try { lock (AppState.LiveLock) sav.SetBoxBinary(r.ReadBox(b, len), b); done++; bg.Report($"{b + 1}/{sav.BoxCount}", b + 1, sav.BoxCount); }
                    catch { AppState.MarkLiveLost(); break; }
                }
            });
            AppState.AfterBulk(); AppState.Box = cur; AppState.NotifyBox();
            bg.Done($"박스 {done}개 읽음"); Note.Show($"게임에서 박스 {done}개를 읽었습니다");
        };
        var writeAll = T.Pill("모든 박스 쓰기"); writeAll.Clicked += async (_, _) =>
        {
            if (!AppState.LiveConnected) return;
            if (AppState.Sav is SAV7b) { Note.Show("레츠고는 쓰기가 게임에 반영되지 않습니다"); return; }
            var sav = AppState.Sav;
            if (!await Application.Current.Windows[0].Page.DisplayAlertAsync("모든 박스 쓰기", $"앱 세이브의 박스 {sav.BoxCount}개를 게임 메모리에 모두 씁니다. 게임 쪽 박스는 전부 앱 내용으로 바뀝니다. 되돌릴 수 없으니 확실할 때만 하세요.", "쓰기", "취소")) return;
            int done = 0;
            using var bg = BgTask.Begin("라이브헥스 모든 박스 쓰기");
            await Task.Run(() =>
            {
                for (int b = 0; b < sav.BoxCount && AppState.LiveConnected; b++)
                {
                    if (!AppState.LiveSyncBox(b)) for (int i = 0; i < sav.BoxSlotCount && AppState.LiveConnected; i++) AppState.LiveSync(b, i);
                    done++; bg.Report($"{b + 1}/{sav.BoxCount}", b + 1, sav.BoxCount);
                }
            });
            bg.Done($"박스 {done}개 씀"); Note.Show(AppState.LiveConnected ? $"게임에 박스 {done}개를 썼습니다 · 게임에서 세이브해야 남습니다" : $"{done}개 쓰던 중 연결이 끊겼습니다");
        };
        var hint = T.L((sw ? "스위치: sys-botbase가 실행 중이어야 합니다 (기본 포트 6000)." : "3DS: Luma3DS에서 BootNTR Selector로 NTR을 켠 뒤 게임을 실행하고, 3DS IP와 포트 8000으로 연결합니다. 지원: XY v1.5, ORAS v1.4, 썬문 v1.2, 울트라썬문 v1.2 (게임을 최신 패치로). Old 3DS·2DS는 연결이 느리거나 불안정할 수 있습니다.") + "\n라이브헥스는 동시에 1개만 연결됩니다. 여러 인스턴스를 쓰는 경우 연결한 인스턴스에서만 게임과 주고받습니다.", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, HeightRequest = 44 };
        g.Add(T.L("박스를 넘길 때 게임에서 읽기", 14), 0); g.Add(readOnChange, 1);
        var s = new VerticalStackLayout { Spacing = 12, Children = { state, new Grid { ColumnDefinitions = { new(new GridLength(2, GridUnitType.Star)), new(GridLength.Star) }, ColumnSpacing = 8, Children = { } }, hint, g, T.L(AppState.Sav is SAV7b ? "레츠고는 박스 읽기·트레이너 가져오기만 됩니다. 쓰기는 게임에 반영되지 않아 앱 세이브에만 저장됩니다 (세이브 파일로 편집하세요)." : "칸에 저장하면 게임에도 바로 기록됩니다.", 12, sub: true), T.Btns(3, connect, readBox, writeBox), T.L("박스 통째로 · 게임에서 세이브해야 남음", 12, sub: true), T.Btns(2, readAll, writeAll) } };
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
            Note.Show((tr ? $"연결했습니다 · 트레이너 {AppState.Sav.OT} 불러옴" : "연결했습니다 (트레이너 정보는 이 게임에서 읽지 못했습니다)") + (AppState.Sav is SAV7b ? " · 레츠고는 읽기 전용" : ""));
        }
        catch (Exception ex) { await page.DisplayAlertAsync("연결 실패", ex.Message, "확인"); }
        Update();
    }
}
