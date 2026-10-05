using PKHeX.Core;
using PKHeX.Core.AutoMod;
namespace PKHeXKR;

// ======================= 트레이너(기본 어버이) 설정 =======================
public class TrainerSheet : Sheet
{
    public TrainerSheet() : base($"트레이너 · {AppState.GameName(AppState.Sav.Version)}", 0.8)
    {
        var ver = AppState.Sav.Version;
        var ot = T.Input(); ot.Text = AppState.OTFor(ver);
        var tid = T.Input(Keyboard.Numeric); tid.Text = AppState.TIDFor(ver).ToString(); bool g7 = AppState.Sav.Generation >= 7; tid.MaxLength = g7 ? 6 : 5;
        var sid = T.Input(Keyboard.Numeric); sid.Text = AppState.SIDFor(ver).ToString(); sid.MaxLength = g7 ? 4 : 5;
        byte gender = AppState.GenderFor(ver); int lang = AppState.LangFor(ver);
        var g = T.Pill(gender == 0 ? "♂ 남" : "♀ 여"); g.HeightRequest = 44;
        g.Clicked += (_, _) => { gender ^= 1; g.Text = gender == 0 ? "♂ 남" : "♀ 여"; T.GenderTint(g, gender); };
        (var lv, var ll) = T.Chooser(() => { });
        ll.Text = AppState.Src.Languages.FirstOrDefault(x => x.Value == lang)?.Text ?? "한국어";
        var tapL = new TapGestureRecognizer(); tapL.Tapped += (_, _) => SheetHost.Show(new PickerSheet("언어", AppState.Src.Languages, lang, c => { lang = c.Value; ll.Text = c.Text; }));
        lv.GestureRecognizers.Clear(); lv.GestureRecognizers.Add(tapL);
        var apply = new Switch { IsToggled = true, OnColor = T.Accent };
        var applyRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } }; applyRow.Add(T.L("현재 세이브에도 바로 적용", 15), 0); applyRow.Add(apply, 1);
        var save = T.Pill("저장", primary: true);
        save.Clicked += (_, _) =>
        {
            var name = string.IsNullOrWhiteSpace(ot.Text) ? "PKHeX" : ot.Text.Trim();
            int t = int.TryParse(tid.Text, out var t0) ? Math.Clamp(t0, 0, g7 ? 999999 : 65535) : 0, sd = int.TryParse(sid.Text, out var s0) ? Math.Clamp(s0, 0, g7 ? 4294 : 65535) : 0;
            AppState.SetTrainerFor(ver, name, gender, t, sd, lang);   // 이 게임 전용으로 저장
            if (apply.IsToggled) AppState.ReapplyTrainer();
            Close(); Note.Show("트레이너 정보를 저장했습니다");
        };
        var hint = T.L("7세대 이후 게임은 TID 6자리·SID 4자리, 그 이전 게임은 TID·SID 각 5자리(최대 65535)로 들어갑니다. 게임마다 따로 저장되며, 이 게임의 새 세이브와 자동 합법화(ALM)가 이 정보를 씁니다. 라이브헥스로 연결하면 게임 속 트레이너 정보로 자동 갱신됩니다.", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        var two = T.Cols(2); two.Add(T.Field("TID", tid), 0); two.Add(T.Field("SID", sid), 1);
        var rTid = T.Pill("무작위 TID", size: 12); rTid.Clicked += (_, _) => tid.Text = Random.Shared.Next(g7 ? 1_000_000 : 65536).ToString(g7 ? "000000" : "0");
        var rSid = T.Pill("무작위 SID", size: 12); rSid.Clicked += (_, _) => sid.Text = Random.Shared.Next(g7 ? 4295 : 65536).ToString(g7 ? "0000" : "0");
        var rRow = T.Cols(2); rRow.Add(rTid, 0); rRow.Add(rSid, 1);
        var name = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 }; name.Add(T.Field("이름", ot), 0); name.Add(T.Field("성별", g), 1);
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 12, Children = { name, two, rRow, T.Field("언어", lv), applyRow, hint, save } } });
    }
}


// ======================= 메뉴 (타일형) =======================
public class MenuSheet : Sheet
{
    public MenuSheet((string Title, (string Glyph, string Key)[] Items)[] groups, Action<string> run) : base("메뉴", side: true)
    {
        var stack = new VerticalStackLayout { Spacing = 4 };
        foreach (var (title, items) in groups)
        {
            var head = T.L(title, 12, sub: true); head.Margin = new Thickness(4, 12, 0, 4);
            stack.Children.Add(head);
            foreach (var (glyph, key) in items)
            {
                var icon = T.L(glyph, 20); icon.WidthRequest = 34; icon.HorizontalTextAlignment = TextAlignment.Center;
                var label = T.L(key, 15); label.LineBreakMode = LineBreakMode.WordWrap;
                var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 10, Padding = new Thickness(8, 12), MinimumHeightRequest = 48 };
                row.Add(icon, 0); row.Add(label, 1);
                var t = new TapGestureRecognizer(); t.Tapped += (_, _) => { Close(); run(key); }; row.GestureRecognizers.Add(t);
                var cell = new Border { Content = row, StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
                cell.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F9FAFB"), Color.FromArgb("#22262F"));
                stack.Children.Add(cell);
            }
        }
        Body.Add(new ScrollView { Content = stack });
    }
}

// ======================= 자동 어버이작 (교환 상대 정보로) =======================
public class AutoOTSheet : Sheet
{
    public AutoOTSheet() : base("자동 어버이작", 0.8)
    {
        var last = AppState.LastPartner;
        var ot = T.Input(); ot.Text = last?.OT ?? Preferences.Get("partner_ot", "");
        var tid = T.Input(Keyboard.Numeric); var sid = T.Input(Keyboard.Numeric);
        bool six = AppState.Sav.Generation >= 7; tid.MaxLength = six ? 6 : 5; sid.MaxLength = six ? 4 : 5;
        void SetIds(uint id32, bool s6) { if (s6) { tid.Text = (id32 % 1_000_000).ToString("000000"); sid.Text = (id32 / 1_000_000).ToString("0000"); } else { tid.Text = (id32 & 0xFFFF).ToString("00000"); sid.Text = (id32 >> 16).ToString("00000"); } }
        SetIds(last?.ID32 ?? (uint)Preferences.Get("partner_id32", 0), six);
        byte gender = last?.Gender ?? (byte)Preferences.Get("partner_gender", 0); int lang = last?.Lang ?? Preferences.Get("partner_lang", AppState.Sav.Language);
        var g = T.Pill(gender == 0 ? "♂ 남" : "♀ 여"); g.HeightRequest = 44; g.Clicked += (_, _) => { gender ^= 1; g.Text = gender == 0 ? "♂ 남" : "♀ 여"; T.GenderTint(g, gender); };
        (var lv, var ll) = T.Chooser(() => { }); ll.Text = AppState.Src.Languages.FirstOrDefault(x => x.Value == lang)?.Text ?? "";
        var tapL = new TapGestureRecognizer(); tapL.Tapped += (_, _) => SheetHost.Show(new PickerSheet("언어", AppState.Src.Languages, lang, c => { lang = c.Value; ll.Text = c.Text; })); lv.GestureRecognizers.Clear(); lv.GestureRecognizers.Add(tapL);
        var status = T.L(AppState.LiveConnected ? "라이브헥스 연결됨: 게임에서 교환 중일 때 상대 정보를 읽을 수 있습니다" : "라이브헥스가 연결되어 있지 않습니다. 상대 정보를 직접 입력하세요", 12, sub: true);
        status.LineBreakMode = LineBreakMode.WordWrap;
        var read = T.Pill("게임에서 교환 상대 읽기");
        read.IsEnabled = AppState.LiveConnected;
        read.Clicked += (_, _) =>
        {
            var p = AppState.ReadTradePartner();
            if (p == null) { status.Text = "교환 상대를 읽지 못했습니다. 링크 교환 화면에서 상대가 들어온 뒤 다시 시도하세요 (지원: SV·Z-A·소드실드)"; return; }
            ot.Text = p.OT; SetIds(p.ID32, p.SixDigit); gender = p.Gender; g.Text = gender == 0 ? "♂ 남" : "♀ 여"; T.GenderTint(g, gender); lang = p.Lang; ll.Text = AppState.Src.Languages.FirstOrDefault(x => x.Value == lang)?.Text ?? "";
            status.Text = $"교환 상대 {p.OT}의 정보를 읽었습니다";
        };
        var apply = T.Pill("편집 중인 포켓몬에 적용", primary: true);
        apply.Clicked += (_, _) =>
        {
            var pk = AppState.Pk; if (pk.Species == 0) { Note.Show("먼저 포켓몬을 불러오세요"); return; }
            if (string.IsNullOrWhiteSpace(ot.Text)) { status.Text = "교환 상대 이름이 필요합니다"; return; }
            int.TryParse(tid.Text, out var t); int.TryParse(sid.Text, out var sd);
            uint id32 = six ? (uint)(sd * 1_000_000L + t) : (uint)((sd << 16) | (t & 0xFFFF));
            var partner = new AppState.Partner(ot.Text.Trim(), id32, six, gender, lang);
            Preferences.Set("partner_ot", partner.OT); Preferences.Set("partner_id32", (int)id32); Preferences.Set("partner_gender", (int)gender); Preferences.Set("partner_lang", lang);
            AppState.Checkpoint(true); AppState.AutoOT(pk, partner); AppState.Edited(); AppState.ReloadEditors();
            Close(); Note.Show($"어버이를 {partner.OT}(으)로 바꿨습니다{(pk.IsShiny ? " · 이로치 유지" : "")}");
        };
        var two = T.Cols(2); two.Add(T.Field("TID", tid), 0); two.Add(T.Field("SID", sid), 1);
        var nm = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 }; nm.Add(T.Field("교환 상대 이름", ot), 0); nm.Add(T.Field("성별", g), 1);
        var hint = T.L("SysBot처럼 편집 중인 포켓몬의 어버이를 교환 상대로 바꿉니다. 현재 트레이너 기록은 지우고, 이로치였다면 ID가 바뀌어도 같은 모양(별·네모)의 이로치로 다시 맞춥니다.", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 12, Children = { hint, status, read, nm, two, T.Field("언어", lv), apply } } });
    }
}


// ======================= 일괄 수정 (PKHeX 배치 에디터) =======================
public class BatchSheet : Sheet
{
    public BatchSheet() : base("일괄 수정")
    {
        var text = new Editor { AutoSize = EditorAutoSizeOption.Disabled, FontSize = 14, HeightRequest = 180, Placeholder = "=Species=25   (조건: 피카츄만)\n.CurrentLevel=100\n.IsNicknamed=false" };
        text.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
        text.Text = Preferences.Get("batch_text", "");
        int scope = 0;
        var s0 = T.Pill("현재 박스", primary: true); var s1 = T.Pill("모든 박스"); var s2 = T.Pill("파티");
        void Sel(int k) { scope = k; foreach (var (b, i) in new[] { (s0, 0), (s1, 1), (s2, 2) }) { if (i == k) { b.BackgroundColor = T.Accent; b.TextColor = Colors.White; } else { b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); b.SetAppThemeColor(Button.TextColorProperty, T.Accent, Color.FromArgb("#A5B4FC")); } } }
        s0.Clicked += (_, _) => Sel(0); s1.Clicked += (_, _) => Sel(1); s2.Clicked += (_, _) => Sel(2);
        var result = T.L("", 12, sub: true); result.LineBreakMode = LineBreakMode.WordWrap;
        var examples = new (string, string)[] {
            ("레벨 100", ".CurrentLevel=100"), ("6V", ".IVs=$suggestAll\n.IV_HP=31\n.IV_ATK=31\n.IV_DEF=31\n.IV_SPA=31\n.IV_SPD=31\n.IV_SPE=31"), ("이로치", ".PID=$shiny"),
            ("닉네임 해제", ".IsNicknamed=false"), ("친밀도 최대", ".CurrentFriendship=255"), ("리본 전부", ".Ribbons=$suggest"), ("추천 기술", ".Moves=$suggest") };
        var exFlex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (n, v) in examples) { var b = T.Pill(n, size: 12); b.Margin = new Thickness(0, 0, 6, 6); b.Clicked += (_, _) => text.Text = string.IsNullOrWhiteSpace(text.Text) ? v : text.Text.TrimEnd() + "\n" + v; exFlex.Children.Add(b); }
        var run = T.Pill("실행", primary: true);
        run.Clicked += async (_, _) =>
        {
            var page = Application.Current.Windows[0].Page;
            var raw = text.Text ?? ""; Preferences.Set("batch_text", raw);
            var lines = raw.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
            if (lines.Length == 0) return;
            var sav = AppState.Sav;
            var slots = new List<(int Box, int Index, PKM Pk)>();
            if (scope == 2) { for (int i = 0; i < sav.PartyCount; i++) slots.Add((-1, i, sav.GetPartySlotAtIndex(i))); }
            else { int from = scope == 0 ? AppState.Box : 0, to = scope == 0 ? AppState.Box : sav.BoxCount - 1;
                for (int b = from; b <= to; b++) for (int i = 0; i < sav.BoxSlotCount; i++) { var p = sav.GetBoxSlotAtIndex(b, i); if (p.Species != 0) slots.Add((b, i, p)); } }
            if (slots.Count == 0) { result.Text = "대상 포켓몬이 없습니다"; return; }
            if (!await page.DisplayAlertAsync("일괄 수정", $"{slots.Count}마리에 적용합니다. 실행 취소가 되지 않으니 필요하면 먼저 박스를 내보내 두세요.", "실행", "취소")) return;
            try
            {
                var before = slots.Select(x => { var b = new byte[x.Pk.SIZE_STORED]; x.Pk.WriteDecryptedDataStored(b); return b; }).ToList();
                var sets = StringInstructionSet.GetBatchSets(lines);
                var proc = EntityBatchProcessor.Execute(lines, slots.Select(x => x.Pk).ToList());
                int changed = 0;
                for (int k = 0; k < slots.Count; k++)
                {
                    var (b, i, p) = slots[k]; p.RefreshChecksum();
                    var now = new byte[p.SIZE_STORED]; p.WriteDecryptedDataStored(now); if (!before[k].AsSpan().SequenceEqual(now)) changed++;
                    if (b < 0) sav.SetPartySlotAtIndex(p, i); else sav.SetBoxSlotAtIndex(p, b, i);
                }
                AppState.NotifyBox();
                result.Text = $"{slots.Count}마리 중 {changed}마리 변경\n" + proc.GetEditorResults(sets);
            }
            catch (Exception ex) { result.Text = "실행 실패: " + ex.Message; }
        };
        var help = T.L("PKHeX 일괄 편집기 문법과 같습니다. '=속성=값'은 조건(일치하는 개체만), '!속성=값'은 제외 조건, '.속성=값'은 변경입니다. $suggest(추천값), $shiny(이로치) 같은 특수값도 쓸 수 있습니다.", 12, sub: true);
        help.LineBreakMode = LineBreakMode.WordWrap;
        var frame = new Border { Content = text, StrokeThickness = 1, Padding = 6, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
        frame.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D1D5DB"), Color.FromArgb("#374151"));
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { help, new HorizontalStackLayout { Spacing = 8, Children = { s0, s1, s2 } }, frame, T.L("예시 넣기", 12, sub: true), exFlex, run, result } } });
    }
}

// ======================= 플레이어 편집 (가방·돈, 라이브헥스 블록 읽기/쓰기) =======================
public class PlayerSheet : Sheet
{
    private PlayerBag bag; private int pouchIdx;
    private readonly VerticalStackLayout list = new() { Spacing = 4 };
    private readonly HorizontalStackLayout pouchBar = new() { Spacing = 6 };   // FlexLayout은 버튼 폭을 잘못 재서 마지막 글자가 잘림 → 가로 스크롤 줄
    private readonly Entry money = T.Input(Keyboard.Numeric);
    private readonly Label status = T.L("", 12, sub: true);

    public PlayerSheet() : base("플레이어 편집")
    {
        status.LineBreakMode = LineBreakMode.WordWrap;
        var sav = AppState.Sav;
        try { money.Text = sav.Money.ToString(); } catch { money.Text = ""; money.IsEnabled = false; money.Placeholder = "이 게임은 돈 편집을 지원하지 않습니다"; }
        var saveBtn = T.Pill("세이브에 적용", primary: true);
        saveBtn.Clicked += (_, _) =>
        {
            try
            {
                if (money.IsEnabled && uint.TryParse(money.Text, out var m)) { uint cap = 9_999_999; try { cap = (uint)sav.MaxMoney; } catch { } try { sav.Money = Math.Min(m, cap); } catch { } }
                bag?.CopyTo(sav); status.Text = "세이브에 적용했습니다 (세이브 내보내기로 저장하세요)";
            }
            catch (Exception ex) { status.Text = "적용 실패: " + ex.Message; }
        };
        var liveRead = T.Pill("게임에서 가방 읽기"); var liveWrite = T.Pill("게임에 가방 쓰기");
        liveRead.IsEnabled = liveWrite.IsEnabled = AppState.LiveConnected;
        liveRead.Clicked += (_, _) => { try { var ok = AppState.Remote.Injector.ReadBlockFromString(AppState.Remote, sav, "Items", out _); if (ok) { Load(); status.Text = "게임에서 가방을 읽었습니다"; } else status.Text = "이 게임은 가방 블록 읽기를 지원하지 않습니다"; } catch (Exception ex) { status.Text = "읽기 실패: " + ex.Message; } };
        liveWrite.Clicked += (_, _) => { try { bag?.CopyTo(sav); AppState.Remote.Injector.WriteBlocksFromSAV(AppState.Remote, "Items", sav); status.Text = "게임에 가방을 썼습니다 (게임에서 가방을 다시 열어 확인하세요)"; } catch (Exception ex) { status.Text = "쓰기 실패: " + ex.Message; } };
        var hint = T.L("가방의 도구·개수와 돈을 고칩니다. 라이브헥스가 연결되어 있으면 게임의 가방을 바로 읽고 쓸 수 있습니다 (지원 게임에 한함).", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        // 세이브 트레이너 정보
        bool six = sav.Generation >= 7;
        var otE = T.Input(); otE.Text = sav.OT; otE.MaxLength = sav.MaxStringLengthTrainer;
        var tidE = T.Input(Keyboard.Numeric); var sidE = T.Input(Keyboard.Numeric);
        tidE.MaxLength = six ? 6 : 5; sidE.MaxLength = six ? 4 : 5;
        tidE.Text = six ? (sav.ID32 % 1_000_000).ToString("000000") : sav.TID16.ToString("00000");
        sidE.Text = six ? (sav.ID32 / 1_000_000).ToString("0000") : sav.SID16.ToString("00000");
        byte gender = sav.Gender; int lang = sav.Language;
        var gB = T.Pill(gender == 0 ? "♂ 남" : "♀ 여"); gB.HeightRequest = 44; gB.Clicked += (_, _) => { gender ^= 1; gB.Text = gender == 0 ? "♂ 남" : "♀ 여"; T.GenderTint(gB, gender); };
        (var lv, var ll) = T.Chooser(() => { }); ll.Text = AppState.Src.Languages.FirstOrDefault(x => x.Value == lang)?.Text ?? "";
        var tapL = new TapGestureRecognizer(); tapL.Tapped += (_, _) => SheetHost.Show(new PickerSheet("언어", AppState.Src.Languages, lang, c => { lang = c.Value; ll.Text = c.Text; })); lv.GestureRecognizers.Clear(); lv.GestureRecognizers.Add(tapL);
        var hh = T.Input(Keyboard.Numeric); var mm = T.Input(Keyboard.Numeric); var ss = T.Input(Keyboard.Numeric);
        bool hasTime = true;
        try { hh.Text = sav.PlayedHours.ToString(); mm.Text = sav.PlayedMinutes.ToString(); ss.Text = sav.PlayedSeconds.ToString(); } catch { hasTime = false; }
        var nameRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 }; nameRow.Add(T.Field("트레이너 이름", otE), 0); nameRow.Add(T.Field("성별", gB), 1);
        var idRow = T.Cols(2); idRow.Add(T.Field("TID", tidE), 0); idRow.Add(T.Field("SID", sidE), 1);
        var timeRow = T.Cols(3); timeRow.Add(T.Field("플레이 시간(시)", hh), 0); timeRow.Add(T.Field("분", mm), 1); timeRow.Add(T.Field("초", ss), 2); timeRow.IsVisible = hasTime;
        var trApply = T.Pill("트레이너 정보 적용", primary: true);
        trApply.Clicked += (_, _) =>
        {
            try
            {
                int.TryParse(tidE.Text, out var t); int.TryParse(sidE.Text, out var sd);
                if (six) { t = Math.Clamp(t, 0, 999999); sd = Math.Clamp(sd, 0, 4294); } else { t = Math.Clamp(t, 0, 65535); sd = Math.Clamp(sd, 0, 65535); }
                AppState.SetTrainerFor(sav.Version, string.IsNullOrWhiteSpace(otE.Text) ? sav.OT : otE.Text.Trim(), gender, t, sd, lang);
                AppState.ReapplyTrainer();   // 세이브 트레이너 + ALM 트레이너 + 게임별 저장값 갱신
                if (hasTime) { try { if (int.TryParse(hh.Text, out var h)) sav.PlayedHours = Math.Clamp(h, 0, 999); if (int.TryParse(mm.Text, out var m2)) sav.PlayedMinutes = Math.Clamp(m2, 0, 59); if (int.TryParse(ss.Text, out var s2)) sav.PlayedSeconds = Math.Clamp(s2, 0, 59); } catch { } }
                status.Text = "트레이너 정보를 세이브에 적용했습니다 (세이브 내보내기로 저장하세요)";
            }
            catch (Exception ex) { status.Text = "적용 실패: " + ex.Message; }
        };
        var trLiveR = T.Pill("게임에서 트레이너 읽기"); var trLiveW = T.Pill("게임에 트레이너 쓰기");
        trLiveR.IsEnabled = trLiveW.IsEnabled = AppState.LiveConnected;
        trLiveR.Clicked += (_, _) => { status.Text = AppState.ReadLiveTrainer() ? "게임에서 트레이너 정보를 읽었습니다 (시트를 다시 열면 반영된 값이 보입니다)" : "이 게임은 트레이너 블록 읽기를 지원하지 않습니다"; };
        trLiveW.Clicked += (_, _) =>
        {
            foreach (var name in new[] { "Trainer Data", "MyStatus" })
            { try { AppState.Remote.Injector.WriteBlocksFromSAV(AppState.Remote, name, sav); status.Text = "게임에 트레이너 정보를 썼습니다"; return; } catch { } }
            status.Text = "이 게임은 트레이너 블록 쓰기를 지원하지 않습니다";
        };
        var trCard = UiFold.Card("세이브 트레이너", "pl_tr", true, new VerticalStackLayout { Spacing = 10, Children = { nameRow, idRow, T.Field("언어", lv), timeRow, trApply,
            new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { trLiveR, trLiveW } } } } });
        var bagCard = UiFold.Card("가방 · 돈", "pl_bag", true, new VerticalStackLayout { Spacing = 10, Children = { hint, T.Field("돈", money),
            new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { liveRead, liveWrite } } },
            new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = pouchBar, HorizontalScrollBarVisibility = ScrollBarVisibility.Never }, list, saveBtn } });
        var dexSeen = T.Pill("모두 본 것으로"); var dexCaught = T.Pill("모두 잡은 것으로", primary: true); var dexClear = T.Pill("모두 해제");
        dexClear.BackgroundColor = T.Bad; dexClear.TextColor = Colors.White; dexClear.BorderWidth = 0;
        dexSeen.Clicked += (_, _) => DexFill.Apply(0, m => status.Text = m);
        dexCaught.Clicked += (_, _) => DexFill.Apply(1, m => status.Text = m);
        dexClear.Clicked += async (_, _) => { if (await Application.Current.Windows[0].Page.DisplayAlertAsync("도감", "도감 기록을 모두 지울까요?", "해제", "취소")) DexFill.Apply(2, m => status.Text = m); };
        var dexHint = T.L("현재 게임에 나오는 포켓몬을 도감에 일괄 등록합니다. 게임에 따라 지원하지 않을 수 있습니다.", 12, sub: true); dexHint.LineBreakMode = LineBreakMode.WordWrap;
        var dexCard = UiFold.Card("세이브 도감 일괄 등록", "pl_dex", false, new VerticalStackLayout { Spacing = 10, Children = { dexHint,
            new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { dexSeen, dexCaught, dexClear } } } } });
        var cosBtn = T.Pill("의상·꾸미기 전부 얻기", primary: true);
        cosBtn.Clicked += (_, _) => { var r = Cosmetics.UnlockAll(AppState.Sav); status.Text = r == null ? "이 게임은 의상 일괄 해금을 지원하지 않습니다 (PKHeX 지원: 소드실드·레츠고·XY 등)" : $"{r}을(를) 모두 얻었습니다 · 세이브 내보내기로 저장하세요"; };
        var cosHint = T.L("PKHeX가 지원하는 게임(소드실드 의상, 레츠고 액세서리, XY 액세서리, SV 던지기 동작 등)만 동작합니다.", 12, sub: true); cosHint.LineBreakMode = LineBreakMode.WordWrap;
        var cosCard = UiFold.Card("의상·꾸미기", "pl_cos", false, new VerticalStackLayout { Spacing = 10, Children = { cosHint, cosBtn } });
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 12, Children = { trCard, bagCard, dexCard, cosCard, status } } });
        Load();
    }

    private void Load()
    {
        var sav = AppState.Sav;
        try { bag = sav.Inventory; } catch { bag = null; }
        pouchBar.Children.Clear(); list.Children.Clear();
        if (bag == null || bag.Pouches.Count == 0) { status.Text = "이 게임은 가방 편집을 지원하지 않습니다"; return; }
        for (int i = 0; i < bag.Pouches.Count; i++)
        {
            int k = i; var b = T.Pill(PouchName(bag.Pouches[i].Type), size: 13);
            if (k == pouchIdx) { b.BackgroundColor = T.Accent; b.TextColor = Colors.White; }
            b.Clicked += (_, _) => { pouchIdx = k; Load(); };
            pouchBar.Children.Add(b);
        }
        pouchIdx = Math.Min(pouchIdx, bag.Pouches.Count - 1);
        var pouch = bag.Pouches[pouchIdx];
        var allowed = bag.Info.GetItems(pouch.Type).ToArray().Select(x => (int)x).ToHashSet();
        var itemList = AppState.Src.Items.Where(c => c.Value == 0 || allowed.Contains(c.Value)).ToList();
        // 가진 도구만 표시 (개수 0으로 바꾸면 다음에 사라짐) + 맨 아래 "도구 추가" 줄
        var shown = pouch.Items.Where(x => x.Index != 0 && x.Count > 0).ToList();
        foreach (var it in shown.Take(400))
        {
            var item = it;
            (var cv, var cl) = T.Chooser(() => SheetHost.Show(new PickerSheet("도구", itemList, item.Index, c => { if (c.Value == 0) { item.Count = 0; } else { item.Index = c.Value; if (item.Count == 0) item.Count = 1; } Load(); }, c => AppState.ItemSprite(c.Value))));
            cl.Text = AppState.Src.Items.FirstOrDefault(c => c.Value == item.Index)?.Text ?? item.Index.ToString();
            var cnt = T.Input(Keyboard.Numeric); cnt.WidthRequest = 80; cnt.HorizontalTextAlignment = TextAlignment.Center; cnt.Text = item.Count.ToString();
            int max = pouch.MaxCount;
            cnt.TextChanged += (_, e) => { if (int.TryParse(e.NewTextValue, out var v)) { if (v > max) { cnt.Text = max.ToString(); return; } item.Count = v; } };
            var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
            row.Add(new Image { Source = AppState.ItemSprite(item.Index), WidthRequest = 28, HeightRequest = 28 }, 0); row.Add(cv, 1); row.Add(cnt, 2);
            list.Children.Add(row);
        }
        (var av, var al) = T.Chooser(() => SheetHost.Show(new PickerSheet("추가할 도구", itemList.Where(c => c.Value != 0).ToList(), 0, c =>
        {
            var slot = pouch.Items.FirstOrDefault(x => x.Index == c.Value) ?? pouch.Items.FirstOrDefault(x => x.Index == 0);
            if (slot == null) { status.Text = "이 주머니에 빈 칸이 없습니다"; return; }
            slot.Index = c.Value; if (slot.Count == 0) slot.Count = 1; Load();
        }, c => AppState.ItemSprite(c.Value))));
        al.Text = "+ 도구 추가";
        list.Children.Add(av);
        status.Text = $"{PouchName(pouch.Type)}: {pouch.Count}종 · 최대 개수 {pouch.MaxCount}";
    }

    private static string PouchName(InventoryType t) => t switch
    {
        InventoryType.Items => "도구", InventoryType.KeyItems => "중요한 물건", InventoryType.TMHMs => "기술머신", InventoryType.Medicine => "회복약",
        InventoryType.Berries => "나무열매", InventoryType.Balls => "볼", InventoryType.BattleItems => "배틀 도구", InventoryType.MailItems => "메일",
InventoryType.Ingredients => "재료", InventoryType.Candy => "사탕", InventoryType.ZCrystals => "Z크리스탈", _ => t.ToString(),
    };
}


// ======================= 인스턴스 (여러 세이브 동시 편집) =======================
public class InstanceSheet : Sheet
{
    public InstanceSheet() : base("인스턴스 (여러 세이브)", 0.8)
    {
        AppState.SaveActive();
        var stack = new VerticalStackLayout { Spacing = 8 };
        var hint = T.L("세이브 여러 개를 동시에 열어 두고 오가며 편집합니다. 포켓몬은 '복사 → 붙여넣기'나 '다른 인스턴스 편집기로 보내기'로 옮기며, 대상 게임 형식으로 자동 변환됩니다.\n라이브헥스는 한 번에 한 인스턴스에만 연결됩니다. 연결한 인스턴스에서만 게임과 주고받습니다.", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap; stack.Children.Add(hint);
        for (int i = 0; i < AppState.Instances.Count; i++)
        {
            int k = i; var inst = AppState.Instances[i];
            bool active = k == AppState.Active, live = inst.Sav == AppState.LiveSav && AppState.Remote?.Connected == true;
            var title = T.L($"{k + 1}. {inst.Title}", 15, bold: active); title.LineBreakMode = LineBreakMode.WordWrap;
            var sub = T.L((active ? "현재 편집 중" : "") + (live ? (active ? " · " : "") + "● 라이브헥스 연결" : "") + (inst.Pk.Species != 0 ? $"  ·  편집기: {GameInfo.Strings.Species[inst.Pk.Species]}" : ""), 12, sub: true);
            var go = T.Pill(active ? "사용 중" : "전환", primary: !active, size: 12); go.IsEnabled = !active;
            go.Clicked += (_, _) => { Close(); AppState.SwitchTo(k); Note.Show($"{k + 1}번 인스턴스로 전환"); if (AppState.Remote?.Connected == true && AppState.LiveSav != AppState.Sav) Note.Show("라이브헥스는 다른 인스턴스에 연결되어 있습니다"); };
            var send = T.Pill("여기로 보내기", size: 12); send.IsVisible = !active && AppState.Pk.Species != 0;
            send.Clicked += (_, _) => { Close(); Note.Show(AppState.SendTo(k, AppState.Pk) ? $"{k + 1}번 인스턴스 편집기로 보냈습니다" : "그 게임 형식으로 변환할 수 없습니다"); };
            var close = T.Pill("닫기", size: 12); close.IsVisible = AppState.Instances.Count > 1;
            close.Clicked += async (_, _) =>
            {
                var page = Application.Current.Windows[0].Page;
                if (!await page.DisplayAlertAsync("인스턴스 닫기", $"{k + 1}번 인스턴스를 닫습니다. 내보내지 않은 변경은 사라집니다.", "닫기", "취소")) return;
                Close(); AppState.CloseInstance(k);
            };
            var btns = new HorizontalStackLayout { Spacing = 6, Children = { go, send, close } };
            var card = T.Card(new VerticalStackLayout { Spacing = 6, Children = { title, sub, btns } }, 10);
            stack.Children.Add(card);
        }
        var addBlank = T.Pill("+ 빈 세이브로 새 인스턴스", primary: true);
        addBlank.Clicked += async (_, _) =>
        {
            var page = Application.Current.Windows[0].Page;
            var names = AppState.Games.Select(AppState.GameName).ToArray();
            var pick = await page.DisplayActionSheetAsync("새 인스턴스의 게임", "취소", null, names);
            int i = Array.IndexOf(names, pick); if (i < 0) return;
            Close(); AppState.AddInstance(AppState.NewBlankSave(AppState.Games[i])); Note.Show($"{pick} 인스턴스를 추가했습니다");
        };
        var addFile = T.Pill("+ 세이브 파일로 새 인스턴스");
        addFile.Clicked += async (_, _) =>
        {
            try
            {
                var f = await FilePicker.PickAsync(); if (f == null) return;
                byte[] data; await using (var st = await f.OpenReadAsync()) { using var ms = new MemoryStream(); await st.CopyToAsync(ms); data = ms.ToArray(); }
                if (FileUtil.GetSupportedFile(data, Path.GetExtension(f.FileName), AppState.Sav) is not SaveFile s) { Note.Show("세이브 파일이 아닙니다"); return; }
                s.Metadata.SetExtraInfo(f.FileName);
                Close(); AppState.AddInstance(s); Note.Show("세이브를 새 인스턴스로 열었습니다");
            }
            catch (Exception ex) { Note.Show("열기 실패: " + ex.Message); }
        };
        stack.Children.Add(addBlank); stack.Children.Add(addFile);
        Body.Add(new ScrollView { Content = stack });
    }
}
