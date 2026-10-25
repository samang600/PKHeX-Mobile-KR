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

    /// <summary>
    /// 인카운터 조건(EncCond)은 전역이라, 조건을 빌려 쓰는 생성(여기)과 조건을 읽는 인카운터 검색이 겹치면 안 됨
    /// (리빙덱스를 돌려 두고 인카운터 검색을 열면 서로 조건을 덮어씀) → 한 번에 하나만.
    /// </summary>
    public static readonly SemaphoreSlim CondGate = new(1, 1);

    /// <summary>한 마리 만들기 (인카운터 조건을 잠시 빌려 쓰고 원래대로 돌려 놓음).</summary>
    public static (PKM Pk, string How) Make(OrderSpec o, SaveFile sav, CancellationToken tok)
    {
        try { CondGate.Wait(tok); } catch (OperationCanceledException) { return (null, "중지했습니다"); }
        var snap = SnapshotEncCond();
        try { return MakeOne(o, sav, tok); }
        finally { RestoreEncCond(snap); CondGate.Release(); }
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

    /// <summary>이 게임(세이브)에 그 포켓몬·폼이 있는지.</summary>
    public static bool InGame(OrderSpec o, SaveFile sav) => InGame(o, sav.Personal);
    public static bool InGame(OrderSpec o, GameVersion v) { try { return InGame(o, GameData.GetPersonal(v)); } catch { return false; } }
    private static bool InGame(OrderSpec o, IPersonalTable pt)
    {
        try { return o.Species != 0 && o.Species <= pt.MaxSpeciesID && pt.IsSpeciesInGame(o.Species) && (o.Form <= 0 || pt.IsPresentInGame(o.Species, (byte)o.Form)); }
        catch { return false; }
    }
    public const string NotInGame = "이 게임에 없는 포켓몬이라 건너뜁니다";
    public const string NoEncounter = "조건에 맞는 조우가 없습니다";

    /// <summary>이 주문을 만들 수 있는 다른 게임 (games 순서, 지금 게임과 같은 세대 묶음은 뺌).</summary>
    public static List<GameVersion> OtherGames(OrderSpec o, SaveFile sav, IEnumerable<GameVersion> games)
        => games.Where(v => { try { return v.Context != sav.Context && InGame(o, v); } catch { return false; } }).ToList();

    // ===== 한 마리 만들기 =====
    public static (PKM, string) MakeOne(OrderSpec o, SaveFile sav, CancellationToken tok)
    {
        if (!InGame(o, sav))
            return (null, NotInGame);
        if (o.Ball == StrangeBall.BallId) return StrangeBall.Make(o, sav, tok);   // 스트레인지볼: GO 와일드 에어리어 출신으로만 가능
        SetEncCond(o, sav);
        var candidates = Candidates(o, sav);
        foreach (var e in candidates.Take(o.SkipAlm ? 12 : 20))
        {
            if (tok.IsCancellationRequested) break;
            var p = TryBoth(o, e, sav, tok, o.Seconds);
            if (p != null) return (Finish(o, p, sav), $"{KindName(e)} · {GenHost.GameName(e.Version)}{(e is MysteryGift mg ? $" · {mg.CardTitle} (카드 {mg.CardID})" : "")}에서 생성 · 어버이 {p.OriginalTrainerName} {p.DisplayTID}");
        }
        // 조우로 못 만들면 자동 합법화(ALM)로 (배포 주문은 배포에서만 찾음)
        if (o.Event) return (null, candidates.Count == 0 ? "조건에 맞는 배포가 없습니다" : "배포로 조건을 모두 만족하는 합법 개체를 찾지 못했습니다");
        if (o.SkipAlm) return (null, "조우로 만들지 못했습니다");   // 리빙덱스: 자동 합법화는 이미 해 봄
        try
        {
            var set = new ShowdownSet(Showdown(o, sav));
            var p = GenHost.Legal(sav, set);
            if (p != null && o.Form >= 0 && p.Form != o.Form) p = null;   // 다른 폼으로 만들어짐
            if (p != null && p.Species == o.Species && (o.AllowTransfer || (p.Version != GameVersion.GO && p.Version.Context == sav.Context)))   // 다른 게임 출신은 만들지 않음 (리빙덱스는 허용)
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
        return (null, candidates.Count == 0 ? NoEncounter + " (볼·우두머리·증표·크기 조합 확인)" : "조건을 모두 만족하는 합법 개체를 찾지 못했습니다");
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
                list.AddRange(EncounterMovesetGenerator.GenerateEncounters(blank, sav, o.Moves.ToArray(), versions));
            }
        }
        catch { }
        bool Ok(IEncounterInfo e)
        {
            if (!o.AllowTransfer)
            {
                if (e.Version == GameVersion.GO || e.GetType().Name.Contains("GO")) return false;   // 포켓몬 GO에서는 만들지 않음
                if (e.Context != sav.Context && !(e is WC8 { IsHOMEGift: true } && sav.Context == EntityContext.Gen8b)) return false;   // 현재 게임 출신만 (HOME 선물은 BDSP로도 옮겨짐)
            }
            if (o.Event && e is not MysteryGift) return false;                                    // "배포"면 배포에서만
            if (o.PreSpecies != 0 && e.Species != o.PreSpecies && e.Species != o.Species) return false;
            if (o.Form >= 0 && e.Species == o.Species && e.Form != o.Form && e.Form < EncounterUtil.FormDynamic) return false;   // 30·31: 무늬·폼이 정해지지 않은 조우 (비비용 등)
            if (EncCond.Impossible(e) != null) return false;
            if (GenHost.ExcludeUntradable && TradeRestrictions.IsUntradableEncounter(e)) return false;   // 라이드 코라이돈·미라이돈 등
            if (o.Alpha == 1 && e is not IAlphaReadOnly { IsAlpha: true }) return false;
            if (o.Alpha == 0 && e is IAlphaReadOnly { IsAlpha: true }) return false;
            if (o.Ball > 0 && e is IFixedBall { FixedBall: not Ball.None } fb && (int)fb.FixedBall != o.Ball) return false;
            return true;
        }
        int Rank(IEncounterInfo e) =>
            (o.Event ? -GiftScore(e as MysteryGift, o) * 1000 : 0) + (e.Context == sav.Context ? 0 : 100)
            // 우두머리를 적지 않았으면 일반 조우(진화 전 포함)를 먼저: 갑주무사·리자몽·데덴네 등 Z-A에서 같은 종 조우가 우두머리뿐이면
            // 진화 전 일반 조우보다 앞서 뽑혀 '이로치'만 적어도 우두머리가 되던 문제. 일반 조우가 없을 때만 우두머리로.
            + (o.Alpha < 0 && e is IAlphaReadOnly { IsAlpha: true } ? 60 : 0) + (o.OT != null && e is IFixedTrainer { IsFixedTrainer: true } ? 40 : 0) + (SameGame(e.Version, sav.Version) ? 0 : 20) + (e.Species == o.Species ? 0 : 10) + e switch { MysteryGift => 50, _ when e.GetType().Name.StartsWith("EncounterGift3") => 50, IEncounterEgg => 30, _ when e.GetType().Name.StartsWith("EncounterSlot") => 0, _ => 5 };
        var sorted = list.Distinct().Where(Ok).OrderBy(Rank).ToList();
        if (sav.Generation > 4 && !o.AllowTransfer) return sorted;
        // 3·4세대: 같은 종류·버전·종의 조우(예: 장소만 다른 야생 칸 수십 개)는 앞쪽에 3개까지만 → 알 등 다른 방식도 시도 순서 안에 들어오게
        return sorted
            .GroupBy(e => (e.GetType().Name, e.Version, e.Species, e.Form)).SelectMany(g => g.Take(3).Select((e, i) => (e, i)))
            .OrderBy(x => x.i).ThenBy(x => Rank(x.e)).Select(x => x.e).ToList();
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
        if (texts.Any(t => t.Contains("ミツリン"))) texts.Add("미츠린 일본판");   // HOME FRLG 보상 세레비 (일본어판 어버이)
        if (texts.Any(t => t == "HOME")) texts.Add("홈");
        var all = string.Join(" ", texts).ToLowerInvariant().Replace(" ", "");
        return o.Keywords.Count(k => k.Length > 0 && all.Contains(k.ToLowerInvariant().Replace(" ", "")));
    }

    /// <summary>
    /// 레벨 올리기. 레전즈 Z-A는 레벨이 오르면 그 레벨까지 배운 기술의 '플러스 기술' 표시가 켜져 있어야 함
    /// (안 켜면 'Plus Move flag ... must be set'으로 불법) → 레벨을 바꾼 뒤 지금 레벨 기준으로 다시 맞춤.
    /// </summary>
    public static void SetLevel(PKM p, int level)
    {
        p.CurrentLevel = (byte)level;
        if (p is PA9 z && z.PersonalInfo is IPermitPlus permit)
        {
            try { z.SetPlusFlags(permit, PlusRecordApplicatorOption.LegalCurrent); } catch { }
        }
    }

    public static PKM TryBoth(OrderSpec o, IEncounterInfo e, SaveFile sav, CancellationToken tok, double seconds)
    {
        Seeder sd = null;
        try { sd = Seeder.For(e, GenHost.TrainerOf(sav)); } catch { }   // 시드로 만들 수 있는 조우(SV 레이드·소드실드 야생 등)는 순차·무작위 동시
        // 3·4세대 개체값 고정 주문: 개체값→시드 역산으로 가능한 경우를 모두 확인하므로 시드 순차 탐색(최대 4초)은 할 필요 없음
        if (sd == null || (sav.Generation is 3 or 4 && IvVectors(o, EncCond.Criteria()) != null)) return TryEncounter(o, e, sav, tok, seconds);
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
            if (o.Level > 0 && o.Level >= c.MetLevel) SetLevel(c, o.Level);
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
        var crit = EncCond.Criteria(); var sw = System.Diagnostics.Stopwatch.StartNew(); int illegal = 0; bool fail = false, mismatch = false; bool? sizeFree = null;
        // 6V: 조우에 개체값을 31로 정해 달라고 함 (안 그러면 무작위 개체값이 우연히 6V가 될 때까지 돌아 증표·크기 조건과 같이 쓰면 만들어지지 않았음)
        if (o.FlawlessIVs == 6 && crit is { IV_HP: < 0, IV_ATK: < 0, IV_DEF: < 0, IV_SPA: < 0, IV_SPD: < 0, IV_SPE: < 0 })
            crit = crit with { IV_HP = 31, IV_ATK = 31, IV_DEF = 31, IV_SPA = 31, IV_SPD = 31, IV_SPE = 31 };
        var tr = TrainerFor(e, o, sav);
        // 3·4세대 + 개체값 고정(6V·5V·개체값 전부 지정): PKHeX가 개체값에서 시드를 거꾸로 찾아 성격·성별까지 정해서 만듦 (결과가 정해져 있음)
        // → 무작위로 4초 돌리는 대신 가능한 개체값 조합만 몇 번씩 시도하고, 안 되면 바로 다음 조우로
        if (sav.Generation is 3 or 4 && IvVectors(o, crit) is { } vecs)
        {
            // 이 조우가 개체값 지정을 따르는지 먼저 확인 (다른 조건 없이 한 번 만들어 봄). 4세대 야생 등은 개체값을 무작위로 정함 → 바로 다음 조우로
            bool Honors(sbyte[] v)
            {
                try
                {
                    var t = e.ConvertToPKM(tr, EncounterCriteria.Unrestricted with { IV_HP = v[0], IV_ATK = v[1], IV_DEF = v[2], IV_SPA = v[3], IV_SPD = v[4], IV_SPE = v[5] });
                    return t.IV_HP == v[0] && t.IV_ATK == v[1] && t.IV_DEF == v[2] && t.IV_SPA == v[3] && t.IV_SPD == v[4] && t.IV_SPE == v[5];
                }
                catch { return false; }
            }
            // 이로치 + 개체값 고정: 3·4세대 야생·고정 조우는 성격값(PID)과 개체값이 같은 난수에서 나와 둘 다 맞출 수 없음 (PKHeX도 개체값만 맞춤)
            // → 알(성격값이 따로 정해짐)이나 이로치가 정해진 조우만 시도
            if (EncCond.ShinyMode is 1 or 3 or 4 && e is not IEncounterEgg && e is not IShinyPotential { Shiny: Shiny.Always or Shiny.AlwaysSquare or Shiny.AlwaysStar or Shiny.FixedValue }) return null;
            if (!vecs.Take(4).Any(Honors)) return null;
            foreach (var v in vecs)
            {
                var c2 = crit with { IV_HP = v[0], IV_ATK = v[1], IV_DEF = v[2], IV_SPA = v[3], IV_SPD = v[4], IV_SPE = v[5] };
                for (int k = 0; k < 4 && !fail && !tok.IsCancellationRequested && sw.Elapsed.TotalSeconds < seconds; k++)
                {
                    mismatch = false;
                    if (Attempt(c2, v) is { } r) return r;
                    if (mismatch) break;   // 이 개체값 조합은 (성격 등과 함께) 이 조우로 불가 → 다음 조합
                }
                if (fail || tok.IsCancellationRequested || sw.Elapsed.TotalSeconds >= seconds) return null;
            }
            return null;
        }
        while (!tok.IsCancellationRequested && sw.Elapsed.TotalSeconds < seconds && !fail)
            if (Attempt(crit, null) is { } r) return r;
        return null;

        PKM Attempt(EncounterCriteria c, sbyte[] want)
        {
            PKM p;
            try { p = e.ConvertToPKM(tr, c); } catch { fail = true; return null; }
            p = EntityConverter.ConvertToType(p, sav.PKMType, out _) ?? p;
            if (p.GetType() != sav.PKMType) { fail = true; return null; }   // 이 게임 형식으로 옮길 수 없음
            // 개체값을 정해 달라고 했는데 다르게 나옴 = 이 조합은 이 조우로 못 만듦 (무작위로 6V·5V가 나올 확률은 사실상 0)
            if (want != null && (p.IV_HP != want[0] || p.IV_ATK != want[1] || p.IV_DEF != want[2] || p.IV_SPA != want[3] || p.IV_SPD != want[4] || p.IV_SPE != want[5])) { mismatch = true; return null; }
            if (e is MysteryGift mg && GenHost.DistributionDate(mg, p.Language) is { } dd)   // 배포 개체: 실제 배포 기간 시작일 (합법이 유지될 때만)
            { var t = p.Clone(); t.MetDate = dd; t.RefreshChecksum(); if (GenHost.IsLegal(t) || !GenHost.IsLegal(p)) p = t; }
            EncCond.ApplyMark(p);
            if (o.Ball > 0) p.Ball = (byte)o.Ball;
            if (o.Level > 0 && o.Level >= p.MetLevel) SetLevel(p, o.Level);
            // 조건(성별·이로치·성격·개체값·크기)은 진화해도 그대로라 진화 전에 먼저 확인 → 맞는 개체만 진화 (진화는 무거움)
            p = EncCond.FitSize(p, ref sizeFree, GenHost.IsLegal);
            if (!EncCond.Satisfied(p)) return null;
            if (o.FlawlessIVs > 0 && new[] { p.IV_HP, p.IV_ATK, p.IV_DEF, p.IV_SPA, p.IV_SPD, p.IV_SPE }.Count(v => v == p.MaxIV) < o.FlawlessIVs) return null;
            if (p.Species != o.Species) { p = EvoUtil.Evolve(p, o.Species, o.Form); if (p.Species != o.Species) return null; }
            if (o.Form >= 0 && p.Form != o.Form && FormInfo.IsFormChangeable(p.Species, p.Form, (byte)o.Form, p.Context, p.Context)) { p.Form = (byte)o.Form; p.ResetPartyStats(); }   // 게임 안에서 바꿀 수 있는 폼 (로토무·트리미앙 등)
            if (o.Form >= 0 && p.Form != o.Form) { fail = e.Form < EncounterUtil.FormDynamic; return null; }   // 다른 폼이 나옴 (폼이 고정된 조우면 더 시도할 필요 없음)
            if (o.AllowTransfer && e.Context != sav.Context) sav.AdaptToSaveFile(p, false);   // 옮겨 온 개체: 지금 세이브 트레이너가 맡은 상태로 (박스에 넣을 때와 같게)
            p.RefreshChecksum();
            if (GenHost.IsLegal(p)) return p;
            if (++illegal > 300) fail = true;   // 이 조우로는 합법이 안 나옴 (볼 불가 등)
            return null;
        }
    }

    /// <summary>
    /// 조우에 쓸 트레이너. 3DS(6·7세대) 비비용 계열은 무늬가 3DS 본체의 나라·지역으로 정해지므로,
    /// 원하는 무늬가 나오는 나라·지역·본체 지역을 골라 줌 (트레이너 언어와 별개).
    /// </summary>
    public static ITrainerInfo TrainerFor(IEncounterInfo e, OrderSpec o, SaveFile sav)
    {
        var tr = GenHost.TrainerOf(sav);
        if (o.Form < 0 || e.Generation is not (6 or 7) || o.Species is not ((int)Species.Vivillon or (int)Species.Spewpa or (int)Species.Scatterbug)) return tr;
        if (o.Species == (int)Species.Vivillon && o.Form > 17) return tr;   // 나비(18)·몬스터볼(19) 무늬는 배포 전용
        var geo = VivillonGeo((byte)Math.Max(0, o.Form));
        if (geo == null) return tr;
        var (cr, c, r) = geo.Value;
        return new SimpleTrainerInfo(e.Version) { OT = tr.OT, TID16 = tr.TID16, SID16 = tr.SID16, Gender = tr.Gender, Language = tr.Language, ConsoleRegion = cr, Country = c, Region = r };
    }

    private static readonly Dictionary<byte, (byte, byte, byte)?> vivGeo = [];
    /// <summary>무늬 → (본체 지역, 나라, 지역). 그 무늬가 나오는 첫 나라·지역.</summary>
    public static (byte ConsoleRegion, byte Country, byte Region)? VivillonGeo(byte form)
    {
        lock (vivGeo)
        {
            if (vivGeo.TryGetValue(form, out var hit)) return hit;
            (byte, byte, byte)? res = null;
            for (int c = 1; c < 256 && res == null; c++)
                for (int r = 0; r < 128 && res == null; r++)
                {
                    if (Vivillon3DS.GetPattern((byte)c, (byte)r) != form || !Vivillon3DS.IsPatternNative(form, (byte)c, (byte)r)) continue;
                    for (byte cr = 0; cr <= 6 && res == null; cr++)
                        if (Vivillon3DS.IsPatternValid(form, cr)) res = (cr, (byte)c, (byte)r);
                }
            return vivGeo[form] = res;
        }
    }

    /// <summary>개체값을 정확히 정할 수 있는 주문이면 가능한 개체값 조합 목록 (6V·개체값 전부 지정: 1개, 5V: 남는 1칸 × 0~30). 아니면 null.</summary>
    private static List<sbyte[]> IvVectors(OrderSpec o, EncounterCriteria c)
    {
        sbyte[] cur = [c.IV_HP, c.IV_ATK, c.IV_DEF, c.IV_SPA, c.IV_SPD, c.IV_SPE];
        if (cur.All(v => v >= 0)) return [cur];
        if (cur.Any(v => v >= 0 && v != 31)) return null;   // 일부만 지정(31 외 값): 기존 방식
        if (o.FlawlessIVs == 6) return [[31, 31, 31, 31, 31, 31]];
        if (o.FlawlessIVs != 5) return null;
        var list = new List<sbyte[]>();
        for (int i = 0; i < 6; i++)
        {
            if (cur[i] == 31) continue;   // V로 지정된 칸은 그대로
            for (sbyte x = 30; x >= 0; x--) { var v = new sbyte[] { 31, 31, 31, 31, 31, 31 }; v[i] = x; list.Add(v); }
        }
        return list.OrderBy(_ => Random.Shared.Next()).ToList();
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
        Extras(o, p);
        GenHost.FixEventDate(p);   // 배포 개체: 만든 방식(조우·자동 합법화)과 관계없이 배포 날짜로
        if (o.MetFrom != null) p = MetDate(o, p);
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
            var t = p.Clone();
            GenHost.AutoOT(t, o.OT ?? me.OT, id32, (byte)(o.OTGender >= 0 ? o.OTGender : me.Gender), me.Language > 0 ? me.Language : p.Language);
            if (GenHost.IsLegal(t) || !GenHost.IsLegal(p)) p = t;
            else o.Notes.Add($"어버이가 정해진 개체(게임 내 교환 등)라 원래 어버이({p.OriginalTrainerName}) 그대로 사용");   // 게임 내 교환 등
        }
        p.RefreshChecksum();
        return p;
    }

    /// <summary>만난 날짜: 정확한 날짜, 또는 구간 안의 날짜 중 합법인 것 (알에서 태어났으면 받은 날짜가 더 늦지 않게).</summary>
    public static PKM MetDate(OrderSpec o, PKM p)
    {
        if (o.MetFrom is not { } from) return p;
        var to = o.MetTo ?? from; if (to < from) (from, to) = (to, from);
        if (p.Format < 4 || p.MetDate == null) { o.Notes.Add("만난 날짜: 이 게임 형식에는 만난 날짜가 없습니다"); return p; }
        int span = to.DayNumber - from.DayNumber;
        var tries = new List<DateOnly>();
        if (span == 0) tries.Add(from);
        else
        {
            var rnd = Random.Shared;
            for (int i = 0; i < 24; i++) tries.Add(from.AddDays(rnd.Next(span + 1)));
            tries.Add(from); tries.Add(to);
        }
        bool legalNow = GenHost.IsLegal(p);
        foreach (var d in tries.Distinct())
        {
            var t = p.Clone(); t.MetDate = d;
            if (t.EggMetDate is { } egg && egg > d) t.EggMetDate = d;
            t.RefreshChecksum();
            if (!legalNow || GenHost.IsLegal(t)) return t;
        }
        o.Notes.Add(span == 0 ? $"만난 날짜 {from:yyyy-MM-dd}: 합법이 아니라 바꾸지 않았습니다 (배포 기간 등 확인)" : $"만난 날짜 {from:yyyy-MM-dd}~{to:yyyy-MM-dd}: 합법인 날짜를 찾지 못해 바꾸지 않았습니다");
        return p;
    }

    /// <summary>거다이맥스·테라스탈 타입·기술: 넣어 보고 합법이 깨지면 되돌리고 알림.</summary>
    private static void Extras(OrderSpec o, PKM p)
    {
        var s = GenHost.Strings;
        bool Try(Action<PKM> set, string what)
        {
            var t = p.Clone(); set(t); t.RefreshChecksum();
            if (GenHost.IsLegal(t) || !GenHost.IsLegal(p)) { set(p); p.RefreshChecksum(); return true; }
            o.Notes.Add($"{what}: 합법이 아니라 넣지 않았습니다"); return false;
        }
        // 리본·증표 여러 개 (대표 증표는 만들 때 이미 붙임): 하나씩 넣어 보고 합법이 깨지면 그것만 뺌
        foreach (var r in o.Ribbons)
        {
            var name = s.Ribbons.GetNameSafe(r, out var kn) ? kn : r.Replace("Ribbon", "");
            var pi = p.GetType().GetProperty(r);
            if (pi == null || pi.PropertyType != typeof(bool) || !pi.CanWrite) { o.Notes.Add($"{name}: 이 게임 형식에는 없습니다"); continue; }
            if ((bool)pi.GetValue(p)!) continue;
            Try(x => x.GetType().GetProperty(r)!.SetValue(x, true), name);
        }
        if (o.Gmax)
        {
            if (p is IGigantamax g0 && !g0.CanGigantamax) Try(x => ((IGigantamax)x).CanGigantamax = true, "거다이맥스");
            else if (p is not IGigantamax) o.Notes.Add("거다이맥스: 이 게임에는 없습니다");
        }
        if (o.HyperTrain)
        {
            if (p is not IHyperTrain) o.Notes.Add("특훈: 이 게임에는 없습니다 (7세대부터)");
            else
            {
                int max = p.MaxIV, min = p.Context is EntityContext.Gen9 or EntityContext.Gen9a ? 50 : 100;
                byte flags = 0;
                for (int i = 0; i < 6; i++) if (p.GetIV(i) < max) flags |= (byte)(1 << i);   // 31인 칸은 특훈 불가(불법) → 이미 '최고'
                if (flags == 0) o.Notes.Add("특훈: 이미 6V라 특훈할 능력치가 없습니다");
                else
                {
                    bool raise = p.CurrentLevel < min;
                    if (Try(x =>
                    {
                        var h = (IHyperTrain)x;
                        h.HT_HP = (flags & 1) != 0; h.HT_ATK = (flags & 2) != 0; h.HT_DEF = (flags & 4) != 0;
                        h.HT_SPE = (flags & 8) != 0; h.HT_SPA = (flags & 16) != 0; h.HT_SPD = (flags & 32) != 0;
                        if (x.CurrentLevel < min) SetLevel(x, min);
                        x.ResetPartyStats();
                    }, "특훈") && raise) o.Notes.Add($"특훈: Lv{min} 이상이어야 해서 Lv{min}으로 올림");
                }
            }
        }
        if (o.Tera >= 0)
        {
            if (p is ITeraType tt0 && (int)tt0.TeraType != o.Tera)
                Try(x => ((ITeraType)x).TeraTypeOverride = (MoveType)o.Tera, "테라스탈 타입");
            else if (p is not ITeraType) o.Notes.Add("테라스탈 타입: 이 게임에는 없습니다");
        }
        if (o.Moves.Count > 0)
        {
            var mv = o.Moves.ToArray();
            var names = string.Join("·", o.Moves.Select(m => s.movelist[m]));
            void Set(PKM x)
            {
                x.SetMoves(mv, true);
                try { x.SetRecordFlags(mv); } catch { }   // 기술머신·기술레코드 기록
                try { x.SetRelearnMoves(new LegalityAnalysis(x)); } catch { }   // 떠올리기 기술 (알 기술 등)
                x.HealPP();
            }
            Try(Set, "기술 " + names);
        }
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
        if (o.Gmax) lines.Add("Gigantamax: Yes");
        if (o.Tera >= 0) lines.Add($"Tera Type: {(o.Tera == 99 ? "Stellar" : en.types[o.Tera])}");
        foreach (var m in o.Moves) lines.Add($"- {en.movelist[m]}");
        return string.Join("\n", lines);
    }


    // 인카운터 검색 조건을 잠시 빌려 쓰고 원래대로 돌려 놓음
    public static Dictionary<FieldInfo, object> SnapshotEncCond() => typeof(EncCond).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => !f.IsInitOnly && !f.IsLiteral).ToDictionary(f => f, f => f.GetValue(null) is int[] a ? a.Clone() : f.GetValue(null));
    public static void RestoreEncCond(Dictionary<FieldInfo, object> s) { foreach (var (f, v) in s) try { f.SetValue(null, v); } catch { } }
}
