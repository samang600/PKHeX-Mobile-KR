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
            if (AppState.Sav.Generation <= 3 && OldGenName.HasJapanese(name)) lang = (int)LanguageID.Japanese;   // 1~3세대: 일본어 이름은 일본어 개체·세이브에서만 저장됨
            AppState.SetTrainerFor(ver, name, gender, t, sd, lang);   // 이 게임 전용으로 저장
            Close();
            if (apply.IsToggled) MainThread.BeginInvokeOnMainThread(async () => { try { Note.Show(await OldGenName.ApplyChecked() ?? "트레이너 정보를 저장했습니다"); } catch (Exception ex) { Note.Show("오류: " + ex.Message); } });
            else Note.Show("트레이너 정보를 저장했습니다");
        };
        var hint = T.L("• TID·SID: 7세대 이후 6·4자리, 그 이전 5·5자리(최대 65535)\n• 게임마다 저장, 새 세이브·자동 합법화에 사용 (라이브헥스 연결 시 자동 갱신)", 12, sub: true);
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
            uint id32 = six ? (uint)Math.Min(Math.Max(sd, 0) * 1_000_000L + Math.Clamp(t, 0, 999_999), uint.MaxValue) : (uint)((Math.Clamp(sd, 0, 65535) << 16) | (t & 0xFFFF));
            var partner = new AppState.Partner(ot.Text.Trim(), id32, six, gender, lang);
            Preferences.Set("partner_ot", partner.OT); Preferences.Set("partner_id32", (int)id32); Preferences.Set("partner_gender", (int)gender); Preferences.Set("partner_lang", lang);
            AppState.Checkpoint(true); AppState.AutoOT(pk, partner); AppState.Edited(); AppState.ReloadEditors();
            Close(); Note.Show($"어버이를 {partner.OT}(으)로 바꿨습니다{(pk.IsShiny ? " · 이로치 유지" : "")}");
        };
        var two = T.Cols(2); two.Add(T.Field("TID", tid), 0); two.Add(T.Field("SID", sid), 1);
        var nm = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 }; nm.Add(T.Field("교환 상대 이름", ot), 0); nm.Add(T.Field("성별", g), 1);
        var hint = T.L("편집 중인 포켓몬의 어버이를 교환 상대로 (SysBot처럼) · 현재 트레이너 기록은 지우고 이로치 모양은 유지", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 12, Children = { hint, status, read, nm, two, T.Field("언어", lv), apply } } });
    }
}


// ======================= 일괄 수정 (PKHeX 배치 에디터) =======================
public class BatchSheet : Sheet
{
    private readonly Editor text = new() { AutoSize = EditorAutoSizeOption.Disabled, FontSize = 14, HeightRequest = 160, Placeholder = "=Species=25   (조건: 피카츄만)\n.CurrentLevel=100\n.IsNicknamed=false" };
    private readonly Label result = T.L("", 12, sub: true);

    // 자주 쓰는 속성 (위쪽에 한국어 이름으로)
    private static readonly (string Prop, string Name)[] Common =
    [
        ("Species", "종"), ("Form", "폼"), ("Nickname", "닉네임"), ("IsNicknamed", "닉네임 여부"), ("Gender", "성별"), ("IsShiny", "이로치 여부"), ("PID", "성격값 (이로치)"),
        ("Ball", "볼"), ("Nature", "성격"), ("StatAlignment", "능력 성격 (민트)"), ("Ability", "특성"), ("AbilityNumber", "특성 번호"), ("CurrentLevel", "레벨"), ("HeldItem", "지닌 물건"),
        ("Language", "언어"), ("Version", "출신 게임"), ("OriginalTrainerName", "어버이 이름"), ("OriginalTrainerGender", "어버이 성별"), ("DisplayTID", "어버이 ID"), ("DisplaySID", "어버이 SID"),
        ("HandlingTrainerName", "현재 트레이너"), ("CurrentFriendship", "친밀도"), ("MetLevel", "만난 레벨"), ("MetLocation", "만난 장소"), ("MetDate", "만난 날짜"),
        ("IV_HP", "개체값 H"), ("IV_ATK", "개체값 A"), ("IV_DEF", "개체값 B"), ("IV_SPA", "개체값 C"), ("IV_SPD", "개체값 D"), ("IV_SPE", "개체값 S"),
        ("EV_HP", "노력치 H"), ("EV_ATK", "노력치 A"), ("EV_DEF", "노력치 B"), ("EV_SPA", "노력치 C"), ("EV_SPD", "노력치 D"), ("EV_SPE", "노력치 S"), ("EVs", "노력치 전체"),
        ("Move1", "기술 1"), ("Move2", "기술 2"), ("Move3", "기술 3"), ("Move4", "기술 4"), ("Moves", "기술 배치"), ("RelearnMoves", "떠올리기 기술"),
        ("Ribbons", "리본"), ("IsEgg", "알"), ("IsAlpha", "우두머리"), ("TeraTypeOverride", "테라 타입"), ("Scale", "배율 (크기)"), ("HeightScalar", "키"), ("WeightScalar", "몸무게"),
        ("CanGigantamax", "거다이맥스"), ("DynamaxLevel", "다이맥스 레벨"), ("FatefulEncounter", "운명적인 만남"), ("Legal", "합법 여부 (조건)"),
    ];

    public BatchSheet() : base("일괄 수정")
    {
        text.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
        text.Text = Preferences.Get("batch_text", "");
        result.LineBreakMode = LineBreakMode.WordWrap;
        int scope = 0;
        var s0 = T.Pill("현재 박스", primary: true); var s1 = T.Pill("모든 박스"); var s2 = T.Pill("파티");
        void Sel(int k) { scope = k; foreach (var (b, i) in new[] { (s0, 0), (s1, 1), (s2, 2) }) { if (i == k) { b.BackgroundColor = T.Accent; b.TextColor = Colors.White; } else { b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); b.SetAppThemeColor(Button.TextColorProperty, T.Accent, Color.FromArgb("#A5B4FC")); } } }
        s0.Clicked += (_, _) => Sel(0); s1.Clicked += (_, _) => Sel(1); s2.Clicked += (_, _) => Sel(2);

        // 한국어로 적기 → 명령어
        var ko = new Editor { AutoSize = EditorAutoSizeOption.TextChanges, FontSize = 15, MinimumHeightRequest = 70, Placeholder = "예) 피카츄 전부 암컷으로 바꾸기\n가디안 계열 암컷 문볼로\n닉네임 붙은 포켓몬 닉네임 없애기\n이로치 포켓몬 어버이 미월로" };
        ko.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
        ko.Text = Preferences.Get("batch_ko", "");
        var conv = T.Pill("명령어로 바꾸기", primary: true);
        conv.Clicked += (_, _) =>
        {
            Preferences.Set("batch_ko", ko.Text ?? "");
            var r = BatchKo.Convert(ko.Text, AppState.Sav, UserDict.Load());
            if (r.Lines.Count == 0) { result.Text = "알아본 명령이 없습니다" + (r.Notes.Count > 0 ? "\n" + string.Join("\n", r.Notes) : ""); return; }
            text.Text = string.Join("\n", r.Lines);
            result.Text = "명령어로 바꿨습니다 · 확인하고 '실행'" + (r.Unknown.Count > 0 ? $"\n? 알아보지 못한 말: {string.Join(", ", r.Unknown)}" : "") + (r.Notes.Count > 0 ? "\n※ " + string.Join("\n※ ", r.Notes) : "");
        };
        var koFrame = Frame(ko);

        var pick = T.Pill("명령어"); pick.Clicked += async (_, _) => { try { await PickCommand(); } catch (Exception ex) { Note.Show("오류: " + ex.Message); } };
        var clear = T.Pill("지우기"); clear.Clicked += (_, _) => { text.Text = ""; result.Text = ""; };
        var saveSet = T.Pill("저장"); saveSet.Clicked += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(text.Text)) { Note.Show("저장할 명령어가 없습니다"); return; }
            var name = await Application.Current.Windows[0].Page.DisplayPromptAsync("명령 묶음 저장", "이름", "저장", "취소", maxLength: 30);
            if (string.IsNullOrWhiteSpace(name)) return;
            BatchSets.Save(name.Trim(), text.Text); Note.Show($"'{name.Trim()}'로 저장했습니다");
        };
        var loadSet = T.Pill("저장 목록"); loadSet.Clicked += (_, _) => SheetHost.Show(new BatchSetsSheet(t => { text.Text = t; result.Text = "저장 목록에서 불러왔습니다 · 확인하고 '실행'"; }));
        var examples = new (string, string)[] {
            ("레벨 100", ".CurrentLevel=100"), ("6V", ".IV_HP=31\n.IV_ATK=31\n.IV_DEF=31\n.IV_SPA=31\n.IV_SPD=31\n.IV_SPE=31"), ("이로치", ".PID=$shiny"),
            ("닉네임 해제", ".IsNicknamed=false"), ("친밀도 최대", ".CurrentFriendship=255"), ("리본 전부", ".Ribbons=$suggestAll"), ("추천 기술", ".Moves=$suggest") };
        var exFlex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (n, v) in examples) { var b = T.Pill(n, size: 12); b.Margin = new Thickness(0, 0, 6, 6); b.Clicked += (_, _) => Append(v); exFlex.Children.Add(b); }
        var run = T.Pill("실행", primary: true);
        run.Clicked += async (_, _) => await Run(scope);
        var help = T.L("한국어로 적거나, 명령어를 고르거나 직접 씁니다.\n=속성=값 조건 · !속성=값 제외 · .속성=값 변경 · ';' 줄로 묶음 나눔", 12, sub: true);
        help.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = {
            help, new HorizontalStackLayout { Spacing = 8, Children = { s0, s1, s2 } },
            T.L("한국어로", 12, sub: true), koFrame, conv,
            T.L("명령어", 12, sub: true), Frame(text), T.Btns(4, pick, saveSet, loadSet, clear), exFlex, run, result } } });
    }

    private static Border Frame(View v)
    {
        var f = new Border { Content = v, StrokeThickness = 1, Padding = 6, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
        f.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D1D5DB"), Color.FromArgb("#374151"));
        return f;
    }
    private void Append(string v) => text.Text = string.IsNullOrWhiteSpace(text.Text) ? v : text.Text.TrimEnd() + "\n" + v;

    /// <summary>PC판 일괄 편집기처럼: 속성 → 조건/변경 → 값을 차례로 골라 한 줄 넣기.</summary>
    private async Task PickCommand()
    {
        var type = AppState.Sav.PKMType; var ed = EntityBatchEditor.Instance;
        int ti = -1; for (int i = 0; i < ed.Types.Count; i++) if (ed.Types[i] == type) { ti = i; break; }
        var props = (ti >= 0 ? ed.Properties[ti] : ed.Properties[0]).ToHashSet();
        props.Add("Legal");
        var items = new List<ComboItem>(); var names = new List<string>();
        foreach (var (prop, name) in Common) if (props.Contains(prop)) { items.Add(new ComboItem($"{name}  ({prop})", names.Count)); names.Add(prop); }
        foreach (var prop in props.Where(x => Common.All(c => c.Prop != x)).OrderBy(x => x)) { items.Add(new ComboItem(prop, names.Count)); names.Add(prop); }
        var tcs = new TaskCompletionSource<int>();
        var ps = new PickerSheet("속성 고르기", items, -1, c => tcs.TrySetResult(c.Value)); ps.Closed += () => tcs.TrySetResult(-1);   // 고르지 않고 닫으면 끝
        SheetHost.Show(ps);
        var idx = await tcs.Task; if (idx < 0) return;
        var p = names[idx];
        var page = Application.Current.Windows[0].Page;
        bool condOnly = p is "Legal" or "IsShiny";
        string[] kinds = condOnly ? ["조건: 같은 것만 (=)", "조건: 다른 것만 (!)"] : ["바꾸기 (.)", "조건: 같은 것만 (=)", "조건: 다른 것만 (!)", "조건: 이상 (≥)", "조건: 이하 (≤)"];
        var kind = await page.DisplayActionSheetAsync($"{p}", "취소", null, kinds);
        if (kind == null || kind == "취소") return;
        var prefix = kind[^2].ToString();
        var value = await PickValue(p, prefix == ".");
        if (value == null) return;
        Append($"{prefix}{p}={value}");
    }

    private static async Task<string> PickValue(string p, bool apply)
    {
        var page = Application.Current.Windows[0].Page; var src = AppState.Src; var s = GameInfo.Strings;
        IReadOnlyList<ComboItem> list = p switch
        {
            "Species" => src.Species, "Ball" => AppState.Balls, "Nature" or "StatAlignment" => src.Natures, "Ability" => src.Abilities,
            "Move1" or "Move2" or "Move3" or "Move4" or "RelearnMove1" or "RelearnMove2" or "RelearnMove3" or "RelearnMove4" => src.Moves,
            "HeldItem" => src.Items, "Language" => src.Languages, "Version" => src.Games,
            "Gender" or "OriginalTrainerGender" => [new("수컷 ♂", 0), new("암컷 ♀", 1), new("성별 없음", 2)],
            "TeraTypeOverride" or "TeraTypeOriginal" => [.. Enumerable.Range(0, 18).Select(i => new ComboItem(s.types[i], i)), new("스텔라", 99)],
            _ => null,
        };
        string[] special = p switch
        {
            "Ribbons" => ["$suggestAll (가능한 리본 전부)", "$suggest (이 게임 리본)", "$suggestNone (모두 제거)"],
            "Moves" => ["$suggest (추천 기술)", "$rand (무작위)"],
            "PID" => ["$shiny (이로치)", "$shiny1 (별 이로치)", "$shiny0 (네모 이로치)", "$rand (무작위)"],
            "AbilityNumber" when apply => ["$0 (특성 1)", "$1 (특성 2)", "$2 (숨겨진 특성)"],
            "EVs" or "RelearnMoves" or "MetLocation" or "CurrentFriendship" or "Ball" or "CurrentLevel" or "HyperTrainFlags" when apply => ["$suggest (추천값)"],
            _ => [],
        };
        bool isBool = EntityBatchEditor.Instance.TryGetHasProperty(AppState.Sav.PKMType, p, out var pi) && pi.PropertyType == typeof(bool) || p == "Legal";
        if (isBool) { var b = await page.DisplayActionSheetAsync(p, "취소", null, "true (예)", "false (아니오)"); return b is null or "취소" ? null : b.Split(' ')[0]; }
        if (special.Length > 0)
        {
            var opts = special.ToList(); opts.Add(list != null ? "목록에서 고르기" : "직접 입력");
            var sp = await page.DisplayActionSheetAsync(p, "취소", null, opts.ToArray());
            if (sp is null or "취소") return null;
            if (sp.StartsWith('$')) return sp.Split(' ')[0];
        }
        if (list != null)
        {
            var tcs = new TaskCompletionSource<string>();
            var ps = new PickerSheet(p, list, -1, c => tcs.TrySetResult(c.Value.ToString())); ps.Closed += () => tcs.TrySetResult(null);
            SheetHost.Show(ps);
            return await tcs.Task;
        }
        return await page.DisplayPromptAsync(p, "값 ($suggest 추천값, 1,10 범위의 무작위 등도 가능)", "넣기", "취소");
    }

    private async Task Run(int scope)
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
        if (!await page.DisplayAlertAsync("일괄 수정", $"{slots.Count}마리에 적용합니다. 실행 취소는 되지 않습니다{(SaveStore.AutoBackup ? " (작업 전 세이브를 백업 목록에 남김)" : "")}.", "실행", "취소")) return;
        SaveStore.Snapshot("일괄수정");
        try
        {
            var mons = slots.Select(x => x.Pk).ToList();
            var before = mons.Select(x => { var b = new byte[x.SIZE_STORED]; x.WriteDecryptedDataStored(b); return b; }).ToList();
            var sets = BatchKo.Sets(lines); var report = new List<string>();
            await Task.Run(() =>
            {
                foreach (var set in sets)
                {
                    var hit = set.Custom.Count > 0 ? mons.Where(m => BatchKo.Matches(m, set.Filters)).ToList() : [];   // '@' 명령 대상은 바꾸기 전에 고름
                    if (set.HasPkhex)
                    {
                        var proc = EntityBatchProcessor.Execute(set.Pkhex, mons);
                        report.Add(proc.GetEditorResults(StringInstructionSet.GetBatchSets(set.Pkhex)));
                    }
                    int n = 0;
                    foreach (var m in hit) { bool any = false; foreach (var c in set.Custom) any |= BatchKo.ApplyCustom(m, c); if (any) n++; }
                    if (set.Custom.Count > 0) report.Add($"{string.Join(", ", set.Custom)}: {hit.Count}마리 중 {n}마리 변경");
                }
            });
            int changed = 0; var touched = new List<(int, int)>();
            for (int k = 0; k < slots.Count; k++)
            {
                var (b, i, p) = slots[k]; p.RefreshChecksum();
                var now = new byte[p.SIZE_STORED]; p.WriteDecryptedDataStored(now); if (!before[k].AsSpan().SequenceEqual(now)) { changed++; if (b >= 0) touched.Add((b, i)); }
                if (b < 0) sav.SetPartySlotAtIndex(p, i); else sav.SetBoxSlotAtIndex(p, b, i);
            }
            AppState.AfterBulk(touched);
            AppState.NotifyBox();
            int illegal = mons.Count(m => !AppState.IsLegal(m));
            result.Text = $"{slots.Count}마리 중 {changed}마리 변경{(illegal > 0 ? $" · 불법 {illegal}마리 (박스에서 확인)" : "")}\n" + string.Join("\n", report.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        catch (Exception ex) { result.Text = "실행 실패: " + ex.Message; }
    }
}

/// <summary>자주 쓰는 일괄 수정 명령 묶음 (이름 붙여 저장).</summary>
public static class BatchSets
{
    public record Item(string Name, string Text);
    private static string Path_ => Path.Combine(FileSystem.AppDataDirectory, "batch_sets.json");
    public static List<Item> Load() { try { if (File.Exists(Path_)) return System.Text.Json.JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(Path_)) ?? []; } catch { } return []; }
    public static void Save(string name, string text) { try { var l = Load(); l.RemoveAll(x => x.Name == name); l.Insert(0, new Item(name, text)); File.WriteAllText(Path_, System.Text.Json.JsonSerializer.Serialize(l)); } catch { } }
    public static void Remove(string name) { try { var l = Load(); l.RemoveAll(x => x.Name == name); File.WriteAllText(Path_, System.Text.Json.JsonSerializer.Serialize(l)); } catch { } }
}

public class BatchSetsSheet : Sheet
{
    public BatchSetsSheet(Action<string> pick) : base("일괄 수정 저장 목록", 0.8)
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        var items = BatchSets.Load();
        if (items.Count == 0) stack.Children.Add(T.L("저장된 묶음이 없습니다 · 명령어를 적고 '저장'을 누르세요", 13, sub: true));
        foreach (var it in items)
        {
            var name = T.L(it.Name, 14, bold: true);
            var body = T.L(it.Text.Length > 200 ? it.Text[..200] + "…" : it.Text, 12, sub: true); body.LineBreakMode = LineBreakMode.WordWrap;
            var use = T.Pill("불러오기", primary: true, size: 12); use.Clicked += (_, _) => { Close(); pick(it.Text); };
            var del = T.Pill("지우기", size: 12); del.Clicked += (_, _) => { BatchSets.Remove(it.Name); Close(); SheetHost.Show(new BatchSetsSheet(pick)); };
            stack.Children.Add(T.Card(new VerticalStackLayout { Spacing = 4, Children = { name, body, new HorizontalStackLayout { Spacing = 6, Children = { use, del } } } }, 10));
        }
        Body.Add(new ScrollView { Content = stack });
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
    private readonly Label bagInfo = T.L("", 12, sub: true);   // 주머니 종류·최대 개수 (가방 바로 아래)

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
        var liveRead = T.Pill("게임에서 읽기"); var liveWrite = T.Pill("게임에 쓰기");
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
                var nm = string.IsNullOrWhiteSpace(otE.Text) ? sav.OT : otE.Text.Trim();
                if (OldGenName.IsOld(sav) && OldGenName.HasJapanese(nm)) lang = (int)LanguageID.Japanese;
                AppState.SetTrainerFor(sav.Version, nm, gender, t, sd, lang);
                if (hasTime) { try { if (int.TryParse(hh.Text, out var h)) sav.PlayedHours = Math.Clamp(h, 0, 999); if (int.TryParse(mm.Text, out var m2)) sav.PlayedMinutes = Math.Clamp(m2, 0, 59); if (int.TryParse(ss.Text, out var s2)) sav.PlayedSeconds = Math.Clamp(s2, 0, 59); } catch { } }
                Dispatcher.Dispatch(async () =>   // 세이브 트레이너 + ALM 트레이너 + 게임별 저장값 갱신 (1~3세대 글자 체계 확인 포함)
                {
                    try
                    {
                        var msg = await OldGenName.ApplyChecked();
                        if (AppState.Sav != sav) { Close(); Note.Show(msg); return; }   // 세이브가 새로 만들어졌으면 시트 닫기
                        otE.Text = sav.OT;
                        status.Text = msg ?? "트레이너 정보를 세이브에 적용했습니다 (세이브 내보내기로 저장하세요)";
                    }
                    catch (Exception ex) { status.Text = "적용 실패: " + ex.Message; }
                });
            }
            catch (Exception ex) { status.Text = "적용 실패: " + ex.Message; }
        };
        var trLiveR = T.Pill("게임에서 읽기"); var trLiveW = T.Pill("게임에 쓰기");
        trLiveR.IsEnabled = trLiveW.IsEnabled = AppState.LiveConnected;
        trLiveR.Clicked += (_, _) => { status.Text = AppState.ReadLiveTrainer() ? "게임에서 트레이너 정보를 읽었습니다 (시트를 다시 열면 반영된 값이 보입니다)" : "이 게임은 트레이너 블록 읽기를 지원하지 않습니다"; };
        trLiveW.Clicked += (_, _) =>
        {
            foreach (var name in new[] { "Trainer Data", "MyStatus" })
            { try { AppState.Remote.Injector.WriteBlocksFromSAV(AppState.Remote, name, sav); status.Text = "게임에 트레이너 정보를 썼습니다"; return; } catch { } }
            status.Text = "이 게임은 트레이너 블록 쓰기를 지원하지 않습니다";
        };
        var trCard = UiFold.Card("세이브 트레이너", "pl_tr", true, new VerticalStackLayout { Spacing = 10, Children = { nameRow, idRow, T.Field("언어", lv), timeRow, trApply,
            T.Btns(2, trLiveR, trLiveW) } });
        var bagCard = UiFold.Card("가방 · 돈", "pl_bag", true, new VerticalStackLayout { Spacing = 10, Children = { hint, T.Field("돈", money),
            T.Btns(2, liveRead, liveWrite),
            new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = pouchBar, HorizontalScrollBarVisibility = ScrollBarVisibility.Never }, bagInfo, list, saveBtn } });
        var dexSeen = T.Pill("모두 본 것"); var dexCaught = T.Pill("모두 잡은 것", primary: true); var dexClear = T.Pill("모두 해제");
        dexClear.BackgroundColor = T.Bad; dexClear.TextColor = Colors.White; dexClear.BorderWidth = 0;
        dexSeen.Clicked += (_, _) => DexFill.Apply(0, m => status.Text = m);
        dexCaught.Clicked += (_, _) => DexFill.Apply(1, m => status.Text = m);
        dexClear.Clicked += async (_, _) => { if (await Application.Current.Windows[0].Page.DisplayAlertAsync("도감", "도감 기록을 모두 지울까요?", "해제", "취소")) DexFill.Apply(2, m => status.Text = m); };
        var dexOne = T.Pill("한 마리씩 편집", primary: true); dexOne.Clicked += (_, _) => SheetHost.Show(new DexEditSheet());
        var dexStats = T.Pill("진행 · 박스 통계"); dexStats.Clicked += (_, _) => SheetHost.Show(new StatsSheet());
        var dexHint = T.L("일괄: 이 게임에 나오는 종 전체 (게임에 따라 지원하지 않을 수 있음)", 12, sub: true); dexHint.LineBreakMode = LineBreakMode.WordWrap;
        var dexCard = UiFold.Card("도감", "pl_dex", false, new VerticalStackLayout { Spacing = 10, Children = { T.Btns(2, dexOne, dexStats), dexHint,
            T.Btns(3, dexSeen, dexCaught, dexClear) } });
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
        if (bag == null || bag.Pouches.Count == 0) { bagInfo.Text = "이 게임은 가방 편집을 지원하지 않습니다"; return; }
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
            if (slot == null) { bagInfo.Text = "이 주머니에 빈 칸이 없습니다"; return; }
            slot.Index = c.Value; if (slot.Count == 0) slot.Count = 1; Load();
        }, c => AppState.ItemSprite(c.Value))));
        al.Text = "+ 도구 추가";
        list.Children.Add(av);
        bagInfo.Text = $"{PouchName(pouch.Type)}: {pouch.Count}종 · 최대 개수 {pouch.MaxCount}";
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
        var hint = T.L("세이브 여러 개를 열어 두고 오가며 편집 · 포켓몬은 복사·붙여넣기나 '여기로 보내기'로 (게임 형식 자동 변환)\n라이브헥스는 연결한 인스턴스 하나만", 12, sub: true);
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
