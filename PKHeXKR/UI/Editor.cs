using PKHeX.Core;
namespace PKHeXKR;

public static class Note
{
    public static void Show(string text)
    {
        try { CommunityToolkit.Maui.Alerts.Toast.Make(text).Show(); } catch { }
    }
}

/// <summary>편집 섹션 공통: Load 중에는 변경 이벤트를 무시.</summary>
public abstract class Section : ContentView
{
    protected bool Loading;
    protected static PKM Pk => AppState.Pk;
    protected static Page Page => Application.Current.Windows[0].Page;
    public void Reload() { Loading = true; try { Load(Pk); } catch (Exception ex) { Note.Show($"{GetType().Name} 표시 오류: {ex.Message}"); } finally { Loading = false; } }
    protected abstract void Load(PKM pk);
    protected void Changed(Action a)
    {
        if (Loading) return;
        if (Pk.Species == 0) { Note.Show("먼저 포켓몬을 불러오거나 선택하세요"); Reload(); return; }   // 빈 개체 편집으로 인한 강제 종료 방지
        AppState.Checkpoint();
        try { a(); } catch (Exception ex) { Note.Show("적용하지 못했습니다: " + ex.Message); }
        AppState.Edited();
    }

    protected static void Pick(string title, IReadOnlyList<ComboItem> list, int cur, Action<ComboItem> on, Func<ComboItem, string> icon = null)
        => SheetHost.Show(new PickerSheet(title, list, cur, on, icon));

    protected static string NameOf(IReadOnlyList<ComboItem> list, int v) => list.FirstOrDefault(x => x.Value == v)?.Text ?? v.ToString();

    protected static Entry Num(Action<int> set, int max = 65535)
    {
        var e = T.Input(Keyboard.Numeric);
        e.TextChanged += (_, a) =>
        {
            if (!long.TryParse(a.NewTextValue, out var v)) return;
            if (v > max) { e.Text = max.ToString(); return; }   // 최대치를 넘으면 최대치로 바꿔 표시 (바뀐 값으로 다시 적용됨)
            set((int)Math.Clamp(v, 0, max));
        };
        return e;
    }
    protected static View Switch(string label, out Switch sw)
    {
        sw = new Switch { OnColor = T.Accent, VerticalOptions = LayoutOptions.Center };
        var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, HeightRequest = 44 };
        g.Add(T.L(label, 15), 0); g.Add(sw, 1);
        return g;
    }
    protected static VerticalStackLayout Stack(params View[] v) { var s = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(0, 4, 0, 24) }; foreach (var x in v) s.Children.Add(x); return s; }
    /// <summary>머리글을 눌러 접고 펴는 카드. 상태는 기기에 저장.</summary>
    protected static View FoldCard(string title, View inner, string key, bool def = false)
    {
        bool open = Preferences.Get(key, def);
        var head = T.L((open ? "▾  " : "▸  ") + title, 15, bold: true);
        inner.IsVisible = open;
        var t = new TapGestureRecognizer();
        t.Tapped += (_, _) => { open = !open; Preferences.Set(key, open); inner.IsVisible = open; head.Text = (open ? "▾  " : "▸  ") + title; };
        head.GestureRecognizers.Add(t);
        return T.Card(new VerticalStackLayout { Spacing = 10, Children = { head, inner } });
    }
    /// <summary>한 줄에 여러 개 넣는 작은 버튼.</summary>
    protected static Button Small(string text) { var b = T.Pill(text, size: 12); b.Padding = new Thickness(4, 0); b.HeightRequest = 34; b.HorizontalOptions = LayoutOptions.Fill; return b; }
    protected static Label Wrap(Label l) { l.LineBreakMode = LineBreakMode.WordWrap; l.MaxLines = -1; return l; }
    protected static Grid Two(View a, View b) { var g = T.Cols(2); g.Add(a, 0); g.Add(b, 1); return g; }
}

// ======================= 기본 =======================
public class MainSection : Section
{
    private Button mintBtn;
    private readonly Label species, form, nature, statNature, ability, item, ball, lang;
    private readonly View formRow, statNatureRow, abilityRow, natureRow, ballRow;
    private readonly Entry nick, level, friend;
    private readonly Switch nickSw, shinySw, eggSw;
    private readonly Button genderBtn;
    private readonly Entry formArg, dmax;
    private readonly Label tera, teraOv, plusState;
    private readonly Switch gmax, alpha;
    private readonly View formArgRow, dmaxRow, gmaxRow, teraRow, alphaRow, plusRow, gameCard;

    public MainSection()
    {
        (var sv, species) = T.Chooser(() => { var fam = AppState.Family(Pk.Species); SheetHost.Show(new PickerSheet("포켓몬", AppState.Src.Species, Pk.Species, c => { AppState.Checkpoint(true); SetSpecies((ushort)c.Value); AppState.Edited(); }, c => AppState.Sprite((ushort)c.Value, 0, false), c => fam.Contains((ushort)c.Value) ? 0 : 2)); });
        (var fv, form) = T.Chooser(() =>
        {
            var names = Forms(Pk.Species);
            if (names.Length <= 1) { Note.Show("이 포켓몬은 다른 폼이 없습니다"); return; }
            Pick("폼", names.Select((n, i) => new ComboItem(n, i)).ToList(), Pk.Form, c => Changed(() => { Pk.Form = (byte)c.Value; Pk.SetGender(Pk.GetSaneGender()); Reload(); }), c => AppState.Sprite(Pk.Species, (byte)c.Value, false));
        });
        formRow = T.Field("폼", fv);
        nick = T.Input(); nick.TextChanged += (_, e) => Changed(() => { Pk.Nickname = e.NewTextValue ?? ""; AppState.ClearNickTrash(Pk); });
        var nickRow = Switch("닉네임 직접 지정", out nickSw);
        nickSw.Toggled += (_, e) => Changed(() => { Pk.IsNicknamed = e.Value; if (!e.Value) { ResetNickname(); Reload(); } });
        level = Num(v => Changed(() => { int old = Pk.CurrentLevel; Pk.CurrentLevel = (byte)Math.Clamp(v, 1, 100); if (old != Pk.CurrentLevel) AppState.SyncPlusForLevel(Pk, old); }), 100);
        friend = Num(v => Changed(() => { Pk.CurrentFriendship = (byte)v; }), 255);
        genderBtn = T.Pill("♂"); genderBtn.HeightRequest = 44;
        genderBtn.Clicked += (_, _) => Changed(() => CycleGender());
        (var nv, nature) = T.Chooser(() => Pick("성격", AppState.NatureItems(), (int)Pk.Nature, c => Changed(() => { Pk.Nature = (Nature)c.Value; if (Pk.Format < 8) Pk.StatAlignment = Pk.Nature; Reload(); })));
        mintBtn = T.Pill("(민트)", size: 11); mintBtn.Padding = new Thickness(8, 0); mintBtn.HeightRequest = 36;
        mintBtn.Clicked += (_, _) => Pick("능력 성격(민트)", AppState.NatureItems(), (int)Pk.StatAlignment, c => Changed(() => { Pk.StatAlignment = (Nature)c.Value; Reload(); }));
        var natGrid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 6 }; natGrid.Add(nv, 0); natGrid.Add(mintBtn, 1);
        natureRow = T.Field("성격", natGrid);
        (var snv, statNature) = T.Chooser(() => Pick("능력 성격(민트)", AppState.NatureItems(), (int)Pk.StatAlignment, c => Changed(() => { Pk.StatAlignment = (Nature)c.Value; Reload(); })));
        statNatureRow = T.Field("능력 성격 (민트)", snv);
        (var av, ability) = T.Chooser(() =>
        {
            if (AppState.HaX) { Pick("특성 (PKHaX: 전체)", AppState.Src.Abilities, Pk.Ability, c => Changed(() => { Pk.Ability = c.Value; Reload(); })); return; }
            var list = AppState.Src.GetAbilityList(Pk.PersonalInfo).Select((c, i) => new ComboItem(c.Text, i)).ToList();
            Pick("특성", list, AbilityIndex(), c => Changed(() => { Pk.RefreshAbility(c.Value); Reload(); }));
        });
        abilityRow = T.Field("특성", av);
        (var iv, item) = T.Chooser(() => Pick("지닌 물건", HeldItems(), Pk.HeldItem, c => Changed(() => { Pk.HeldItem = c.Value; Reload(); }), c => AppState.ItemSprite(c.Value)));
        (var bv, ball) = T.Chooser(() => { var ok = LegalTools.LegalBalls(Pk); SheetHost.Show(new PickerSheet("볼", AppState.Src.Balls, Pk.Ball, c => Changed(() => { Pk.Ball = (byte)c.Value; Reload(); }), c => AppState.BallSprite(c.Value), c => ok.Contains(c.Value) ? 0 : 2)); });
        ballRow = T.Field("볼", bv);
        (var lv, lang) = T.Chooser(() => Pick("언어", AppState.Src.Languages, Pk.Language, c => Changed(() => { Pk.Language = c.Value; if (!Pk.IsNicknamed) ResetNickname(); Reload(); })));
        var shinyRow = Switch("이로치", out shinySw);
        shinySw.Toggled += (_, e) => Changed(() => { if (e.Value) Pk.SetShiny(); else Pk.SetUnshiny(); });
        var eggRow = Switch("알 상태", out eggSw);
        eggSw.Toggled += (_, e) => Changed(() => { Pk.IsEgg = e.Value; });

        // 폼 인수 (타부자고·대쓰여너·도롱충이 등)
        formArg = Num(v => Changed(() => { if (Pk is IFormArgument fa) fa.ChangeFormArgument(Pk.Species, Pk.Form, Pk.Context, (uint)v); }), int.MaxValue);
        var faMax = T.Pill("최댓값"); faMax.HeightRequest = 44;
        faMax.Clicked += (_, _) => Changed(() => { if (Pk is IFormArgument fa) fa.ChangeFormArgument(Pk.Species, Pk.Form, Pk.Context, FormArgumentUtil.GetFormArgumentMax(Pk.Species, Pk.Form, Pk.Context)); Reload(); });
        var faGrid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 }; faGrid.Add(formArg, 0); faGrid.Add(faMax, 1);
        formArgRow = T.Field("폼 인수 (진화·폼 조건 값)", faGrid);

        // 게임 전용 항목
        dmax = Num(v => Changed(() => { if (Pk is IDynamaxLevel d) d.DynamaxLevel = (byte)Math.Min(v, 10); }), 10);
        dmaxRow = T.Field("다이맥스 레벨 (0~10)", dmax);
        gmaxRow = Switch("거다이맥스 가능", out gmax);
        gmax.Toggled += (_, e) => Changed(() => { if (Pk is IGigantamax g) g.CanGigantamax = e.Value; });
        (var tv, tera) = T.Chooser(() => { if (Pk is ITeraType t) Pick("테라스탈 타입", TeraList(false), (int)t.TeraTypeOriginal, c => Changed(() => { t.TeraTypeOriginal = (MoveType)c.Value; Reload(); })); });
        (var tov, teraOv) = T.Chooser(() => { if (Pk is ITeraType t) Pick("변경된 테라스탈 타입", TeraList(true), (int)t.TeraTypeOverride, c => Changed(() => { t.TeraTypeOverride = (MoveType)c.Value; Reload(); })); });
        teraRow = Two(T.Field("테라스탈 타입", tv), T.Field("변경(테라피스)", tov));
        alphaRow = Switch("우두머리(알파)", out alpha);
        alpha.Toggled += (_, e) => Changed(() => { if (Pk is IAlpha a) a.IsAlpha = e.Value; });
        plusState = T.L("", 13, sub: true);
        var plusCur = T.Pill("현재 기술 기준으로 기술플러스"); plusCur.Clicked += (_, _) => Changed(() => SetPlus(PlusRecordApplicatorOption.LegalCurrent));
        var plusTm = T.Pill("익힐 수 있는 기술 전부"); plusTm.Clicked += (_, _) => Changed(() => SetPlus(PlusRecordApplicatorOption.LegalSeedTM));
        var plusNone = T.Pill("기술플러스 해제"); plusNone.Clicked += (_, _) => Changed(() => SetPlus(PlusRecordApplicatorOption.None));
        var plusBtns = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { plusCur, plusTm, plusNone } };
        foreach (var b in plusBtns.Children.OfType<View>()) b.Margin = new Thickness(0, 0, 6, 6);
        plusRow = new VerticalStackLayout { Spacing = 6, Children = { T.L("기술플러스 (레전즈 Z-A)", 13, sub: true), plusState, plusBtns } };
        gameCard = T.Card(Stack(T.L("게임 전용", 13, sub: true), alphaRow, plusRow));   // 다이맥스·테라스탈은 능력치 탭으로

        Content = Stack(
            T.Card(Stack(Two(T.Field("포켓몬", sv), formRow), formArgRow, T.Field("닉네임", nick), nickRow)),
            T.Card(Stack(Two(T.Field("레벨", LevelRow()), T.Field("성별", genderBtn)), Two(natureRow, statNatureRow), Two(abilityRow, T.Field("지닌 물건", iv)))),
            T.Card(Stack(Two(shinyRow, eggRow), Two(T.Field("친밀도", friend), T.Field("언어", lv)))), gameCard);
    }

    internal static List<ComboItem> TeraList(bool withNone)
    {
        var t = GameInfo.Strings.types; var l = new List<ComboItem>();
        for (int i = 0; i < 18 && i < t.Length; i++) l.Add(new ComboItem(t[i], i));
        l.Add(new ComboItem("스텔라", TeraTypeUtil.Stellar));
        if (withNone) l.Insert(0, new ComboItem("없음 (원래 타입)", TeraTypeUtil.OverrideNone));
        return l;
    }
    internal static string TeraName(int v) => v == TeraTypeUtil.OverrideNone ? "없음 (원래 타입)" : v == TeraTypeUtil.Stellar ? "스텔라" : v < GameInfo.Strings.types.Length ? GameInfo.Strings.types[v] : v.ToString();
    private void SetPlus(PlusRecordApplicatorOption opt)
    {
        if (Pk is PA9 z && z.PersonalInfo is IPermitPlus pp) { z.SetPlusFlags(pp, opt); Reload(); }
    }

    /// <summary>폼 이름 목록. 세이브 게임의 폼 수를 기준으로, 이름이 없으면 "폼 n"으로 채움.</summary>
    public static string[] Forms(ushort sp)
    {
        var s = GameInfo.Strings;
        string[] names;
        try { names = FormConverter.GetFormList(sp, s.types, s.forms, GameInfo.GenderSymbolUnicode, AppState.Sav.Context); } catch { names = [""]; }
        int count = 1;
        try { count = AppState.Sav.Personal.GetFormEntry(sp, 0).FormCount; } catch { }
        count = Math.Max(count, names.Length);
        return Enumerable.Range(0, count).Select(i => i < names.Length && !string.IsNullOrWhiteSpace(names[i]) ? names[i] : (i == 0 ? "기본" : $"폼 {i}")).ToArray();
    }
    private int AbilityIndex() => Pk.AbilityNumber switch { 1 => 0, 2 => 1, 4 => 2, _ => 0 };

    private void SetSpecies(ushort sp)
    {
        try { SetSpeciesCore(sp); } finally { if (Pk is IScaledSizeValue sz && Pk.Species != 0) { sz.ResetHeight(); sz.ResetWeight(); } }
    }
    private void SetSpeciesCore(ushort sp)
    {
        var prev = Pk.Species;
        if (Pk.Species == 0)   // 빈 칸에서 시작: 세이브 트레이너로 새 개체
        {
            var s = AppState.Sav;
            Pk.OriginalTrainerName = s.OT; Pk.ID32 = s.ID32; Pk.OriginalTrainerGender = s.Gender; Pk.Language = s.Language > 0 ? s.Language : (int)LanguageID.Korean;
            Pk.Version = s.Version; Pk.CurrentLevel = 50; Pk.Ball = (byte)Ball.Poke; Pk.SetRandomEC();
        }
        var keepAbility = Pk.Ability; var prevForm = Pk.Form;
        Pk.Species = sp; Pk.Form = 0;
        // 리전폼 진화(가라르 야돈 → 가라르 야도킹 등): 새 종에 같은 폼 번호가 있으면 유지한 쪽과 기본폼 중 합법성이 나은 쪽
        if (prev != 0 && prev != sp && prevForm > 0 && Pk.PersonalInfo.FormCount > prevForm)
        {
            try
            {
                int bad0 = new LegalityAnalysis(Pk).Results.Count(r => !r.Valid);
                Pk.Form = prevForm;
                int badK = new LegalityAnalysis(Pk).Results.Count(r => !r.Valid);
                if (badK > bad0) Pk.Form = 0;
            }
            catch { Pk.Form = 0; }
        }
        Pk.SetGender(Pk.GetSaneGender());
        // Z-A: 종이 바뀌어도(진화) 특성·특성 번호를 그대로 유지해야 조우가 맞음 (모으령→타부자고, 가라르 야돈→가라르 야도킹 등)
        if (!(prev != 0 && prev != sp && Pk.Context == EntityContext.Gen9a))
            Pk.RefreshAbility(Math.Min(AbilityIndex(), Pk.PersonalInfo.AbilityCount - 1));
        // 진화처럼 종만 바꾼 경우: 원래 특성을 유지해야 합법인 게임이 있음 (예: Z-A 모으령→타부자고는 '주눅' 유지, 바꾸면 조우 불일치)
        if (prev != 0 && prev != sp && keepAbility != Pk.Ability)
        {
            try
            {
                var refreshed = Pk.Ability;
                int bad1 = new LegalityAnalysis(Pk).Results.Count(r => !r.Valid);
                Pk.Ability = keepAbility;
                int bad0 = new LegalityAnalysis(Pk).Results.Count(r => !r.Valid);
                if (bad1 < bad0) Pk.Ability = refreshed;   // 새 특성이 더 나을 때만 새 특성
            }
            catch { }
        }
        if (!Pk.IsNicknamed) ResetNickname();
        try
        {
            if (prev != 0 && Pk is IFormArgument fa)
            {
                var min = FormArgumentUtil.GetFormArgumentMinEvolution(sp, prev);   // 예: 타부자고 = 모으령 코인 999
                if (min > fa.FormArgument)
                {
                    var keep = fa.FormArgument;
                    fa.ChangeFormArgument(sp, Pk.Form, Pk.Context, min);
                    // 이 게임에서 폼 인수를 쓰지 않는 진화라면(예: Z-A) 되돌림
                    if (new LegalityAnalysis(Pk).Results.Any(r => !r.Valid && r.Identifier == CheckIdentifier.Form)) fa.FormArgument = keep;
                }
            }
            // Z-A: 진화하면 새 종이 반드시 가져야 하는 기술플러스가 생김 → 현재 기술 기준으로 채움 (결과가 나빠지면 되돌림)
            if (prev != 0 && prev != sp && Pk is IPlusRecord rec && Pk.PersonalInfo is IPermitPlus permit)
            {
                var before = new LegalityAnalysis(Pk).Results.Count(r => !r.Valid);
                var c = Pk.Clone(); ((IPlusRecord)c).SetPlusFlags(c, (IPermitPlus)c.PersonalInfo, PlusRecordApplicatorOption.LegalCurrent);
                if (new LegalityAnalysis(c).Results.Count(r => !r.Valid) <= before) rec.SetPlusFlags(Pk, permit, PlusRecordApplicatorOption.LegalCurrent);
            }
        }
        catch { }
        Reload();
    }
    private static List<ComboItem> HeldItems()
    {
        bool Ok(ComboItem c)
        {
            if (c.Value == 0) return true;
            if (string.IsNullOrWhiteSpace(c.Text) || c.Text.StartsWith('☆') || c.Text.Contains("???") || c.Text.StartsWith('(')) return false;
            try { return ItemRestrictions.IsHeldItemAllowed(c.Value, Pk.Context); } catch { return true; }
        }
        return AppState.Src.Items.Where(Ok).ToList();
    }
    private View LevelRow()
    {
        var max = T.Pill("100", size: 12); max.WidthRequest = 50; max.Padding = 0; max.HeightRequest = 36;
        max.Clicked += (_, _) => Changed(() => { int old = Pk.CurrentLevel; Pk.CurrentLevel = 100; AppState.SyncPlusForLevel(Pk, old); Reload(); });
        var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 4 };
        g.Add(level, 0); g.Add(max, 1);
        return g;
    }
    private void ResetNickname() { Pk.Nickname = SpeciesName.GetSpeciesNameGeneration(Pk.Species, Pk.Language, Pk.Format); Pk.IsNicknamed = false; }
    private void CycleGender()
    {
        var pi = Pk.PersonalInfo;
        if (pi.Genderless || pi.OnlyMale || pi.OnlyFemale) { Pk.SetGender(Pk.GetSaneGender()); }
        else Pk.SetGender((byte)(Pk.Gender == 0 ? 1 : 0));
        Reload();
    }

    protected override void Load(PKM pk)
    {
        var forms = Forms(pk.Species);
        species.Text = pk.Species == 0 ? "(선택)" : AppState.SpeciesName(pk);
        formRow.IsVisible = pk.Species != 0;
        form.Text = forms.Length <= 1 ? "(폼 없음)" : pk.Form < forms.Length ? forms[pk.Form] : pk.Form.ToString();
        formArgRow.IsVisible = pk is IFormArgument && FormArgumentUtil.GetFormArgumentMax(pk.Species, pk.Form, pk.Context) > 0;
        if (pk is IFormArgument fa) formArg.Text = fa.FormArgument.ToString();
        dmaxRow.IsVisible = pk is IDynamaxLevel; if (pk is IDynamaxLevel d) dmax.Text = d.DynamaxLevel.ToString();
        gmaxRow.IsVisible = pk is IGigantamax; if (pk is IGigantamax g) gmax.IsToggled = g.CanGigantamax;
        teraRow.IsVisible = pk is ITeraType; if (pk is ITeraType t) { tera.Text = TeraName((int)t.TeraTypeOriginal); teraOv.Text = TeraName((int)t.TeraTypeOverride); }
        alphaRow.IsVisible = pk is IAlpha; if (pk is IAlpha a) alpha.IsToggled = a.IsAlpha;
        plusRow.IsVisible = pk is PA9; if (pk is IPlusRecord pr) plusState.Text = pr.GetMovePlusFlagAny() ? "기술플러스 기록 있음" : "기술플러스 기록 없음";
        gameCard.IsVisible = pk.Species != 0 && (alphaRow.IsVisible || plusRow.IsVisible);
        nick.Text = pk.Nickname; nickSw.IsToggled = pk.IsNicknamed;
        level.Text = pk.CurrentLevel.ToString(); friend.Text = pk.CurrentFriendship.ToString();
        genderBtn.Text = pk.Gender switch { 0 => "♂ 수컷", 1 => "♀ 암컷", _ => "— 무성" };
        T.GenderTint(genderBtn, pk.Gender);
        bool g3 = pk.Format >= 3;
        natureRow.IsVisible = abilityRow.IsVisible = ballRow.IsVisible = g3;
        statNatureRow.IsVisible = pk.Format >= 8;
        nature.Text = NameOf(AppState.Src.Natures, (int)pk.Nature); statNature.Text = NameOf(AppState.Src.Natures, (int)pk.StatAlignment);
        if (mintBtn != null) { mintBtn.IsVisible = false;   // 민트는 편집기 상단 성격 옆 (괄호)에서
            mintBtn.Text = $"(민트: {NameOf(AppState.Src.Natures, (int)pk.StatAlignment)})"; }
        var abil = AppState.Src.GetAbilityList(pk.PersonalInfo);
        string AbilName(int a) => a >= 0 && a < GameInfo.Strings.abilitylist.Length ? GameInfo.Strings.abilitylist[a] : "-";
        ability.Text = pk.Format < 3 ? "-" : AppState.HaX ? AbilName(pk.Ability) : AbilityIndex() < abil.Count ? abil[AbilityIndex()].Text : AbilName(pk.Ability);
        item.Text = pk.HeldItem == 0 ? "없음" : NameOf(AppState.Src.Items, pk.HeldItem);
        ball.Text = NameOf(AppState.Src.Balls, pk.Ball);
        lang.Text = NameOf(AppState.Src.Languages, pk.Language);
        shinySw.IsToggled = pk.IsShiny; eggSw.IsToggled = pk.IsEgg;

    }
}

// ======================= 능력치 =======================
public class StatsSection : Section
{
    private static readonly string[] Names = ["HP", "공격", "방어", "특공", "특방", "스피드"];
    private readonly Label[] baseL = new Label[6], statL = new Label[6];
    private readonly Entry[] ivE = new Entry[6], evE = new Entry[6];
    private readonly CheckBox[] htC = new CheckBox[6];
    private readonly Label evTotal = new() { FontSize = 13, FontAttributes = FontAttributes.Bold };
    private readonly Label baseTotal = new() { FontSize = 13, FontAttributes = FontAttributes.Bold };
    private readonly Label hpType = new() { FontSize = 13, FontAttributes = FontAttributes.Bold, TextDecorations = TextDecorations.Underline };
    private static bool GetHT(IHyperTrain h, int i) => i switch { 0 => h.HT_HP, 1 => h.HT_ATK, 2 => h.HT_DEF, 3 => h.HT_SPA, 4 => h.HT_SPD, _ => h.HT_SPE };
    private static void SetHT(IHyperTrain h, int i, bool v) { switch (i) { case 0: h.HT_HP = v; break; case 1: h.HT_ATK = v; break; case 2: h.HT_DEF = v; break; case 3: h.HT_SPA = v; break; case 4: h.HT_SPD = v; break; default: h.HT_SPE = v; break; } }

    /// <summary>작은 ± 버튼 (최대 ↔ 최소).</summary>
    private static Button Pm(Action click)
    {
        var b = new Button { Text = "±", FontSize = 12, Padding = 0, WidthRequest = 26, HeightRequest = 26, CornerRadius = 13, BorderWidth = 1, VerticalOptions = LayoutOptions.Center };
        b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); b.BorderColor = T.Accent; b.TextColor = T.Accent;
        b.Clicked += (_, _) => click(); return b;
    }
    private static Grid Cell(View entry, View btn) { var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 2, VerticalOptions = LayoutOptions.Center }; g.Add(entry, 0); g.Add(btn, 1); return g; }
    private Label evHead;
    private Button evRndBtn, ev0Btn;
    private bool AV => Pk is IAwakened;   // 레츠고: 노력치 대신 각성치(AV, 0~200) → CP
    private int EvCap(int k) { if (AV) return 200; int others = Enumerable.Range(0, 6).Where(j => j != k).Sum(GetEV); return Pk.Format < 3 ? Pk.MaxEV : Math.Max(0, Math.Min(252, 510 - others)); }

    public StatsSection()
    {
        var g = new Grid { ColumnSpacing = 6, RowSpacing = 6, ColumnDefinitions = { new(GridLength.Auto), new(new GridLength(0.9, GridUnitType.Star)), new(new GridLength(1.5, GridUnitType.Star)), new(new GridLength(1.6, GridUnitType.Star)), new(new GridLength(0.9, GridUnitType.Star)), new(GridLength.Auto) } };
        string[] head = ["", "종족값", "개체값", "노력치", "능력치", "특훈"];
        g.RowDefinitions.Add(new(GridLength.Auto));
        for (int c = 0; c < 6; c++) { var h = T.L(head[c], 12, sub: true); h.HorizontalTextAlignment = TextAlignment.Center; h.HorizontalOptions = LayoutOptions.Fill; g.Add(h, c, 0); if (c == 3) evHead = h; }
        for (int i = 0; i < 6; i++)
        {
            int k = i;
            g.RowDefinitions.Add(new(GridLength.Auto));
            baseL[i] = T.L("", 14, sub: true); statL[i] = T.L("", 15, bold: true);
            ivE[i] = Num(v => Changed(() => { int val = Math.Min(v, Pk.MaxIV); SetIV(k, val); Recalc(); if (val != v) { var e = ivE[k]; e.Dispatcher.Dispatch(() => e.Text = val.ToString()); } }), 31);
            evE[i] = Num(v => Changed(() =>
            {
                // 노력치: 한 능력치 최대 252, 합계 최대 510 → 넘치면 들어갈 수 있는 만큼으로 자동 조정 (1·2세대는 0~65535)
                int val = Math.Min(v, EvCap(k));
                SetEV(k, val); Recalc();
                if (val != v) { var e = evE[k]; e.Dispatcher.Dispatch(() => e.Text = val.ToString()); }
            }), 65535);
            ivE[i].HorizontalTextAlignment = evE[i].HorizontalTextAlignment = TextAlignment.Center;
            foreach (var l in new[] { baseL[i], statL[i] }) { l.HorizontalTextAlignment = TextAlignment.Center; l.VerticalTextAlignment = TextAlignment.Center; l.HorizontalOptions = LayoutOptions.Fill; l.VerticalOptions = LayoutOptions.Center; }
            // ± : 최대가 아니면 최대로, 최대면 0으로 (개체값 V ↔ Z, 노력치 최대 ↔ 0)
            // 개체값 버튼: 그 밖의 값 → V(최대), V → U(최대-1), U → Z(0), Z → V
            var ivPm = Pm(() => { int m = Pk.MaxIV, v = GetIV(k); ivE[k].Text = (v == m ? m - 1 : v == m - 1 ? 0 : m).ToString(); });
            var evPm = Pm(() => { int cap = EvCap(k); evE[k].Text = (GetEV(k) >= cap && cap > 0 ? 0 : cap).ToString(); });
            var nameL = T.L(Names[i], 14, bold: true); nameL.VerticalOptions = LayoutOptions.Center;
            g.Add(nameL, 0, i + 1); g.Add(baseL[i], 1, i + 1); g.Add(Cell(ivE[i], ivPm), 2, i + 1); g.Add(Cell(evE[i], evPm), 3, i + 1); g.Add(statL[i], 4, i + 1);
            htC[i] = new CheckBox { Color = T.Accent, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
            htC[i].CheckedChanged += (_, e) => Changed(() => { if (Pk is IHyperTrain h) SetHT(h, k, e.Value); Recalc(); });
            g.Add(htC[i], 5, i + 1);
        }
        // 아래 줄: 종족값 합계 · 잠재파워 타입(누르면 변경) · 노력치 합계 · 특훈 전부
        g.RowDefinitions.Add(new(GridLength.Auto));
        g.Add(T.L("합계", 12, sub: true), 0, 7);
        foreach (var l in new[] { baseTotal, hpType, evTotal }) { l.HorizontalTextAlignment = TextAlignment.Center; l.VerticalTextAlignment = TextAlignment.Center; }
        g.Add(baseTotal, 1, 7); g.Add(hpType, 2, 7); g.Add(evTotal, 3, 7);
        hpType.TextColor = T.Accent;
        var hpTap = new TapGestureRecognizer(); hpTap.Tapped += (_, _) => PickHiddenPower(); hpType.GestureRecognizers.Add(hpTap);
        var htAll = new Button { Text = "전부", FontSize = 10, Padding = new Thickness(4, 0), HeightRequest = 26, CornerRadius = 13, BorderWidth = 1, BorderColor = T.Accent, TextColor = T.Accent, VerticalOptions = LayoutOptions.Center };
        htAll.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24"));
        htAll.Clicked += (_, _) => Changed(() =>
        {
            if (Pk is not IHyperTrain h) return;
            var want = Enumerable.Range(0, 6).Where(i => GetIV(i) < 31).ToList();   // 개체값 31은 자동 제외
            bool allOn = want.Count > 0 && want.All(i => GetHT(h, i));
            for (int i = 0; i < 6; i++) SetHT(h, i, !allOn && GetIV(i) < 31);   // 이미 전부 켜져 있으면 모두 해제
            Reload();
        });
        g.Add(htAll, 5, 7); htAllBtn = htAll;

        var rnd = Small("IV 무작위"); rnd.Clicked += (_, _) => Changed(() =>
        {
            // 조우가 보장하는 V 개수(우두머리 3V 등)만큼 무작위 칸을 31로
            int flawless = Pk is IAlpha { IsAlpha: true } ? 3 : 0;
            try { if (new LegalityAnalysis(Pk).EncounterMatch is IFlawlessIVCount fc) flawless = Math.Max(flawless, fc.FlawlessIVCount); } catch { }
            for (int i = 0; i < 6; i++) SetIV(i, Random.Shared.Next(Pk.MaxIV + 1));
            foreach (var i in Enumerable.Range(0, 6).OrderBy(_ => Random.Shared.Next()).Take(flawless)) SetIV(i, Pk.MaxIV);
            Reload();
        });
        var evR = Small("EV 무작위"); evR.Clicked += (_, _) => Changed(() =>
        {
            for (int i = 0; i < 6; i++) SetEV(i, 0);
            if (Pk.Format < 3) { for (int i = 0; i < 6; i++) SetEV(i, Random.Shared.Next(Pk.MaxEV + 1)); }
            else if (AV) { for (int i = 0; i < 6; i++) SetEV(i, Random.Shared.Next(201)); }
            else { int left = 510; foreach (var i in Enumerable.Range(0, 6).OrderBy(_ => Random.Shared.Next())) { int v = Random.Shared.Next(Math.Min(252, left) + 1); SetEV(i, v); left -= v; } }
            Reload();
        });
        var ev0 = Small("EV 0"); ev0.Clicked += (_, _) => Changed(() => { for (int i = 0; i < 6; i++) SetEV(i, 0); Reload(); });
        evRndBtn = evR; ev0Btn = ev0;
        var v6 = Small("6V"); v6.Clicked += (_, _) => Changed(() => { for (int i = 0; i < 6; i++) SetIV(i, Pk.MaxIV); Reload(); });
        foreach (var bb in new[] { rnd, v6, evR, ev0 }) { bb.FontSize = 12; bb.Padding = new Thickness(2, 0); }
        var buttons = T.Cols(4, 4); buttons.Add(rnd, 0); buttons.Add(v6, 1); buttons.Add(evR, 2); buttons.Add(ev0, 3);

        // 크기: 키·몸무게·배율 한 줄, 각 칸 옆에 작은 최대/최소
        hE = Num(v => Changed(() => { if (Pk is IScaledSize z) z.HeightScalar = (byte)Math.Min(v, 255); UpdateSize(); }), 255);
        wE = Num(v => Changed(() => { if (Pk is IScaledSize z) z.WeightScalar = (byte)Math.Min(v, 255); UpdateSize(); }), 255);
        sE = Num(v => Changed(() => { if (Pk is IScaledSize3 z) z.Scale = (byte)Math.Min(v, 255); if (AppState.SyncHomeScale(Pk) && Pk is IScaledSize zz) { hE.Text = zz.HeightScalar.ToString(); wE.Text = zz.WeightScalar.ToString(); } UpdateSize(); }), 255);   // 홈 트래커가 있으면 키(·Z-A 몸무게)도 배율과 같이
        static Button Tiny(string t, Action a) { var b = new Button { Text = t, FontSize = 9, Padding = 0, WidthRequest = 30, HeightRequest = 20, CornerRadius = 6, BorderWidth = 1, BorderColor = T.Accent, TextColor = T.Accent }; b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); b.Clicked += (_, _) => a(); return b; }
        View SizeField(string label, Entry e)
        {
            e.HorizontalTextAlignment = TextAlignment.Center;
            var btns = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { Tiny("최대", () => e.Text = "255"), Tiny("최소", () => e.Text = "0") } };
            return T.Field(label, Cell(e, btns));
        }
        sRow = SizeField("배율", sE);
        var sizeRow = T.Cols(3, 6); sizeRow.Add(SizeField("키", hE), 0); sizeRow.Add(SizeField("몸무게", wE), 1); sizeRow.Add(sRow, 2);
        var rnd2 = Small("크기 무작위"); rnd2.Clicked += (_, _) => Changed(() =>
        {
            if (Pk is IScaledSize z) { z.HeightScalar = (byte)Random.Shared.Next(256); z.WeightScalar = (byte)Random.Shared.Next(256); }
            if (Pk is IScaledSize3 z3) z3.Scale = (byte)Random.Shared.Next(256);
            AppState.SyncHomeScale(Pk);
            Reload();
        });
        sizeCard = T.Card(new VerticalStackLayout { Spacing = 8, Children = { T.L("크기 (0~255)", 13, sub: true), sizeRow, sizeInfo, rnd2,
            Wrap(T.L("크기는 조우마다 정해진 방식이 있어 임의로 바꾸면 불법이 될 수 있습니다 (예: Z-A 우두머리 배율 255 고정, 시드와 연결된 값).", 11, sub: true)) } });
        // 다이맥스 레벨·거다이맥스 (소드실드), 테라스탈 타입 (SV)
        dmaxE = Num(v => Changed(() => { if (Pk is IDynamaxLevel d) d.DynamaxLevel = (byte)Math.Min(v, 10); }), 10); dmaxE.HorizontalTextAlignment = TextAlignment.Center;
        var dTiny = new Button { Text = "최대", FontSize = 9, Padding = 0, WidthRequest = 34, HeightRequest = 22, CornerRadius = 6, BorderWidth = 1, BorderColor = T.Accent, TextColor = T.Accent, VerticalOptions = LayoutOptions.Center };
        dTiny.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); dTiny.Clicked += (_, _) => dmaxE.Text = "10";
        gmaxSw = new Switch { OnColor = T.Accent, VerticalOptions = LayoutOptions.Center }; gmaxSw.Toggled += (_, e) => Changed(() => { if (Pk is IGigantamax gg) gg.CanGigantamax = e.Value; });
        var gmaxBox = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.End, Children = { new Image { Source = "ov_dyna.png", WidthRequest = 18, HeightRequest = 18, VerticalOptions = LayoutOptions.Center }, T.L("거다이맥스", 13), gmaxSw } };
        dmaxField = T.Field("다이맥스 레벨 (0~10)", Cell(dmaxE, dTiny));
        dynRow = T.Cols(2, 8); dynRow.Add(dmaxField, 0); dynRow.Add(gmaxBox, 1); gmaxCell = gmaxBox;
        (var tv, teraL) = T.Chooser(() => { if (Pk is ITeraType t) Pick("테라스탈 타입", MainSection.TeraList(false), (int)t.TeraTypeOriginal, c => Changed(() => { t.TeraTypeOriginal = (MoveType)c.Value; Reload(); })); });
        (var tov, teraOvL) = T.Chooser(() => { if (Pk is ITeraType t) Pick("테라스탈 타입 덮어쓰기", MainSection.TeraList(true), (int)t.TeraTypeOverride, c => Changed(() => { t.TeraTypeOverride = (MoveType)c.Value; Reload(); })); });
        teraRow2 = Two(T.Field("테라스탈 타입", tv), T.Field("덮어쓰기(변경)", tov));
        battleCard = T.Card(new VerticalStackLayout { Spacing = 8, Children = { T.L("다이맥스 · 테라스탈", 13, sub: true), dynRow, teraRow2 } });
        Content = Stack(T.Card(g), buttons, battleCard, sizeCard);
    }
    private Entry dmaxE; private Switch gmaxSw; private View dmaxField, gmaxCell, teraRow2, battleCard; private Grid dynRow; private Label teraL, teraOvL;
    private readonly Entry hE, wE, sE;
    private readonly View sizeCard, sRow;
    private Button htAllBtn;
    private readonly Label sizeInfo = T.L("", 12, sub: true);
    private static string Rate(byte v) => PokeSizeUtil.GetSizeRating(v) switch { PokeSize.XS => "아주 작음", PokeSize.S => "작음", PokeSize.M => "보통", PokeSize.L => "큼", _ => "아주 큼" };
    private void UpdateSize()
    {
        var parts = new List<string>();
        if (Pk is IScaledSize z) parts.Add($"키 {Rate(z.HeightScalar)} · 몸무게 {Rate(z.WeightScalar)}");
        if (Pk is IScaledSize3 z3s) parts.Add($"배율 {Rate(z3s.Scale)}");
        if (Pk is IScaledSizeAbsolute a) parts.Add($"실제 {a.HeightAbsolute:0.00}m / {a.WeightAbsolute:0.0}kg");
        sizeInfo.Text = string.Join("  ·  ", parts);
    }

    /// <summary>잠재파워 타입 바꾸기: 지금 개체값에서 가장 적게 바꿔(각 능력치 ±1 이내) 원하는 타입이 되게.</summary>
    private void PickHiddenPower()
    {
        if (Pk.Format < 3) { Note.Show("1·2세대는 개체값(DV) 방식이 달라 여기서 바꿀 수 없습니다"); return; }
        var types = Enumerable.Range(0, 16).Select(t => new ComboItem(GameInfo.Strings.types[t + 1], t)).ToList();
        int cur = -1; try { Span<int> iv0 = stackalloc int[6]; Pk.GetIVs(iv0); cur = HiddenPower.GetType(iv0, Pk.Context); } catch { }
        SheetHost.Show(new PickerSheet("잠재파워 타입", types, cur, c => Changed(() =>
        {
            Span<int> ivs = stackalloc int[6]; Pk.GetIVs(ivs);
            int[] baseIv = ivs.ToArray(), best = null; int bestCost = int.MaxValue;
            for (int mask = 0; mask < 64; mask++)
            {
                var t = (int[])baseIv.Clone(); int cost = 0;
                for (int i = 0; i < 6; i++) if ((mask >> i & 1) != 0) { t[i] = t[i] == 0 ? 1 : t[i] - (t[i] % 2 == 1 ? 1 : -1); if (t[i] > 31) t[i] = 30; cost++; }
                if (cost >= bestCost) continue;
                try { if (HiddenPower.GetType(t, Pk.Context) == c.Value) { best = t; bestCost = cost; } } catch { }
            }
            if (best == null) { Note.Show("이 타입으로 만들 수 없습니다"); return; }
            Pk.SetIVs(best); Reload();
            Note.Show($"잠재파워 {c.Text} (개체값 {bestCost}칸 조정)");
        }), t => $"type_icon_{t.Value + 1:00}.png"));
    }

    private int GetIV(int i) => i switch { 0 => Pk.IV_HP, 1 => Pk.IV_ATK, 2 => Pk.IV_DEF, 3 => Pk.IV_SPA, 4 => Pk.IV_SPD, _ => Pk.IV_SPE };
    private void SetIV(int i, int v) { switch (i) { case 0: Pk.IV_HP = v; break; case 1: Pk.IV_ATK = v; break; case 2: Pk.IV_DEF = v; break; case 3: Pk.IV_SPA = v; break; case 4: Pk.IV_SPD = v; break; default: Pk.IV_SPE = v; break; } }
    private int GetEV(int i)
    {
        if (Pk is IAwakened a) return i switch { 0 => a.AV_HP, 1 => a.AV_ATK, 2 => a.AV_DEF, 3 => a.AV_SPA, 4 => a.AV_SPD, _ => a.AV_SPE };
        return i switch { 0 => Pk.EV_HP, 1 => Pk.EV_ATK, 2 => Pk.EV_DEF, 3 => Pk.EV_SPA, 4 => Pk.EV_SPD, _ => Pk.EV_SPE };
    }
    private void SetEV(int i, int v)
    {
        if (Pk is IAwakened a)
        {
            byte b = (byte)Math.Clamp(v, 0, 200);
            switch (i) { case 0: a.AV_HP = b; break; case 1: a.AV_ATK = b; break; case 2: a.AV_DEF = b; break; case 3: a.AV_SPA = b; break; case 4: a.AV_SPD = b; break; default: a.AV_SPE = b; break; }
            if (Pk is PB7 pb) pb.ResetCalculatedValues();   // 능력치·CP 다시 계산 (CP는 레벨·개체값·각성치로 정해짐 → 항상 합법)
            return;
        }
        switch (i) { case 0: Pk.EV_HP = v; break; case 1: Pk.EV_ATK = v; break; case 2: Pk.EV_DEF = v; break; case 3: Pk.EV_SPA = v; break; case 4: Pk.EV_SPD = v; break; default: Pk.EV_SPE = v; break; }
    }

    private void Recalc()
    {
        var p = Pk.Clone(); p.ResetPartyStats();
        int[] st = [p.Stat_HPMax, p.Stat_ATK, p.Stat_DEF, p.Stat_SPA, p.Stat_SPD, p.Stat_SPE];
        var pi = AppState.Sav.Personal.GetFormEntry(Pk.Species, Pk.Form); if (pi.HP == 0) pi = Pk.PersonalInfo;
        int[] bs = [pi.HP, pi.ATK, pi.DEF, pi.SPA, pi.SPD, pi.SPE];
        int up = -1, down = -1; var nat = (int)Pk.StatAlignment;
        int[] toRow = [1, 2, 5, 3, 4];   // 성격 표의 능력치 순서 → 화면 표 순서 (빨강 상승·파랑 하락)
        if (Pk.Format >= 3 && nat is >= 0 and < 25 && nat / 5 != nat % 5) { up = toRow[nat / 5]; down = toRow[nat % 5]; }
        for (int i = 0; i < 6; i++)
        {
            baseL[i].Text = bs[i].ToString(); statL[i].Text = st[i].ToString();
            statL[i].TextColor = i == up ? T.Bad : i == down ? Color.FromArgb("#2563EB") : null;
            if (statL[i].TextColor == null) T.Text(statL[i]);
        }
        int evSum = Enumerable.Range(0, 6).Sum(GetEV);
        if (Pk is PB7 pb7) { evTotal.Text = $"CP {pb7.Stat_CP}"; T.Text(evTotal); }   // 레츠고: 각성치 합계 대신 CP
        else { evTotal.Text = Pk.Format < 3 ? $"{evSum}" : $"{evSum}/510"; evTotal.TextColor = Pk.Format < 3 ? null : evSum > 510 ? T.Bad : evSum == 510 ? T.Good : null; if (evTotal.TextColor == null) T.Text(evTotal); }
        baseTotal.Text = bs.Sum().ToString(); T.Text(baseTotal);
        try { Span<int> ivs = stackalloc int[6]; Pk.GetIVs(ivs); var ht = HiddenPower.GetType(ivs, Pk.Context); hpType.Text = Pk.Format >= 2 ? $"잠재 {GameInfo.Strings.types[ht + 1]}" : ""; } catch { hpType.Text = ""; }
    }

    protected override void Load(PKM pk)
    {
        for (int i = 0; i < 6; i++)
        {
            ivE[i].Text = GetIV(i).ToString(); evE[i].Text = GetEV(i).ToString();
            htC[i].IsVisible = pk is IHyperTrain; if (pk is IHyperTrain h) htC[i].IsChecked = GetHT(h, i);
        }
        if (htAllBtn != null) htAllBtn.IsVisible = pk is IHyperTrain;
        bool av = pk is IAwakened;
        if (evHead != null) evHead.Text = av ? "각성치" : "노력치";
        if (evRndBtn != null) { evRndBtn.Text = av ? "AV 무작위" : "EV 무작위"; ev0Btn.Text = av ? "AV 0" : "EV 0"; }
        dmaxField.IsVisible = pk is IDynamaxLevel; if (pk is IDynamaxLevel dl) dmaxE.Text = dl.DynamaxLevel.ToString();
        gmaxCell.IsVisible = pk is IGigantamax; if (pk is IGigantamax gx) gmaxSw.IsToggled = gx.CanGigantamax;
        teraRow2.IsVisible = pk is ITeraType; if (pk is ITeraType tt) { teraL.Text = MainSection.TeraName((int)tt.TeraTypeOriginal); teraOvL.Text = MainSection.TeraName((int)tt.TeraTypeOverride); }
        battleCard.IsVisible = pk.Species != 0 && (pk is IDynamaxLevel || pk is IGigantamax || pk is ITeraType);
        Recalc();
        sizeCard.IsVisible = pk.Species != 0 && pk is IScaledSize;
        if (pk is IScaledSize z) { hE.Text = z.HeightScalar.ToString(); wE.Text = z.WeightScalar.ToString(); }
        sRow.IsVisible = pk is IScaledSize3; if (pk is IScaledSize3 z3) sE.Text = z3.Scale.ToString();
        UpdateSize();
    }
}

// ======================= 기술 =======================
public class MovesSection : Section
{
    private readonly Label[] mv = new Label[4], rl = new Label[4];
    private readonly Image[] ty = new Image[4];
    private readonly Button[] ppUp = new Button[4];
    private readonly View relearnCard, plusCard, trCard;
    private readonly VerticalStackLayout trList = new() { Spacing = 2 };
    /// <summary>이 개체가 합법적으로 가질 수 있는 기술 (PKHeX LearnPossible).</summary>
    private static string TypeIcon(ComboItem c) { try { return c.Value > 0 ? $"type_icon_{MoveInfo.GetType((ushort)c.Value, Pk.Context):00}.png" : null; } catch { return null; } }
    private static bool[] Learnable()
    {
        var can = new bool[GameInfo.Strings.movelist.Length];
        try { var la = new LegalityAnalysis(Pk); LearnPossible.Get(Pk, la.EncounterOriginal, la.Info.EvoChainsAllGens, can); } catch { }
        return can;
    }
    private readonly VerticalStackLayout plusList = new() { Spacing = 2 };
    private readonly Label plusHead = T.L("", 14, bold: true);
    private View PlusFold()
    {
        plusList.IsVisible = Preferences.Get("plus_open", false);
        var t = new TapGestureRecognizer(); t.Tapped += (_, _) => { plusList.IsVisible = !plusList.IsVisible; Preferences.Set("plus_open", plusList.IsVisible); Reload(); };
        plusHead.GestureRecognizers.Add(t);
        return plusHead;
    }

    public MovesSection()
    {
        var moves = new VerticalStackLayout { Spacing = 8 };
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            (var v, mv[i]) = T.Chooser(() => { var can = Learnable(); SheetHost.Show(new PickerSheet($"기술 {k + 1}", AppState.Src.Moves, GetMove(k), c => Changed(() => { SetMove(k, (ushort)c.Value); Pk.HealPP(); Reload(); }), icon: TypeIcon, tier: c => c.Value > 0 && c.Value < can.Length && can[c.Value] ? 0 : 2)); });
            ty[i] = new Image { WidthRequest = 28, HeightRequest = 28 };
            ppUp[i] = T.Pill("PP", size: 11); ppUp[i].WidthRequest = 104; ppUp[i].HeightRequest = 44; ppUp[i].Padding = new Thickness(4, 0);
            ppUp[i].Clicked += (_, _) => Changed(() => { SetPPUp(k, (GetPPUp(k) + 1) % 4); Pk.HealPP(); Reload(); });
            var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
            row.Add(ty[i], 0); row.Add(v, 1); row.Add(ppUp[i], 2);
            moves.Children.Add(row);
        }
        var relearn = new VerticalStackLayout { Spacing = 8, Children = { T.L("떠올리기 기술", 13, sub: true) } };
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            (var v, rl[i]) = T.Chooser(() => { var can = Learnable(); SheetHost.Show(new PickerSheet($"떠올리기 기술 {k + 1}", AppState.Src.Relearn, GetRelearn(k), c => Changed(() => { SetRelearn(k, (ushort)c.Value); Reload(); }), icon: TypeIcon, tier: c => c.Value > 0 && c.Value < can.Length && can[c.Value] ? 0 : 2)); });
            relearn.Children.Add(v);
        }
        var sugg = T.Pill("추천 기술"); sugg.Clicked += (_, _) => Changed(() => { Pk.SetMoveset(); Pk.HealPP(); Reload(); });
        var suggR = T.Pill("떠올리기 추천"); suggR.Clicked += (_, _) => Changed(() => { Pk.SetRelearnMoves(new LegalityAnalysis(Pk)); Reload(); });
        relearnCard = T.Card(relearn);
        var tAll = Small("배울 수 있는 것 전부"); tAll.Clicked += (_, _) => Changed(() => { if (Pk is ITechRecord t) t.SetRecordFlags(Pk, TechnicalRecordApplicatorOption.LegalAll); Reload(); });
        var tCur = Small("현재 기술만"); tCur.Clicked += (_, _) => Changed(() => { if (Pk is ITechRecord t) t.SetRecordFlags(Pk, TechnicalRecordApplicatorOption.LegalCurrent); Reload(); });
        var tNone = Small("모두 해제"); tNone.Clicked += (_, _) => Changed(() => { if (Pk is ITechRecord t) t.ClearRecordFlags(); Reload(); });
        var tBtns = T.Cols(3, 6); tBtns.Add(tAll, 0); tBtns.Add(tCur, 1); tBtns.Add(tNone, 2);
        var tHint = T.L("파란색 = 이 포켓몬이 배울 수 있는 기술 (기술머신·기술레코드로 다시 배울 수 있음을 기록하는 플래그)", 12, sub: true); tHint.LineBreakMode = LineBreakMode.WordWrap;
        trCard = FoldCard("재학습 플래그", new VerticalStackLayout { Spacing = 8, Children = { tHint, tBtns, trList } }, "tr_open", false);
        var pAll = T.Pill("합법 전부"); pAll.Clicked += (_, _) => Changed(() => { if (Pk is IPlusRecord r && Pk.PersonalInfo is IPermitPlus p) r.SetPlusFlags(Pk, p, PlusRecordApplicatorOption.LegalSeedTM); Reload(); });
        var pCur = T.Pill("현재 기술만"); pCur.Clicked += (_, _) => Changed(() => { if (Pk is IPlusRecord r && Pk.PersonalInfo is IPermitPlus p) r.SetPlusFlags(Pk, p, PlusRecordApplicatorOption.LegalCurrent); Reload(); });
        var pNone = T.Pill("모두 해제"); pNone.Clicked += (_, _) => Changed(() => { if (Pk is IPlusRecord r && Pk.PersonalInfo is IPermitPlus p) r.ClearPlusFlags(p.PlusCountTotal); Reload(); });
        var pBtns = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { pAll, pCur, pNone } };
        foreach (var b in pBtns.Children.OfType<View>()) b.Margin = new Thickness(0, 0, 6, 6);
        plusCard = T.Card(new VerticalStackLayout { Spacing = 8, Children = { T.L("기술플러스 (레전즈 Z-A) · 강조 = 합법으로 켤 수 있는 기술", 13, sub: true), pBtns, PlusFold(), plusList } });
        var ppMaxB = T.Pill("PP 최대"); ppMaxB.Clicked += (_, _) => Changed(() => { for (int i = 0; i < 4; i++) if (GetMove(i) != 0) SetPPUp(i, 3); Pk.HealPP(); Reload(); });
        Content = Stack(T.Card(moves), new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { sugg, suggR, ppMaxB } } }, plusCard, relearnCard, trCard);
    }
    private ushort GetMove(int i) => i switch { 0 => Pk.Move1, 1 => Pk.Move2, 2 => Pk.Move3, _ => Pk.Move4 };
    private void SetMove(int i, ushort v) { switch (i) { case 0: Pk.Move1 = v; break; case 1: Pk.Move2 = v; break; case 2: Pk.Move3 = v; break; default: Pk.Move4 = v; break; } }
    private int GetPPUp(int i) => i switch { 0 => Pk.Move1_PPUps, 1 => Pk.Move2_PPUps, 2 => Pk.Move3_PPUps, _ => Pk.Move4_PPUps };
    private void SetPPUp(int i, int v) { switch (i) { case 0: Pk.Move1_PPUps = v; break; case 1: Pk.Move2_PPUps = v; break; case 2: Pk.Move3_PPUps = v; break; default: Pk.Move4_PPUps = v; break; } }
    private ushort GetRelearn(int i) => i switch { 0 => Pk.RelearnMove1, 1 => Pk.RelearnMove2, 2 => Pk.RelearnMove3, _ => Pk.RelearnMove4 };
    private void SetRelearn(int i, ushort v) { switch (i) { case 0: Pk.RelearnMove1 = v; break; case 1: Pk.RelearnMove2 = v; break; case 2: Pk.RelearnMove3 = v; break; default: Pk.RelearnMove4 = v; break; } }

    protected override void Load(PKM pk)
    {
        for (int i = 0; i < 4; i++)
        {
            var m = GetMove(i);
            mv[i].Text = m == 0 ? "(없음)" : GameInfo.Strings.movelist[m];
            ty[i].Source = m == 0 ? null : $"type_icon_{MoveInfo.GetType(m, pk.Context):00}.png";
            int ppMax = 0, ppCur = 0;
            try { ppMax = m == 0 ? 0 : pk.GetMovePP(m, GetPPUp(i)); ppCur = i switch { 0 => pk.Move1_PP, 1 => pk.Move2_PP, 2 => pk.Move3_PP, _ => pk.Move4_PP }; } catch { }
            ppUp[i].Text = m == 0 ? $"PP · +{GetPPUp(i)}" : $"PP {ppCur}/{ppMax} · +{GetPPUp(i)}";
            var r = GetRelearn(i); rl[i].Text = r == 0 ? "(없음)" : GameInfo.Strings.movelist[r];
        }
        relearnCard.IsVisible = pk.Format >= 6;
        trCard.IsVisible = pk.Species != 0 && pk is ITechRecord;
        trList.Children.Clear();
        if (trCard.IsVisible && pk is ITechRecord tr)
        {
            var idx = tr.Permit.RecordPermitIndexes;
            ReadOnlySpan<EvoCriteria> evos = [];
            try { evos = new LegalityAnalysis(pk).Info.EvoChainsAllGens.Get(pk.Context); } catch { }
            var evoArr = evos.ToArray();
            for (int i = 0; i < idx.Length; i++)
            {
                bool permitted = tr.IsRecordPermitted(evoArr, i), on = tr.GetMoveRecordFlag(i);
                if (!permitted && !on) continue;   // 배울 수 없고 꺼져 있는 것은 생략
                int k = i; var move = idx[i];
                var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8, Padding = new Thickness(6, 0), HeightRequest = 40 };
                g.BackgroundColor = permitted ? Color.FromArgb("#332563EB") : Color.FromArgb("#33DC2626");   // 파랑=배울 수 있음, 빨강=배울 수 없는데 켜짐
                g.Add(new Image { Source = $"type_icon_{MoveInfo.GetType(move, pk.Context):00}.png", WidthRequest = 24, HeightRequest = 24 }, 0);
                g.Add(T.L(GameInfo.Strings.movelist[move], 14), 1);
                var sw = new Switch { IsToggled = on, OnColor = T.Accent };
                sw.Toggled += (_, e) => Changed(() => tr.SetMoveRecordFlag(k, e.Value));
                g.Add(sw, 2); trList.Children.Add(g);
            }
        }
        plusCard.IsVisible = pk.Species != 0 && pk is IPlusRecord && pk.PersonalInfo is IPermitPlus;
        plusList.Children.Clear();
        if (plusCard.IsVisible && pk is IPlusRecord rec && pk.PersonalInfo is IPermitPlus permit)
        {
            var idx = permit.PlusMoveIndexes;
            // 합법으로 켤 수 있는 기술 = 합법 전부 적용 시 켜지는 칸
            var legal = new HashSet<int>();
            try { var t = Pk.Clone(); if (t is IPlusRecord pr) { pr.ClearPlusFlags(permit.PlusCountTotal); pr.SetPlusFlags(t, permit, PlusRecordApplicatorOption.LegalSeedTM); for (int i = 0; i < idx.Length; i++) if (pr.GetMovePlusFlag(i)) legal.Add(i); } } catch { }
            int on = 0; for (int i = 0; i < idx.Length; i++) if (rec.GetMovePlusFlag(i)) on++;
            plusHead.Text = $"{(plusList.IsVisible ? "▾" : "▸")}  기술 목록 (켜짐 {on} · 합법 {legal.Count} / 전체 {idx.Length})";
            if (!plusList.IsVisible) return;
            for (int i = 0; i < idx.Length; i++)
            {
                int k = i; var move = idx[i];
                var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8, HeightRequest = 40 };
                g.Add(new Image { Source = $"type_icon_{MoveInfo.GetType(move, pk.Context):00}.png", WidthRequest = 24, HeightRequest = 24 }, 0);
                g.Add(T.L(GameInfo.Strings.movelist[move], 14), 1);
                var sw = new Switch { IsToggled = rec.GetMovePlusFlag(k), OnColor = T.Accent };
                if (legal.Contains(k)) { g.BackgroundColor = T.Accent.WithAlpha(0.14f); ((Label)g.Children[1]).FontAttributes = FontAttributes.Bold; }
                sw.Toggled += (_, e) => Changed(() => rec.SetMovePlusFlag(k, e.Value));
                g.Add(sw, 2);
                plusList.Children.Add(g);
            }
        }
    }
}

// ======================= 만남 =======================
public class MetSection : Section
{
    private readonly Label game, loc, eggLoc;
    private readonly Entry metLevel;
    private readonly DatePicker metDate = DateBox(), eggDate = DateBox();
    /// <summary>입력칸(Entry)과 같은 글자 크기·높이의 날짜 선택.</summary>
    private static DatePicker DateBox() { var d = new DatePicker { Format = "yyyy-MM-dd", FontSize = 15, HeightRequest = 44, VerticalOptions = LayoutOptions.End }; d.SetAppThemeColor(DatePicker.TextColorProperty, Color.FromArgb("#111827"), Color.FromArgb("#F3F4F6")); return d; }
    private readonly Switch fateful, hatched;
    private readonly View dateRow, eggCard, ballRow;
    private readonly Label metBall;
    private Entry obey; private View obeyRow;
    private readonly Image metBallIcon;

    public MetSection()
    {
        (var gv, game) = T.Chooser(() => Pick("원산 게임", AppState.Src.Games, (int)Pk.Version, c => Changed(() => { Pk.Version = (GameVersion)c.Value; Reload(); })));
        (var lv, loc) = T.Chooser(() => Pick("만난 장소", GameInfo.GetLocationList(Pk.Version, Pk.Context, false), Pk.MetLocation, c => Changed(() => { Pk.MetLocation = (ushort)c.Value; Reload(); })));
        (var ev, eggLoc) = T.Chooser(() => Pick("알 받은 장소", GameInfo.GetLocationList(Pk.Version, Pk.Context, true), Pk.EggLocation, c => Changed(() => { Pk.EggLocation = (ushort)c.Value; Reload(); })));
        metLevel = Num(v => Changed(() => { Pk.MetLevel = (byte)Math.Clamp(v, 0, 100); }), 100);
        metDate.DateSelected += (_, e) => Changed(() => { if (e.NewDate is DateTime d) Pk.MetDate = DateOnly.FromDateTime(d); });
        eggDate.DateSelected += (_, e) => Changed(() => { if (e.NewDate is DateTime d) Pk.EggMetDate = DateOnly.FromDateTime(d); });
        var fRow = Switch("운명적인 만남", out fateful);
        fateful.Toggled += (_, e) => Changed(() => Pk.FatefulEncounter = e.Value);
        var hRow = Switch("알에서 부화", out hatched);
        hatched.Toggled += (_, e) => Changed(() => { if (!e.Value) { Pk.EggLocation = 0; Pk.EggMetDate = null; } else if (Pk.EggLocation == 0) Pk.EggLocation = Locations.LinkTrade6; Reload(); });
        dateRow = T.Field("만난 날짜", metDate);
        (var bv, metBall) = T.Chooser(() => Pick("잡은 볼", AppState.Src.Balls, Pk.Ball, c => Changed(() => { Pk.Ball = (byte)c.Value; Reload(); AppState.ReloadEditors(); }), c => AppState.BallSprite(c.Value)));
        metBallIcon = new Image { WidthRequest = 30, HeightRequest = 30, VerticalOptions = LayoutOptions.End, Margin = new Thickness(0, 0, 0, 6) };
        obey = Num(v => Changed(() => { if (Pk is IObedienceLevel o) o.ObedienceLevel = (byte)Math.Clamp(v, 0, 100); }), 100);
        var obeySet = T.Pill("만난 레벨로"); obeySet.VerticalOptions = LayoutOptions.End;
        obeySet.Clicked += (_, _) => Changed(() => { if (Pk is IObedienceLevel o) o.ObedienceLevel = Pk.MetLevel; Reload(); });
        var og = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 10 }; og.Add(T.Field("순종 레벨 (SV · Z-A)", obey), 0); og.Add(obeySet, 1);
        obeyRow = og;
        var ballGrid = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 8 }; ballGrid.Add(metBallIcon, 0); ballGrid.Add(T.Field("잡은 볼", bv), 1);
        ballRow = ballGrid;
        eggCard = T.Card(Stack(T.Field("알 받은 장소", ev), T.Field("알 받은 날짜", eggDate)));
        var suggest = T.Pill("추천 만남 정보 적용");
        suggest.Clicked += (_, _) => Changed(() =>
        {
            var enc = new LegalityAnalysis(Pk).EncounterMatch;
            var s = EncounterSuggestion.GetSuggestedMetInfo(Pk);
            if (s != null) { Pk.MetLocation = s.Location; Pk.MetLevel = s.LevelMin; }
            Reload();
        });
        Content = Stack(T.Card(Stack(T.Field("원산 게임", gv), T.Field("만난 장소", lv), Two(T.Field("만난 레벨", metLevel), dateRow), obeyRow, ballRow, Two(fRow, hRow))), eggCard, suggest);
    }

    protected override void Load(PKM pk)
    {
        game.Text = NameOf(AppState.Src.Games, (int)pk.Version);
        ballRow.IsVisible = pk.Format >= 3;
        obeyRow.IsVisible = pk is IObedienceLevel; if (pk is IObedienceLevel ob) obey.Text = ob.ObedienceLevel.ToString();
        metBall.Text = NameOf(AppState.Src.Balls, pk.Ball); metBallIcon.Source = pk.Format >= 3 ? AppState.BallSprite(pk.Ball) : null;
        loc.Text = NameOf(GameInfo.GetLocationList(pk.Version, pk.Context, false), pk.MetLocation);
        metLevel.Text = pk.MetLevel.ToString();
        dateRow.IsVisible = pk.Format >= 4;
        if (pk.MetDate is { } d) metDate.Date = d.ToDateTime(TimeOnly.MinValue);
        fateful.IsToggled = pk.FatefulEncounter;
        bool egg = pk.Format >= 4 && pk.EggLocation != 0;
        hatched.IsToggled = egg; eggCard.IsVisible = egg;
        eggLoc.Text = NameOf(GameInfo.GetLocationList(pk.Version, pk.Context, true), pk.EggLocation);
        if (pk.EggMetDate is { } e) eggDate.Date = e.ToDateTime(TimeOnly.MinValue);
    }
}

// ======================= 어버이/기타 =======================
public class OTSection : Section
{
    private readonly Entry ot, tid, sid, ht, ec, pid, trackE;
    private readonly View trackRow;
    private readonly Button otG, htG;
    private readonly View htCard, ecRow;

    public OTSection()
    {
        ot = T.Input(); ot.TextChanged += (_, e) => Changed(() => Pk.OriginalTrainerName = e.NewTextValue ?? "");
        tid = Num(v => Changed(() => SetIds()), 999999); sid = Num(v => Changed(() => SetIds()), 999999);
        otG = T.Pill("♂"); otG.HeightRequest = 44; otG.Clicked += (_, _) => Changed(() => { Pk.OriginalTrainerGender ^= 1; Reload(); });
        ht = T.Input(); ht.TextChanged += (_, e) => Changed(() => Pk.HandlingTrainerName = e.NewTextValue ?? "");
        htG = T.Pill("♂"); htG.HeightRequest = 44; htG.Clicked += (_, _) => Changed(() => { Pk.HandlingTrainerGender ^= 1; Reload(); });
        ec = T.Input(); ec.MaxLength = 8; ec.TextChanged += (_, e) => Changed(() => { if (uint.TryParse(e.NewTextValue, System.Globalization.NumberStyles.HexNumber, null, out var v)) Pk.EncryptionConstant = v; });
        pid = T.Input(); pid.MaxLength = 8; pid.TextChanged += (_, e) => Changed(() => { if (uint.TryParse(e.NewTextValue, System.Globalization.NumberStyles.HexNumber, null, out var v)) Pk.PID = v; });
        var mine = T.Pill("세이브 트레이너로 설정", primary: true);
        mine.Clicked += (_, _) => Changed(() =>
        {
            var s = AppState.Sav;
            Pk.OriginalTrainerName = s.OT; Pk.ID32 = s.ID32; Pk.OriginalTrainerGender = s.Gender;
            if (Pk.Format >= 6) { Pk.HandlingTrainerName = ""; Pk.CurrentHandler = 0; }
            Reload();
        });
        var reroll = T.Pill("암호화 상수 다시 뽑기"); reroll.Clicked += (_, _) => Changed(() => { Pk.SetRandomEC(); Reload(); });
        var trash = T.Pill("쓰레기 바이트 지우기");
        trash.Clicked += (_, _) => Changed(() =>
        {
            var before = AppState.IsLegal(Pk);
            var ot = Pk.OriginalTrainerName; Pk.OriginalTrainerTrash.Clear(); Pk.OriginalTrainerName = ot;
            var nick = Pk.Nickname; var isNick = Pk.IsNicknamed; Pk.NicknameTrash.Clear(); Pk.Nickname = nick; Pk.IsNicknamed = isNick;
            if (Pk.HandlingTrainerTrash.Length > 0) { var ht = Pk.HandlingTrainerName; Pk.HandlingTrainerTrash.Clear(); Pk.HandlingTrainerName = ht; }
            Reload();
            var after = AppState.IsLegal(Pk);
            Note.Show(before && !after ? "지웠습니다 — 이 개체는 쓰레기 바이트가 필요해 불법이 됐습니다. 실행 취소로 되돌리세요" : "어버이·닉네임·현재 트레이너 이름의 쓰레기 바이트를 지웠습니다");
        });
        htCard = T.Card(Stack(T.L("현재 트레이너 (교환받은 경우)", 13, sub: true), Two(T.Field("이름", ht), T.Field("성별", htG))));
        trackE = T.Input(); trackE.MaxLength = 16; trackE.TextChanged += (_, e) => Changed(() => { if (Pk is IHomeTrack h && ulong.TryParse(e.NewTextValue, System.Globalization.NumberStyles.HexNumber, null, out var v) && h.Tracker != v) { h.Tracker = v; if (AppState.SyncHomeScale(Pk)) Note.Show("홈 트래커에 맞춰 키를 배율과 같게 맞췄습니다 (HOME과 같은 방식)"); } });
        var trackClr = T.Pill("홈 트래커 비우기"); trackClr.Clicked += (_, _) => Changed(() => { if (Pk is IHomeTrack h) h.Tracker = 0; Reload(); });
        trackRow = new VerticalStackLayout { Spacing = 8, Children = { T.Field("홈 트래커 (16진수)", trackE), trackClr } };
        ecRow = T.Card(Stack(Two(T.Field("암호화 상수 (16진수)", ec), T.Field("PID (16진수)", pid)), reroll, trackRow));
        Content = Stack(T.Card(Stack(Two(T.Field("어버이 이름", ot), T.Field("성별", otG)), Two(T.Field("TID", tid), T.Field("SID", sid)), Two(mine, trash))), htCard, ecRow);
        mine.FontSize = trash.FontSize = 12; mine.Padding = trash.Padding = new Thickness(4, 0);
    }

    private bool G7 => Pk.Format >= 7;
    private void SetIds()
    {
        if (!int.TryParse(tid.Text, out var t) || !int.TryParse(sid.Text, out var s)) return;
        if (G7) Pk.ID32 = (uint)(Math.Min(s, 4294) * 1_000_000L + Math.Min(t, 999999));
        else { Pk.TID16 = (ushort)Math.Min(t, 65535); Pk.SID16 = (ushort)Math.Min(s, 65535); }
    }

    protected override void Load(PKM pk)
    {
        ot.Text = pk.OriginalTrainerName;
        otG.Text = pk.OriginalTrainerGender == 0 ? "♂ 남" : "♀ 여"; T.GenderTint(otG, pk.OriginalTrainerGender);
        tid.MaxLength = G7 ? 6 : 5; sid.MaxLength = G7 ? 4 : 5;   // 7세대 이후 TID 6자리·SID 4자리, 이전 세대 각 5자리(최대 65535)
        if (G7) { tid.Text = (pk.ID32 % 1_000_000).ToString("000000"); sid.Text = (pk.ID32 / 1_000_000).ToString("0000"); }
        else { tid.Text = pk.TID16.ToString("00000"); sid.Text = pk.SID16.ToString("00000"); }
        htCard.IsVisible = pk.Format >= 6;
        ht.Text = pk.HandlingTrainerName; htG.Text = pk.HandlingTrainerGender == 0 ? "♂ 남" : "♀ 여"; T.GenderTint(htG, pk.HandlingTrainerGender);
        ecRow.IsVisible = pk.Format >= 3;
        ec.Text = pk.EncryptionConstant.ToString("X8"); pid.Text = pk.PID.ToString("X8");
        trackRow.IsVisible = pk is IHomeTrack; if (pk is IHomeTrack ht0) trackE.Text = ht0.Tracker.ToString("X16");
    }
}

// ======================= 리본·메달·추억 =======================
public class RibbonSection : Section
{
    public static View SharedMem;
    private readonly VerticalStackLayout ribbonList = new() { Spacing = 2 }, markList = new() { Spacing = 2 };
    private readonly Label ribbonCount = T.L("", 13, sub: true), ribbonHead = T.L("", 15, bold: true), markHead = T.L("", 15, bold: true);
    private static bool ribbonOpen = Preferences.Get("ribbon_open", false), markOpen = Preferences.Get("mark_open", false);
    private View markCard;
    private readonly View medalCard, memCard;
    private readonly Label medalState = T.L("", 13, sub: true);
    private readonly Label otMem, otInt, otFeel, htMem, htInt, htFeel;
    private readonly Entry otVar, htVar, otFriend, htFriend, otAff, htAff;
    private readonly View htMemBlock, affRow;

    private string ribbonQuery = "";
    public RibbonSection()
    {
        var all = T.Pill("가능한 리본 전부", primary: true); all.Clicked += (_, _) => Changed(() => { AppState.SetAllRibbons(Pk); Reload(); });
        var none = T.Pill("리본 모두 제거"); none.Clicked += (_, _) => Changed(() => { RibbonApplicator.RemoveAllValidRibbons(Pk); Reload(); });
        var tr = new TapGestureRecognizer(); tr.Tapped += (_, _) => { ribbonOpen = !ribbonOpen; Preferences.Set("ribbon_open", ribbonOpen); Reload(); }; ribbonHead.GestureRecognizers.Add(tr);
        var tm = new TapGestureRecognizer(); tm.Tapped += (_, _) => { markOpen = !markOpen; Preferences.Set("mark_open", markOpen); Reload(); }; markHead.GestureRecognizers.Add(tm);
        var search = T.Input(placeholder: "리본·증표 검색 (초성 가능)");
        search.TextChanged += (_, e) => { ribbonQuery = (e.NewTextValue ?? "").Trim(); if (ribbonQuery.Length > 0) { ribbonOpen = markOpen = true; } Reload(); };
        var ribbonCard = T.Card(new VerticalStackLayout { Spacing = 8, Children = { new HorizontalStackLayout { Spacing = 8, Children = { all, none } }, search, ribbonCount, ribbonHead, ribbonList } });
        markCard = T.Card(new VerticalStackLayout { Spacing = 8, Children = { markHead, markList } });

        var mAll = T.Pill("메달 모두 획득"); mAll.Clicked += (_, _) => Changed(() => { if (Pk is ISuperTrain st) { st.SuperTrainBitFlags = RibbonRules.SetSuperTrainSupremelyTrained(st.SuperTrainBitFlags) | 0xFFFFFFFCu; } Reload(); });
        var mNone = T.Pill("메달 초기화"); mNone.Clicked += (_, _) => Changed(() => { if (Pk is ISuperTrain st) st.SuperTrainBitFlags = 0; Reload(); });
        medalCard = T.Card(new VerticalStackLayout { Spacing = 8, Children = { T.L("대단한 트레이닝 메달 (6·7세대)", 13, sub: true), new HorizontalStackLayout { Spacing = 8, Children = { mAll, mNone } }, medalState } });

        (var a1, otMem) = T.Chooser(() => PickMem(true, 0)); (var a2, otInt) = T.Chooser(() => PickMem(true, 1)); (var a3, otFeel) = T.Chooser(() => PickMem(true, 2));
        (var b1, htMem) = T.Chooser(() => PickMem(false, 0)); (var b2, htInt) = T.Chooser(() => PickMem(false, 1)); (var b3, htFeel) = T.Chooser(() => PickMem(false, 2));
        otVar = Num(v => Changed(() => { if (Pk is IMemoryOT m) m.OriginalTrainerMemoryVariable = (ushort)v; }));
        htVar = Num(v => Changed(() => { if (Pk is IMemoryHT m) m.HandlingTrainerMemoryVariable = (ushort)v; }));
        otFriend = Num(v => Changed(() => Pk.OriginalTrainerFriendship = (byte)v), 255);
        htFriend = Num(v => Changed(() => Pk.HandlingTrainerFriendship = (byte)v), 255);
        otAff = Num(v => Changed(() => { if (Pk is IAffection a) a.OriginalTrainerAffection = (byte)v; }), 255);
        htAff = Num(v => Changed(() => { if (Pk is IAffection a) a.HandlingTrainerAffection = (byte)v; }), 255);
        affRow = Two(T.Field("어버이 친밀도(애정)", otAff), T.Field("현재 트레이너 애정", htAff));
        htMemBlock = new VerticalStackLayout { Spacing = 8, Children = { T.L("현재 트레이너와의 추억", 13, sub: true), T.Field("추억", b1), Two(T.Field("강도", b2), T.Field("감정", b3)), T.Field("대상(변수 번호)", htVar) } };
        memCard = FoldCard("추억", Stack(T.L("어버이와의 추억", 13, sub: true), T.Field("추억", a1), Two(T.Field("강도", a2), T.Field("감정", a3)), T.Field("대상(변수 번호)", otVar), htMemBlock), "mem_open", false);
        SharedMem = memCard;   // "기타" 탭에 표시 (친밀도·애정은 기타 탭의 카드와 중복이라 제거)
        Content = Stack(ribbonCard, markCard);   // 메달은 "기타" 탭으로
    }

    private static MemoryStrings Mem => new(GameInfo.Strings);
    private int MemGen => Pk.Format >= 8 ? 8 : 6;
    private void PickMem(bool ot, int kind)
    {
        if (ot ? Pk is not IMemoryOT : Pk is not IMemoryHT) return;
        IReadOnlyList<ComboItem> list = kind switch
        {
            0 => Mem.Memory,
            1 => ToList(Mem.GetMemoryQualities()),
            _ => ToList(Mem.GetMemoryFeelings(MemGen)),
        };
        int cur = ot ? kind switch { 0 => ((IMemoryOT)Pk).OriginalTrainerMemory, 1 => ((IMemoryOT)Pk).OriginalTrainerMemoryIntensity, _ => ((IMemoryOT)Pk).OriginalTrainerMemoryFeeling }
                     : kind switch { 0 => ((IMemoryHT)Pk).HandlingTrainerMemory, 1 => ((IMemoryHT)Pk).HandlingTrainerMemoryIntensity, _ => ((IMemoryHT)Pk).HandlingTrainerMemoryFeeling };
        Pick(kind == 0 ? "추억 종류" : kind == 1 ? "강도" : "감정", list, cur, c => Changed(() =>
        {
            var v = (byte)c.Value;
            if (ot) { var m = (IMemoryOT)Pk; if (kind == 0) m.OriginalTrainerMemory = v; else if (kind == 1) m.OriginalTrainerMemoryIntensity = v; else m.OriginalTrainerMemoryFeeling = v; }
            else { var m = (IMemoryHT)Pk; if (kind == 0) m.HandlingTrainerMemory = v; else if (kind == 1) m.HandlingTrainerMemoryIntensity = v; else m.HandlingTrainerMemoryFeeling = v; }
            Reload();
        }));
    }
    private static List<ComboItem> ToList(ReadOnlySpan<string> s) { var l = new List<ComboItem>(); for (int i = 0; i < s.Length; i++) l.Add(new ComboItem(string.IsNullOrEmpty(s[i]) ? $"({i})" : s[i], i)); return l; }
    private static string At(IReadOnlyList<ComboItem> l, int v) => l.FirstOrDefault(x => x.Value == v)?.Text ?? v.ToString();

    protected override void Load(PKM pk)
    {
        ribbonList.Children.Clear(); markList.Children.Clear();
        if (pk.Species == 0) { ribbonCount.Text = "포켓몬을 불러오면 리본을 편집할 수 있습니다"; ribbonHead.Text = ""; medalCard.IsVisible = memCard.IsVisible = markCard.IsVisible = false; return; }
        var infos = RibbonInfo.GetRibbonInfo(pk);
        bool IsMark(RibbonInfo r) => r.Name.StartsWith("RibbonMark");
        bool Has(RibbonInfo r) => r.HasRibbon || r.RibbonCount > 0;
        var ribbons = infos.Where(r => !IsMark(r)).ToList(); var marks = infos.Where(IsMark).ToList();
        ribbonCount.Text = "버튼은 리본과 증표 모두에 적용됩니다";
        ribbonHead.Text = $"{(ribbonOpen ? "▾" : "▸")} 리본  {ribbons.Count(Has)} / {ribbons.Count}";
        markHead.Text = $"{(markOpen ? "▾" : "▸")} 증표  {marks.Count(Has)} / {marks.Count}";
        markCard.IsVisible = marks.Count > 0;
        ribbonList.IsVisible = ribbonOpen; markList.IsVisible = markOpen;
        var possible = LegalTools.PossibleRibbons(pk);
        foreach (var r in infos.Where(r => IsMark(r) ? markOpen : ribbonOpen).OrderByDescending(r => possible.Contains(r.Name)).ThenByDescending(Has))
        {
            var name = GameInfo.Strings.Ribbons.GetNameSafe(r.Name, out var kn) ? kn : r.Name.Replace("Ribbon", "");
            if (ribbonQuery.Length > 0 && !Korean.Match(name, ribbonQuery) && !r.Name.Contains(ribbonQuery, StringComparison.OrdinalIgnoreCase)) continue;
            var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8, HeightRequest = 42 };
            if (possible.Contains(r.Name)) { g.BackgroundColor = Color.FromArgb("#3316A34A"); g.Padding = new Thickness(4, 0); }   // 달 수 있는 리본 (초록)
            g.Add(new Image { Source = r.Name.ToLowerInvariant() + ".png", WidthRequest = 30, HeightRequest = 30 }, 0);
            g.Add(T.L(name, 14), 1);
            var prop = pk.GetType().GetProperty(r.Name);
            if (r.Type == RibbonValueType.Boolean)
            {
                var sw = new Switch { IsToggled = r.HasRibbon, OnColor = T.Accent };
                sw.Toggled += (_, e) => Changed(() => prop?.SetValue(Pk, e.Value));
                g.Add(sw, 2);
            }
            else
            {
                var e = T.Input(Keyboard.Numeric); e.WidthRequest = 70; e.Text = r.RibbonCount.ToString(); e.HorizontalTextAlignment = TextAlignment.Center;
                e.TextChanged += (_, a) => Changed(() => { if (byte.TryParse(a.NewTextValue, out var v)) prop?.SetValue(Pk, Math.Min(v, (byte)r.MaxCount)); });
                g.Add(e, 2);
            }
            (IsMark(r) ? markList : ribbonList).Children.Add(g);
        }
        medalCard.IsVisible = pk is ISuperTrain;
        if (pk is ISuperTrain st) medalState.Text = $"메달 플래그 0x{st.SuperTrainBitFlags:X8}";
        memCard.IsVisible = pk is IMemoryOT;
        if (pk is IMemoryOT o)
        {
            otMem.Text = At(Mem.Memory, o.OriginalTrainerMemory); otInt.Text = At(ToList(Mem.GetMemoryQualities()), o.OriginalTrainerMemoryIntensity);
            otFeel.Text = At(ToList(Mem.GetMemoryFeelings(MemGen)), o.OriginalTrainerMemoryFeeling); otVar.Text = o.OriginalTrainerMemoryVariable.ToString();
        }
        htMemBlock.IsVisible = pk is IMemoryHT;
        if (pk is IMemoryHT h)
        {
            htMem.Text = At(Mem.Memory, h.HandlingTrainerMemory); htInt.Text = At(ToList(Mem.GetMemoryQualities()), h.HandlingTrainerMemoryIntensity);
            htFeel.Text = At(ToList(Mem.GetMemoryFeelings(MemGen)), h.HandlingTrainerMemoryFeeling); htVar.Text = h.HandlingTrainerMemoryVariable.ToString();
        }
        otFriend.Text = pk.OriginalTrainerFriendship.ToString(); htFriend.Text = pk.HandlingTrainerFriendship.ToString();
        affRow.IsVisible = pk is IAffection;
        if (pk is IAffection af) { otAff.Text = af.OriginalTrainerAffection.ToString(); htAff.Text = af.HandlingTrainerAffection.ToString(); }
    }
}


// ======================= 기타 (게임별 상태·스탯) =======================
public class MiscSection : Section
{
    private readonly Entry pkrsStrain, pkrsDays; private readonly View pkrsCard;
    private readonly Entry[] contest = new Entry[6]; private readonly View contestCard;
    private readonly CheckBox[] leaf = new CheckBox[6]; private readonly View leafCard;
    private readonly View medalCard; private readonly Label medalState = T.L("", 12, sub: true);
    private readonly Entry otF, htF, otA, htA; private readonly View affRow2;
    private readonly Entry[] gv = new Entry[6], av = new Entry[6]; private readonly View gvCard, avCard;
    private static readonly string[] Stat = ["HP", "공격", "방어", "특공", "특방", "스피드"];

    public MiscSection()
    {
        // 포켓러스
        pkrsStrain = Num(v => Changed(() => Pk.PokerusStrain = Math.Min(v, 15)), 15);
        pkrsDays = Num(v => Changed(() => Pk.PokerusDays = Math.Min(v, 15)), 15);
        var inf = T.Pill("감염"); inf.Clicked += (_, _) => Changed(() => { Pk.PokerusStrain = Math.Max(1, Pk.PokerusStrain); Pk.PokerusDays = Math.Max(1, (Pk.PokerusStrain % 4) + 1); Reload(); });
        var cur = T.Pill("완치"); cur.Clicked += (_, _) => Changed(() => { Pk.PokerusStrain = Math.Max(1, Pk.PokerusStrain); Pk.PokerusDays = 0; Reload(); });
        var no = T.Pill("없음"); no.Clicked += (_, _) => Changed(() => { Pk.PokerusStrain = 0; Pk.PokerusDays = 0; Reload(); });
        pkrsCard = FoldCard("포켓러스", Stack(Two(T.Field("균주 (0~15)", pkrsStrain), T.Field("남은 일수 (0~15)", pkrsDays)), new HorizontalStackLayout { Spacing = 8, Children = { inf, cur, no } }), "misc_pkrs");

        // 콘테스트 능력
        string[] cn = ["멋짐", "아름다움", "귀여움", "슬기로움", "강인함", "윤기"];
        var cg = T.Cols(3);
        for (int i = 0; i < 6; i++) { int k = i; contest[i] = Num(v => Changed(() => SetContest(k, (byte)Math.Min(v, 255))), 255); cg.Add(T.Field(cn[i], contest[i]), i % 3, i / 3); }
        if (cg.RowDefinitions.Count < 2) { cg.RowDefinitions.Add(new(GridLength.Auto)); cg.RowDefinitions.Add(new(GridLength.Auto)); }
        var cmax = T.Pill("최대"); cmax.Clicked += (_, _) => Changed(() => { for (int i = 0; i < 6; i++) SetContest(i, 255); Reload(); });
        var c0 = T.Pill("0"); c0.Clicked += (_, _) => Changed(() => { for (int i = 0; i < 6; i++) SetContest(i, 0); Reload(); });
        contestCard = FoldCard("콘테스트 능력", Stack(cg, new HorizontalStackLayout { Spacing = 8, Children = { cmax, c0 } }), "misc_contest");

        // 빛나는 나뭇잎 (하트골드·소울실버)
        var lg = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        string[] ln = ["잎 1", "잎 2", "잎 3", "잎 4", "잎 5", "왕관"];
        for (int i = 0; i < 6; i++)
        {
            int k = i; leaf[i] = new CheckBox { Color = T.Accent };
            leaf[i].CheckedChanged += (_, e) => Changed(() => { if (Pk is G4PKM g) g.ShinyLeaf = e.Value ? g.ShinyLeaf | (1 << k) : g.ShinyLeaf & ~(1 << k); });
            lg.Children.Add(new HorizontalStackLayout { Margin = new Thickness(0, 0, 10, 4), Children = { leaf[i], T.L(ln[i], 14) } });
        }
        leafCard = FoldCard("빛나는 나뭇잎 (HGSS)", lg, "misc_leaf");

        // 메달
        var mAll = T.Pill("메달 모두 획득"); mAll.Clicked += (_, _) => Changed(() => { if (Pk is ISuperTrain st) st.SuperTrainBitFlags = RibbonRules.SetSuperTrainSupremelyTrained(st.SuperTrainBitFlags) | 0xFFFFFFFCu; Reload(); });
        var mNone = T.Pill("메달 초기화"); mNone.Clicked += (_, _) => Changed(() => { if (Pk is ISuperTrain st) st.SuperTrainBitFlags = 0; Reload(); });
        medalCard = FoldCard("대단한 트레이닝 메달 (6·7세대)", Stack(new HorizontalStackLayout { Spacing = 8, Children = { mAll, mNone } }, medalState), "misc_medal");

        // 친밀도·애정
        otF = Num(v => Changed(() => Pk.OriginalTrainerFriendship = (byte)v), 255); htF = Num(v => Changed(() => Pk.HandlingTrainerFriendship = (byte)v), 255);
        otA = Num(v => Changed(() => { if (Pk is IAffection a) a.OriginalTrainerAffection = (byte)v; }), 255); htA = Num(v => Changed(() => { if (Pk is IAffection a) a.HandlingTrainerAffection = (byte)v; }), 255);
        affRow2 = Two(T.Field("어버이 애정(절친도)", otA), T.Field("현재 트레이너 애정", htA));
        var fmax = T.Pill("친밀도·애정 최대"); fmax.Clicked += (_, _) => Changed(() => { Pk.OriginalTrainerFriendship = 255; if (Pk.Format >= 6) Pk.HandlingTrainerFriendship = 255; if (Pk is IAffection a) { a.OriginalTrainerAffection = 255; } Reload(); });
        var friendCard = FoldCard("친밀도 · 애정", Stack(Two(T.Field("어버이 친밀도", otF), T.Field("현재 트레이너 친밀도", htF)), affRow2, fmax), "misc_friend", true);

        // 노력 레벨 GV (레전즈 아르세우스) · 각성치 AV (레츠고)
        var gg = T.Cols(3); var ag = T.Cols(3);
        gg.RowDefinitions.Add(new(GridLength.Auto)); gg.RowDefinitions.Add(new(GridLength.Auto)); ag.RowDefinitions.Add(new(GridLength.Auto)); ag.RowDefinitions.Add(new(GridLength.Auto));
        for (int i = 0; i < 6; i++)
        {
            int k = i;
            gv[i] = Num(v => Changed(() => { if (Pk is IGanbaru g) SetGV(g, k, (byte)Math.Min(v, 10)); }), 10); gg.Add(T.Field(Stat[i], gv[i]), i % 3, i / 3);
            av[i] = Num(v => Changed(() => { if (Pk is IAwakened a) SetAV(a, k, (byte)Math.Min(v, 200)); }), 200); ag.Add(T.Field(Stat[i], av[i]), i % 3, i / 3);
        }
        var gmax = T.Pill("최대 (10)"); gmax.Clicked += (_, _) => Changed(() => { if (Pk is IGanbaru g) for (int i = 0; i < 6; i++) SetGV(g, i, 10); Reload(); });
        var amax = T.Pill("최대 (200)"); amax.Clicked += (_, _) => Changed(() => { if (Pk is IAwakened a) for (int i = 0; i < 6; i++) SetAV(a, i, 200); Reload(); });
        gvCard = FoldCard("노력 레벨 (레전즈 아르세우스)", Stack(gg, gmax), "misc_gv");
        avCard = FoldCard("각성치 AV (레츠고)", Stack(ag, amax), "misc_av");

        Content = Stack(friendCard, RibbonSection.SharedMem ?? new ContentView(), pkrsCard, contestCard, medalCard, leafCard, gvCard);   // 레츠고 각성치(CP)는 능력치 탭
    }

    private void SetContest(int i, byte v) { if (Pk is not IContestStats c) return; switch (i) { case 0: c.ContestCool = v; break; case 1: c.ContestBeauty = v; break; case 2: c.ContestCute = v; break; case 3: c.ContestSmart = v; break; case 4: c.ContestTough = v; break; default: c.ContestSheen = v; break; } }
    private static byte GetContest(IContestStats c, int i) => i switch { 0 => c.ContestCool, 1 => c.ContestBeauty, 2 => c.ContestCute, 3 => c.ContestSmart, 4 => c.ContestTough, _ => c.ContestSheen };
    private static void SetGV(IGanbaru g, int i, byte v) { switch (i) { case 0: g.GV_HP = v; break; case 1: g.GV_ATK = v; break; case 2: g.GV_DEF = v; break; case 3: g.GV_SPA = v; break; case 4: g.GV_SPD = v; break; default: g.GV_SPE = v; break; } }
    private static byte GetGV(IGanbaru g, int i) => i switch { 0 => g.GV_HP, 1 => g.GV_ATK, 2 => g.GV_DEF, 3 => g.GV_SPA, 4 => g.GV_SPD, _ => g.GV_SPE };
    private static void SetAV(IAwakened a, int i, byte v) { switch (i) { case 0: a.AV_HP = v; break; case 1: a.AV_ATK = v; break; case 2: a.AV_DEF = v; break; case 3: a.AV_SPA = v; break; case 4: a.AV_SPD = v; break; default: a.AV_SPE = v; break; } }
    private static byte GetAV(IAwakened a, int i) => i switch { 0 => a.AV_HP, 1 => a.AV_ATK, 2 => a.AV_DEF, 3 => a.AV_SPA, 4 => a.AV_SPD, _ => a.AV_SPE };

    protected override void Load(PKM pk)
    {
        pkrsCard.IsVisible = pk.Format >= 2; pkrsStrain.Text = pk.PokerusStrain.ToString(); pkrsDays.Text = pk.PokerusDays.ToString();
        contestCard.IsVisible = pk is IContestStats; if (pk is IContestStats c) for (int i = 0; i < 6; i++) contest[i].Text = GetContest(c, i).ToString();
        leafCard.IsVisible = pk is G4PKM; if (pk is G4PKM g4) for (int i = 0; i < 6; i++) leaf[i].IsChecked = (g4.ShinyLeaf & (1 << i)) != 0;
        medalCard.IsVisible = pk is ISuperTrain; if (pk is ISuperTrain st) medalState.Text = $"메달 플래그 0x{st.SuperTrainBitFlags:X8}";
        otF.Text = pk.OriginalTrainerFriendship.ToString(); htF.Text = pk.HandlingTrainerFriendship.ToString();
        affRow2.IsVisible = pk is IAffection; if (pk is IAffection a) { otA.Text = a.OriginalTrainerAffection.ToString(); htA.Text = a.HandlingTrainerAffection.ToString(); }
        gvCard.IsVisible = pk is IGanbaru; if (pk is IGanbaru g) for (int i = 0; i < 6; i++) gv[i].Text = GetGV(g, i).ToString();
        avCard.IsVisible = pk is IAwakened; if (pk is IAwakened w) for (int i = 0; i < 6; i++) av[i].Text = GetAV(w, i).ToString();
    }
}
