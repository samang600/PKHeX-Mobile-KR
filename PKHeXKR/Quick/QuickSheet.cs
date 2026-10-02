using PKHeX.Core;
using PKHeX.Core.AutoMod;
using System.Reflection;
namespace PKHeXKR;

/// <summary>텍스트로 빠른 생성: 대충 쓴 주문 문장을 해석해 합법 개체를 만듦 (테스트 기능).</summary>
public class QuickSheet : Sheet
{
    private readonly Editor input = new() { AutoSize = EditorAutoSizeOption.TextChanges, FontSize = 15, MinimumHeightRequest = 120, Placeholder = "예)\n어버이: 새아 468686\n빠르모트 암컷 이로치 러브볼 가장작게 / 찌리배리 레벨볼 큰증\n싸리용(뻗은,젖힌) 우두로치 다이브볼 가장크게\n망나뇽 암" };
    private readonly VerticalStackLayout preview = new() { Spacing = 6 };
    private readonly Label status = T.L("", 13, sub: true);
    private readonly Button parseBtn = T.Pill("해석해 보기"), makeBtn = T.Pill("만들기", primary: true), stopBtn = T.Pill("중지");
    private CancellationTokenSource cts;
    private static string lastText = "";

    public QuickSheet() : base("텍스트로 빠른 생성")
    {
        input.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
        input.Text = lastText;
        status.LineBreakMode = LineBreakMode.WordWrap;
        parseBtn.Clicked += (_, _) => ShowParse();
        makeBtn.Clicked += async (_, _) => await Make();
        stopBtn.Clicked += (_, _) => cts?.Cancel(); stopBtn.IsVisible = false;
        var dictBtn = T.Pill("내 사전"); dictBtn.Clicked += (_, _) => SheetHost.Show(new UserDictSheet());
        var frame = new Border { Content = input, StrokeThickness = 1, Padding = 6, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
        frame.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D1D5DB"), Color.FromArgb("#374151"));
        var hint = T.L("한 줄 또는 '/'마다 한 마리. 포켓몬·성별(암/수)·이로치(이로치·우두로치·별·네모)·볼(럽볼·렙볼·프볼…)·크기(가장작게·L·가장크게)·증표(작증·큰증·카레)·성격·숨특·6V·A0·Lv50·x3 등을 순서 상관없이 적으세요. '어버이: 이름 TID' 줄은 아래 전체에 적용되고, 적지 않은 값은 게임에서 자연스럽게 나오는 값으로 채웁니다. 한 마리면 편집기로, 여러 마리면 현재 박스부터 빈 칸에 넣습니다.", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, frame, new HorizontalStackLayout { Spacing = 8, Children = { parseBtn, makeBtn, dictBtn, stopBtn } }, status, preview } } });
    }

    private List<OrderSpec> Parse() { lastText = input.Text ?? ""; return QuickParse.Parse(lastText, AppState.Sav, UserDict.Load()); }

    private void ShowParse()
    {
        var list = Parse(); preview.Children.Clear();
        foreach (var o in list) preview.Children.Add(Line(o, null));
        status.Text = $"{list.Count}마리로 해석했습니다" + (list.Any(x => x.Unknown.Count > 0) ? " · '?' 표시 단어는 알아보지 못해 무시합니다" : "");
    }

    private static View Line(OrderSpec o, string result, bool? ok = null)
    {
        var s = GameInfo.Strings;
        var main = T.L((ok == null ? "" : ok.Value ? "✔ " : "✖ ") + (o.Species == 0 ? "(포켓몬 없음)" : o.Describe(s)), 14, bold: true); main.LineBreakMode = LineBreakMode.WordWrap;
        if (ok == false) main.TextColor = T.Bad;
        var sub = T.L("입력: " + o.Raw.Trim() + (o.Unknown.Count > 0 ? "   ? " + string.Join(", ", o.Unknown) : "") + (o.Notes.Count > 0 ? "   ※ " + string.Join("; ", o.Notes) : "") + (result != null ? "\n" + result : ""), 12, sub: true);
        sub.LineBreakMode = LineBreakMode.WordWrap;
        var img = new Image { WidthRequest = 44, HeightRequest = 38, Source = o.Species == 0 ? null : AppState.Sprite(o.Species, (byte)Math.Max(0, o.Form), o.Shiny is 1 or 3 or 4) };
        var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 8 };
        g.Add(img, 0); g.Add(new VerticalStackLayout { Spacing = 2, Children = { main, sub } }, 1);
        return g;
    }

    private async Task Make()
    {
        var list = Parse();
        if (list.Count == 0) { status.Text = "입력한 내용이 없습니다"; return; }
        var noShiny = list.Where(x => x.Species != 0 && x.Shiny == 0 && !x.Event).ToList();   // 배포는 카드가 이로치 여부를 정함
        if (noShiny.Count > 0)
        {
            var names = string.Join(", ", noShiny.Take(5).Select(x => GameInfo.Strings.Species[x.Species])) + (noShiny.Count > 5 ? " 외" : "");
            var pick = await Application.Current.Windows[0].Page.DisplayActionSheetAsync($"이로치 여부를 적지 않은 포켓몬 {noShiny.Count}마리 ({names})", "취소", null, "이로치로 만들기", "일반 색으로 만들기");
            if (pick == null || pick == "취소") return;
            foreach (var x in noShiny) x.Shiny = pick.StartsWith("이로치") ? 1 : 2;
        }
        cts = new CancellationTokenSource(); var tok = cts.Token;
        makeBtn.IsEnabled = parseBtn.IsEnabled = false; stopBtn.IsVisible = true; preview.Children.Clear();
        var made = new List<PKM>(); var sav = AppState.Sav; int total = list.Sum(x => x.Species == 0 ? 1 : x.Count), done = 0;
        var snap = SnapshotEncCond();
        try
        {
            foreach (var o in list)
            {
                if (tok.IsCancellationRequested) break;
                if (o.Species == 0) { preview.Children.Add(Line(o, "포켓몬 이름을 알아보지 못했습니다", false)); done++; continue; }
                for (int k = 0; k < o.Count && !tok.IsCancellationRequested; k++)
                {
                    status.Text = $"만드는 중… {done + 1}/{total} · {GameInfo.Strings.Species[o.Species]}";
                    var (pk, how) = await Task.Run(() => MakeOne(o, sav, tok));
                    done++;
                    bool ok = pk != null && AppState.IsLegal(pk);
                    if (pk != null) made.Add(pk);
                    preview.Children.Add(Line(o, pk == null ? how : $"{how} · {(ok ? "합법" : "불법 — 편집기에서 합법성 표시를 확인하세요")}{Stats(pk)}", pk != null && ok));
                }
            }
        }
        finally { RestoreEncCond(snap); makeBtn.IsEnabled = parseBtn.IsEnabled = true; stopBtn.IsVisible = false; }
        if (made.Count == 0) { status.Text = "만든 포켓몬이 없습니다"; return; }
        if (made.Count == 1)
        {
            status.Text = "편집기로 불러왔습니다";
            Close(); AppState.Load(made[0]); AppState.Edited(); Note.Show("텍스트로 만든 포켓몬을 불러왔습니다");
            return;
        }
        int put = 0, box = AppState.Box, slot = 0;
        foreach (var p in made)
        {
            while (box < sav.BoxCount && sav.GetBoxSlotAtIndex(box, slot).Species != 0) { if (++slot >= sav.BoxSlotCount) { slot = 0; box++; } }
            if (box >= sav.BoxCount) break;
            sav.SetBoxSlotAtIndex(p, box, slot); put++;
        }
        AppState.NotifyBox();
        status.Text = $"{made.Count}마리 중 {put}마리를 {AppState.BoxName(AppState.Box)}부터 빈 칸에 넣었습니다" + (put < made.Count ? " (박스가 가득 참)" : "");
    }

    private static string Stats(PKM p)
    {
        var s = GameInfo.Strings;
        var ab = p.Format >= 3 && p.Ability >= 0 && p.Ability < s.abilitylist.Length ? s.abilitylist[p.Ability] : "";
        var sc = p.Context is EntityContext.Gen8 or EntityContext.Gen8b && p is IScaledSize h8 ? $" · 키 {h8.HeightScalar} · 몸무게 {h8.WeightScalar}"   // 소드실드·BDSP는 키가 크기
            : p is IScaledSize3 z ? $" · 배율 {z.Scale}" : p is IScaledSize h ? $" · 키 {h.HeightScalar}" : "";
        return $"\n{(p.IsShiny ? (p.ShinyXor == 0 ? "■ " : "★ ") : "")}{(p.Format >= 3 ? s.natures[(int)p.Nature] : "")} {ab} · IV {p.IV_HP}/{p.IV_ATK}/{p.IV_DEF}/{p.IV_SPA}/{p.IV_SPD}/{p.IV_SPE} · Lv{p.CurrentLevel}{sc} · {s.balllist[p.Ball]}";
    }

    // ===== 한 마리 만들기 =====
    private static (PKM, string) MakeOne(OrderSpec o, SaveFile sav, CancellationToken tok)
    {
        if (!sav.Personal.IsSpeciesInGame(o.Species) || (o.Form > 0 && !sav.Personal.IsPresentInGame(o.Species, (byte)o.Form)))
            return (null, "이 게임에 없는 포켓몬이라 건너뜁니다");
        SetEncCond(o, sav);
        var candidates = Candidates(o, sav);
        foreach (var e in candidates.Take(20))
        {
            if (tok.IsCancellationRequested) break;
            var p = TryBoth(o, e, sav, tok, 4);
            if (p != null) return (Finish(o, p, sav), $"{KindName(e)} · {AppState.GameName(e.Version)}{(e is MysteryGift mg ? $" · {mg.CardTitle} (카드 {mg.CardID})" : "")}에서 생성 · 어버이 {p.OriginalTrainerName} {p.DisplayTID}");
        }
        // 조우로 못 만들면 자동 합법화(ALM)로 (배포 주문은 배포에서만 찾음)
        if (o.Event) return (null, candidates.Count == 0 ? "조건에 맞는 배포가 없습니다" : "배포로 조건을 모두 만족하는 합법 개체를 찾지 못했습니다");
        try
        {
            var set = new ShowdownSet(Showdown(o, sav));
            var p = sav.GetLegalFromSet(set).Created;
            if (p != null && p.Species == o.Species && p.Version != GameVersion.GO && p.Version.Context == sav.Context)   // 다른 게임 출신은 만들지 않음
            {
                if (o.Ball > 0) p.Ball = (byte)o.Ball;
                EncCond.ApplyMark(p);
                // 자동 합법화는 우두머리·크기 등을 모르므로, 조건을 만족하지 않으면 쓰지 않음 (엉뚱한 개체 방지)
                bool alphaOk = o.Alpha != 1 || p is IAlpha { IsAlpha: true };
                if (!alphaOk || !EncCond.Satisfied(p) || !AppState.IsLegal(p))
                    return (null, "조건(우두머리·크기·이로치 등)을 만족하는 합법 개체를 찾지 못했습니다");
                return (Finish(o, p, sav), "자동 합법화(ALM)로 생성");
            }
        }
        catch { }
        return (null, candidates.Count == 0 ? "조건에 맞는 조우가 없습니다 (볼·우두머리·증표·크기 조합 확인)" : "조건을 모두 만족하는 합법 개체를 찾지 못했습니다");
    }

    private static string KindName(IEncounterInfo e) => e switch { IEncounterEgg => "알", MysteryGift => "배포", _ when e.GetType().Name.StartsWith("EncounterSlot") => "야생", _ => "게임 내 입수" };

    private static List<IEncounterInfo> Candidates(OrderSpec o, SaveFile sav)
    {
        var list = new List<IEncounterInfo>();
        try
        {
            var blank = sav.BlankPKM; var pi = sav.Personal.GetFormEntry(o.Species, 0);
            var versions = GameUtil.GetVersionsWithinRange(blank, blank.Context).ToArray();
            for (byte f = 0; f < pi.FormCount; f++)
            {
                if (o.Form >= 0 && f != o.Form) continue;
                if (FormInfo.IsBattleOnlyForm(o.Species, f, blank.Format)) continue;
                blank.Species = o.Species; blank.Form = f; blank.SetGender(o.Gender >= 0 && !blank.PersonalInfo.Genderless ? (byte)o.Gender : blank.GetSaneGender());
                EncounterMovesetGenerator.OptimizeCriteria(blank, sav);
                list.AddRange(EncounterMovesetGenerator.GenerateEncounters(blank, sav, ReadOnlyMemory<ushort>.Empty, versions));
            }
        }
        catch { }
        bool Ok(IEncounterInfo e)
        {
            if (e.Version == GameVersion.GO || e.GetType().Name.Contains("GO")) return false;   // 포켓몬 GO에서는 만들지 않음
            if (e.Context != sav.Context) return false;                                           // 현재 게임 출신만 (다른 게임에서 전송된 개체는 만들지 않음)
            if (o.Event && e is not MysteryGift) return false;                                    // "배포"면 배포에서만
            if (o.PreSpecies != 0 && e.Species != o.PreSpecies && e.Species != o.Species) return false;
            if (o.Form >= 0 && e.Species == o.Species && e.Form != o.Form) return false;
            if (EncCond.Impossible(e) != null) return false;
            if (o.Alpha == 1 && e is not IAlphaReadOnly { IsAlpha: true }) return false;
            if (o.Alpha == 0 && e is IAlphaReadOnly { IsAlpha: true }) return false;
            if (o.Ball > 0 && e is IFixedBall { FixedBall: not Ball.None } fb && (int)fb.FixedBall != o.Ball) return false;
            return true;
        }
        int Rank(IEncounterInfo e) =>
            (o.Event ? -GiftScore(e as MysteryGift, o) * 1000 : 0) + (e.Context == sav.Context ? 0 : 100) + (e.Species == o.Species ? 0 : 10) + e switch { MysteryGift => 50, IEncounterEgg => 30, _ when e.GetType().Name.StartsWith("EncounterSlot") => 0, _ => 5 };
        return list.Distinct().Where(Ok).OrderBy(Rank).ToList();
    }

    /// <summary>배포 카드가 단서와 얼마나 맞는지 (어버이 이름·카드 제목·닉네임, 모든 언어).</summary>
    public static int GiftScore(MysteryGift g, OrderSpec o)
    {
        if (g == null || o.Keywords.Count == 0) return 0;
        var texts = new List<string> { g.CardTitle ?? "", g.OriginalTrainerName ?? "" };
        foreach (var name in new[] { "GetOT", "GetNickname" })
        {
            var mi = g.GetType().GetMethod(name, [typeof(int)]);
            if (mi != null) foreach (var lang in new[] { 1, 2, 3, 4, 5, 7, 8, 9, 10 }) try { if (mi.Invoke(g, [lang]) is string x) texts.Add(x); } catch { }
        }
        var all = string.Join(" ", texts).ToLowerInvariant().Replace(" ", "");
        return o.Keywords.Count(k => k.Length > 0 && all.Contains(k.ToLowerInvariant().Replace(" ", "")));
    }

    private static PKM TryBoth(OrderSpec o, IEncounterInfo e, SaveFile sav, CancellationToken tok, double seconds)
    {
        Seeder sd = null;
        if (e is EncounterSlot8) { try { sd = Seeder.For(e, sav); } catch { } }
        if (sd == null) return TryEncounter(o, e, sav, tok, seconds);
        using var race = CancellationTokenSource.CreateLinkedTokenSource(tok);
        var a = Task.Run(() => TryEncounter(o, e, sav, race.Token, seconds));
        var b = Task.Run(() => TrySeq(o, sd, sav, race.Token, seconds));
        var tasks = new List<Task<PKM>> { a, b };
        while (tasks.Count > 0)
        {
            int i = Task.WaitAny(tasks.ToArray());
            var r = tasks[i].Result; tasks.RemoveAt(i);
            if (r != null) { race.Cancel(); return r; }
        }
        return null;
    }

    /// <summary>시드를 0부터 순서대로 (소드실드 야생 등).</summary>
    private static PKM TrySeq(OrderSpec o, Seeder sd, SaveFile sav, CancellationToken tok, double seconds)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew(); var p = sd.Template.Clone(); int illegal = 0;
        for (ulong seed = 0; !tok.IsCancellationRequested && sw.Elapsed.TotalSeconds < seconds && (!sd.Is32 || seed <= 0xFFFFFFFF); seed++)
        {
            bool ok; try { ok = sd.Gen(p, seed); } catch { ok = false; }
            if (!ok) continue;
            var c = sd.Finish(p.Clone());
            EncCond.ApplyMark(c);
            if (o.Ball > 0) c.Ball = (byte)o.Ball;
            if (o.Level > 0 && o.Level >= c.MetLevel) c.CurrentLevel = (byte)o.Level;
            if (!EncCond.Satisfied(c)) continue;
            if (c.Species != o.Species) { c = EvoUtil.Evolve(c, o.Species, o.Form); if (c.Species != o.Species) continue; }
            if (false) continue;
            if (o.FlawlessIVs > 0 && new[] { c.IV_HP, c.IV_ATK, c.IV_DEF, c.IV_SPA, c.IV_SPD, c.IV_SPE }.Count(v => v == c.MaxIV) < o.FlawlessIVs) continue;
            c.RefreshChecksum();
            if (AppState.IsLegal(c)) return c;
            if (++illegal > 300) return null;
        }
        return null;
    }

    private static PKM TryEncounter(OrderSpec o, IEncounterInfo e, SaveFile sav, CancellationToken tok, double seconds)
    {
        var crit = EncCond.Criteria(); var sw = System.Diagnostics.Stopwatch.StartNew(); int illegal = 0;
        while (!tok.IsCancellationRequested && sw.Elapsed.TotalSeconds < seconds)
        {
            PKM p;
            try { p = e.ConvertToPKM(sav, crit); } catch { return null; }
            p = EntityConverter.ConvertToType(p, sav.PKMType, out _) ?? p;
            EncCond.ApplyMark(p);
            if (o.Ball > 0) p.Ball = (byte)o.Ball;
            if (o.Level > 0 && o.Level >= p.MetLevel) p.CurrentLevel = (byte)o.Level;
            // 조건(성별·이로치·성격·개체값·크기)은 진화해도 그대로라 진화 전에 먼저 확인 → 맞는 개체만 진화 (진화는 무거움)
            if (!EncCond.Satisfied(p)) continue;
            if (o.FlawlessIVs > 0 && new[] { p.IV_HP, p.IV_ATK, p.IV_DEF, p.IV_SPA, p.IV_SPD, p.IV_SPE }.Count(v => v == p.MaxIV) < o.FlawlessIVs) continue;
            if (p.Species != o.Species) { p = EvoUtil.Evolve(p, o.Species, o.Form); if (p.Species != o.Species) continue; }
            p.RefreshChecksum();
            if (AppState.IsLegal(p)) return p;
            if (++illegal > 300) return null;   // 이 조우로는 합법이 안 나옴 (볼 불가 등)
        }
        return null;
    }

    /// <summary>마무리: Z-A 우두머리는 HP 노력치 252(게임 실제 값), 어버이 지정 반영(이로치 모양 유지).</summary>
    private static PKM Finish(OrderSpec o, PKM p, SaveFile sav)
    {
        if (p is PA9 { IsAlpha: true } z && z.EV_HP + z.EV_ATK + z.EV_DEF + z.EV_SPA + z.EV_SPD + z.EV_SPE == 0) { z.EV_HP = 252; z.ResetPartyStats(); }
        bool giftOT = o.Event && p.FatefulEncounter && !string.IsNullOrEmpty(p.OriginalTrainerName) && p.OriginalTrainerName != sav.OT;
        if (giftOT) o.Notes.Add($"배포 어버이 {p.OriginalTrainerName} {p.DisplayTID}를 그대로 사용");
        if (!giftOT && (o.OT != null || o.TID >= 0))
        {
            bool six = p.Format >= 7;
            int tid = o.TID >= 0 ? o.TID : (six ? (int)(sav.ID32 % 1_000_000) : sav.TID16);
            int sid = o.SID >= 0 ? o.SID : Random.Shared.Next(six ? 4295 : 65536);
            uint id32 = six ? (uint)(Math.Min(sid, 4294) * 1_000_000L + Math.Min(tid, 999_999)) : (uint)((Math.Min(sid, 65535) << 16) | Math.Min(tid, 65535));
            AppState.AutoOT(p, new AppState.Partner(o.OT ?? sav.OT, id32, six, (byte)(o.OTGender >= 0 ? o.OTGender : sav.Gender), sav.Language > 0 ? sav.Language : p.Language));
        }
        p.RefreshChecksum();
        return p;
    }

    private static void SetEncCond(OrderSpec o, SaveFile sav)
    {
        EncCond.Gender = o.Gender;
        EncCond.ShinyMode = o.Shiny;   // 0 무관, 1 이로치, 2 일반, 3 별, 4 네모
        EncCond.Nature = o.Nature; EncCond.Ability = o.Ability;
        EncCond.IVExact = o.ExactIV.Any(v => v >= 0);
        EncCond.IVMin = EncCond.IVExact ? (int[])o.ExactIV.Clone() : new int[6];
        EncCond.HMin = EncCond.WMin = EncCond.SMin = 0; EncCond.HMax = EncCond.WMax = EncCond.SMax = 255;
        if (o.ScaleMin >= 0)
        {
            if (sav.Context is EntityContext.Gen9 or EntityContext.Gen9a or EntityContext.Gen8a) { EncCond.SMin = o.ScaleMin; EncCond.SMax = o.ScaleMax; }   // SV·Z-A·PLA: 배율
            else { EncCond.HMin = o.ScaleMin; EncCond.HMax = o.ScaleMax; }
        }
        EncCond.Mark = o.Mark;
        EncCond.SeedMode = 0;
    }

    private static string Showdown(OrderSpec o, SaveFile sav)
    {
        var en = GameInfo.GetStrings("en");
        var blank = sav.BlankPKM; blank.Species = o.Species; blank.Form = (byte)Math.Max(0, o.Form);
        var first = ShowdownParsing.GetShowdownText(blank).Split('\n')[0].Trim();
        first = System.Text.RegularExpressions.Regex.Replace(first, @"\s*\((M|F)\)\s*$", "");
        var lines = new List<string> { first + (o.Gender == 0 ? " (M)" : o.Gender == 1 ? " (F)" : "") };
        if (o.Shiny is 1 or 3 or 4) lines.Add("Shiny: Yes");
        if (o.Ball > 0) lines.Add($"Ball: {en.balllist[o.Ball]}");
        if (o.Level > 0) lines.Add($"Level: {o.Level}");
        if (o.Nature >= 0) lines.Add($"{en.natures[o.Nature]} Nature");
        if (o.FlawlessIVs == 6) lines.Add("IVs: 31 HP / 31 Atk / 31 Def / 31 SpA / 31 SpD / 31 Spe");
        return string.Join("\n", lines);
    }

    // 인카운터 검색 조건을 잠시 빌려 쓰고 원래대로 돌려 놓음
    private static Dictionary<FieldInfo, object> SnapshotEncCond() => typeof(EncCond).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => !f.IsInitOnly && !f.IsLiteral).ToDictionary(f => f, f => f.GetValue(null) is int[] a ? a.Clone() : f.GetValue(null));
    private static void RestoreEncCond(Dictionary<FieldInfo, object> s) { foreach (var (f, v) in s) try { f.SetValue(null, v); } catch { } }
}


/// <summary>사용자 줄임말 사전 (한 줄에 "줄임말 = 바꿀 말", 여러 마리는 "/"로).</summary>
public static class UserDict
{
    public static string Text { get => Preferences.Get("quick_alias", ""); set => Preferences.Set("quick_alias", value ?? ""); }
    public static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>();
        foreach (var line in Text.Replace("\r", "").Split('\n'))
        {
            var i = line.IndexOf('='); if (i <= 0) continue;
            var k = line[..i].Trim(); var v = line[(i + 1)..].Trim();
            if (k.Length > 0 && v.Length > 0) d[k] = v;
        }
        return d;
    }
}

public class UserDictSheet : Sheet
{
    public UserDictSheet() : base("내 사전 (줄임말·별칭)")
    {
        var ed = new Editor { Text = UserDict.Text, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 160, FontSize = 15, Placeholder = "한 줄에 하나씩\n포니 = 포푸니라\n럽볼 = 러브러브볼\n전설쌍 = 칠색조 / 루기아" };
        ed.SetAppThemeColor(Editor.TextColorProperty, Colors.Black, Colors.White);
        var frame = new Border { Content = ed, StrokeThickness = 1, Padding = 6, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
        frame.SetAppThemeColor(Border.StrokeProperty, Color.FromArgb("#D1D5DB"), Color.FromArgb("#374151"));
        var save = T.Pill("저장", primary: true); save.Clicked += (_, _) => { UserDict.Text = ed.Text; Note.Show($"사전 {UserDict.Load().Count}개를 저장했습니다"); Close(); };
        var hint = T.L("'줄임말 = 바꿀 말' 형식으로 한 줄에 하나씩 적습니다. 바꿀 말에 '/'를 넣으면 여러 마리로 나뉩니다. 내 사전이 기본 사전보다 먼저 적용됩니다.", 12, sub: true); hint.LineBreakMode = LineBreakMode.WordWrap;
        var builtIn = T.L("기본 사전: " + string.Join(", ", QuickParse.BuiltInAlias.Select(kv => $"{kv.Key}→{kv.Value}")), 11, sub: true); builtIn.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, frame, save, builtIn } } });
    }
}
