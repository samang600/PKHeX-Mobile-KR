using PKHeX.Core;
using PKHeX.Core.AutoMod;
using System.Reflection;
namespace PKHeXKR;

/// <summary>
/// 텍스트로 빠른 생성 엔진 (화면 없음): 주문 한 마리 → 합법 개체.
/// 모바일 앱의 "텍스트로 빠른 생성"과 PC PKHeX 플러그인이 함께 씀.
/// </summary>
public static class QuickGen
{
    /// <summary>주문 문장 해석.</summary>
    public static List<OrderSpec> Parse(string text, SaveFile sav, Dictionary<string, string> userDict) => QuickParse.Parse(text, sav, userDict);

    /// <summary>이로치 여부를 적지 않은 주문 (배포는 카드가 정하므로 제외).</summary>
    public static List<OrderSpec> NoShinySpecified(IEnumerable<OrderSpec> list) => list.Where(x => x.Species != 0 && x.Shiny == 0 && !x.Event).ToList();

    /// <summary>한 마리 만들기 (인카운터 조건을 잠시 빌려 쓰고 원래대로 돌려 놓음).</summary>
    public static (PKM Pk, string How) Make(OrderSpec o, SaveFile sav, CancellationToken tok)
    {
        var snap = SnapshotEncCond();
        try { return MakeOne(o, sav, tok); }
        finally { RestoreEncCond(snap); }
    }

    /// <summary>여러 마리를 시작 박스부터 빈 칸에. 반환: 넣은 수.</summary>
    public static int PlaceInBoxes(SaveFile sav, IList<PKM> made, int startBox)
    {
        int put = 0, box = startBox, slot = 0;
        foreach (var p in made)
        {
            while (box < sav.BoxCount && sav.GetBoxSlotAtIndex(box, slot).Species != 0) { if (++slot >= sav.BoxSlotCount) { slot = 0; box++; } }
            if (box >= sav.BoxCount) break;
            sav.SetBoxSlotAtIndex(p, box, slot); put++;
        }
        return put;
    }

    public static string Stats(PKM p)
    {
        var s = GenHost.Strings;
        var ab = p.Format >= 3 && p.Ability >= 0 && p.Ability < s.abilitylist.Length ? s.abilitylist[p.Ability] : "";
        var sc = p.Context is EntityContext.Gen8 or EntityContext.Gen8b && p is IScaledSize h8 ? $" · 키 {h8.HeightScalar} · 몸무게 {h8.WeightScalar}"   // 소드실드·BDSP는 키가 크기
            : p is IScaledSize3 z ? $" · 배율 {z.Scale}" : p is IScaledSize h ? $" · 키 {h.HeightScalar}" : "";
        return $"\n{(p.IsShiny ? (p.ShinyXor == 0 ? "■ " : "★ ") : "")}{(p.Format >= 3 ? s.natures[(int)p.Nature] : "")} {ab} · IV {p.IV_HP}/{p.IV_ATK}/{p.IV_DEF}/{p.IV_SPA}/{p.IV_SPD}/{p.IV_SPE} · Lv{p.CurrentLevel}{sc} · {s.balllist[p.Ball]}";
    }

    // ===== 한 마리 만들기 =====
    public static (PKM, string) MakeOne(OrderSpec o, SaveFile sav, CancellationToken tok)
    {
        if (!sav.Personal.IsSpeciesInGame(o.Species) || (o.Form > 0 && !sav.Personal.IsPresentInGame(o.Species, (byte)o.Form)))
            return (null, "이 게임에 없는 포켓몬이라 건너뜁니다");
        SetEncCond(o, sav);
        var candidates = Candidates(o, sav);
        foreach (var e in candidates.Take(20))
        {
            if (tok.IsCancellationRequested) break;
            var p = TryBoth(o, e, sav, tok, 4);
            if (p != null) return (Finish(o, p, sav), $"{KindName(e)} · {GenHost.GameName(e.Version)}{(e is MysteryGift mg ? $" · {mg.CardTitle} (카드 {mg.CardID})" : "")}에서 생성 · 어버이 {p.OriginalTrainerName} {p.DisplayTID}");
        }
        // 조우로 못 만들면 자동 합법화(ALM)로 (배포 주문은 배포에서만 찾음)
        if (o.Event) return (null, candidates.Count == 0 ? "조건에 맞는 배포가 없습니다" : "배포로 조건을 모두 만족하는 합법 개체를 찾지 못했습니다");
        try
        {
            var set = new ShowdownSet(Showdown(o, sav));
            var p = GenHost.Legal(sav, set);
            if (p != null && p.Species == o.Species && p.Version != GameVersion.GO && p.Version.Context == sav.Context)   // 다른 게임 출신은 만들지 않음
            {
                if (o.Ball > 0) p.Ball = (byte)o.Ball;
                EncCond.ApplyMark(p);
                // 자동 합법화는 우두머리·크기 등을 모르므로, 조건을 만족하지 않으면 쓰지 않음 (엉뚱한 개체 방지)
                bool alphaOk = o.Alpha != 1 || p is IAlpha { IsAlpha: true };
                if (!alphaOk || !EncCond.Satisfied(p) || !GenHost.IsLegal(p))
                    return (null, "조건(우두머리·크기·이로치 등)을 만족하는 합법 개체를 찾지 못했습니다");
                return (Finish(o, p, sav), "자동 합법화(ALM)로 생성");
            }
        }
        catch { }
        return (null, candidates.Count == 0 ? "조건에 맞는 조우가 없습니다 (볼·우두머리·증표·크기 조합 확인)" : "조건을 모두 만족하는 합법 개체를 찾지 못했습니다");
    }

    public static string KindName(IEncounterInfo e) => e switch { IEncounterEgg => "알", MysteryGift => "배포", _ when e.GetType().Name.StartsWith("EncounterSlot") => "야생", _ => "게임 내 입수" };

    public static List<IEncounterInfo> Candidates(OrderSpec o, SaveFile sav)
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
            (o.Event ? -GiftScore(e as MysteryGift, o) * 1000 : 0) + (e.Context == sav.Context ? 0 : 100) + (SameGame(e.Version, sav.Version) ? 0 : 20) + (e.Species == o.Species ? 0 : 10) + e switch { MysteryGift => 50, _ when e.GetType().Name.StartsWith("EncounterGift3") => 50, IEncounterEgg => 30, _ when e.GetType().Name.StartsWith("EncounterSlot") => 0, _ => 5 };
        return list.Distinct().Where(Ok).OrderBy(Rank).ToList();
    }

    /// <summary>지금 게임(또는 짝 버전·묶음)의 조우인지. 예: 파이어레드 세이브 → FR·LG·FRLG 조우를 콜로세움/XD·에메랄드보다 먼저.</summary>
    private static bool SameGame(GameVersion enc, GameVersion save)
    {
        try { return enc == save || enc.Contains(save) || save.Contains(enc) || GameUtil.GetMetLocationVersionGroup(enc) == GameUtil.GetMetLocationVersionGroup(save); } catch { return enc == save; }
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

    public static PKM TryBoth(OrderSpec o, IEncounterInfo e, SaveFile sav, CancellationToken tok, double seconds)
    {
        Seeder sd = null;
        try { sd = Seeder.For(e, GenHost.TrainerOf(sav)); } catch { }   // 시드로 만들 수 있는 조우(SV 레이드·소드실드 야생 등)는 순차·무작위 동시
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
    public static PKM TrySeq(OrderSpec o, Seeder sd, SaveFile sav, CancellationToken tok, double seconds)
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
            if (GenHost.IsLegal(c)) return c;
            if (++illegal > 300) return null;
        }
        return null;
    }

    public static PKM TryEncounter(OrderSpec o, IEncounterInfo e, SaveFile sav, CancellationToken tok, double seconds)
    {
        var crit = EncCond.Criteria(); var sw = System.Diagnostics.Stopwatch.StartNew(); int illegal = 0;
        while (!tok.IsCancellationRequested && sw.Elapsed.TotalSeconds < seconds)
        {
            PKM p;
            try { p = e.ConvertToPKM(GenHost.TrainerOf(sav), crit); } catch { return null; }
            p = EntityConverter.ConvertToType(p, sav.PKMType, out _) ?? p;
            if (e is MysteryGift mg && GenHost.DistributionDate(mg, p.Language) is { } dd)   // 배포 개체: 실제 배포 기간 시작일 (합법이 유지될 때만)
            { var t = p.Clone(); t.MetDate = dd; t.RefreshChecksum(); if (GenHost.IsLegal(t) || !GenHost.IsLegal(p)) p = t; }
            EncCond.ApplyMark(p);
            if (o.Ball > 0) p.Ball = (byte)o.Ball;
            if (o.Level > 0 && o.Level >= p.MetLevel) p.CurrentLevel = (byte)o.Level;
            // 조건(성별·이로치·성격·개체값·크기)은 진화해도 그대로라 진화 전에 먼저 확인 → 맞는 개체만 진화 (진화는 무거움)
            if (!EncCond.Satisfied(p)) continue;
            if (o.FlawlessIVs > 0 && new[] { p.IV_HP, p.IV_ATK, p.IV_DEF, p.IV_SPA, p.IV_SPD, p.IV_SPE }.Count(v => v == p.MaxIV) < o.FlawlessIVs) continue;
            if (p.Species != o.Species) { p = EvoUtil.Evolve(p, o.Species, o.Form); if (p.Species != o.Species) continue; }
            p.RefreshChecksum();
            if (GenHost.IsLegal(p)) return p;
            if (++illegal > 300) return null;   // 이 조우로는 합법이 안 나옴 (볼 불가 등)
        }
        return null;
    }

    /// <summary>마무리: Z-A 우두머리는 HP 노력치 252(게임 실제 값), 어버이 지정 반영(이로치 모양 유지).</summary>
    public static PKM Finish(OrderSpec o, PKM p, SaveFile sav)
    {
        { var t = p.Clone(); GenHost.ClearNickTrash(t); GenHost.ClearOTTrash(t); t.RefreshChecksum(); if (!GenHost.IsLegal(p) || GenHost.IsLegal(t)) p = t; }   // 쓰레기 바이트 지우기
        if (o.EV.Any(v => v >= 0))   // 노력치 (한 능력치 252, 합계 510 한도)
        {
            int sum = 0;
            for (int i = 0; i < 6; i++)
            {
                int v = Math.Max(0, o.EV[i]);
                if (p.Format >= 3) { v = Math.Min(v, 252); if (sum + v > 510) v = Math.Max(0, 510 - sum); }
                sum += v;
                switch (i) { case 0: p.EV_HP = v; break; case 1: p.EV_ATK = v; break; case 2: p.EV_DEF = v; break; case 3: p.EV_SPA = v; break; case 4: p.EV_SPD = v; break; default: p.EV_SPE = v; break; }
            }
            p.ResetPartyStats(); p.RefreshChecksum();
        }
        GenHost.FixEventDate(p);   // 배포 개체: 만든 방식(조우·자동 합법화)과 관계없이 배포 날짜로
        if (p is PA9 { IsAlpha: true } z && z.EV_HP + z.EV_ATK + z.EV_DEF + z.EV_SPA + z.EV_SPD + z.EV_SPE == 0) { z.EV_HP = 252; z.ResetPartyStats(); }
        var me = GenHost.TrainerOf(sav);
        bool giftOT = o.Event && p.FatefulEncounter && !string.IsNullOrEmpty(p.OriginalTrainerName) && p.OriginalTrainerName != me.OT;
        if (giftOT) o.Notes.Add($"배포 어버이 {p.OriginalTrainerName} {p.DisplayTID}를 그대로 사용");
        if (!giftOT && (o.OT != null || o.TID >= 0))
        {
            bool six = p.Format >= 7;
            int tid = o.TID >= 0 ? o.TID : (six ? (int)(me.ID32 % 1_000_000) : me.TID16);
            int sid = o.SID >= 0 ? o.SID : Random.Shared.Next(six ? 4295 : 65536);
            uint id32 = six ? (uint)(Math.Min(sid, 4294) * 1_000_000L + Math.Min(tid, 999_999)) : (uint)((Math.Min(sid, 65535) << 16) | Math.Min(tid, 65535));
            GenHost.AutoOT(p, o.OT ?? me.OT, id32, (byte)(o.OTGender >= 0 ? o.OTGender : me.Gender), me.Language > 0 ? me.Language : p.Language);
        }
        p.RefreshChecksum();
        return p;
    }

    public static void SetEncCond(OrderSpec o, SaveFile sav)
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

    public static string Showdown(OrderSpec o, SaveFile sav)
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
    public static Dictionary<FieldInfo, object> SnapshotEncCond() => typeof(EncCond).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => !f.IsInitOnly && !f.IsLiteral).ToDictionary(f => f, f => f.GetValue(null) is int[] a ? a.Clone() : f.GetValue(null));
    public static void RestoreEncCond(Dictionary<FieldInfo, object> s) { foreach (var (f, v) in s) try { f.SetValue(null, v); } catch { } }
}
