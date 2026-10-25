using PKHeX.Core;
using PKHeX.Core.AutoMod;
namespace PKHeXKR;

/// <summary>
/// 스트레인지볼(볼 번호 27)이 가능한 포켓몬 목록.
/// PKHeX 기준으로 진짜 스트레인지볼은 포켓몬 GO 와일드 에어리어 이벤트에서 사파리볼로 잡아 HOME으로 보낸 경우뿐:
/// GO 조우 목록 중 (1) 와일드 에어리어 기간에 나왔고 (2) 사파리볼을 쓸 수 있는 조우(스페셜 리서치 전설·환상 제외)의 종 + 그 진화형.
/// 실제 판정은 PKHeX의 GO 조우 검사(IsBallValid·날짜 범위)를 그대로 불러서 맞춤.
/// </summary>
public static class StrangeBall
{
    public const int BallId = (int)Ball.Strange;

    /// <summary>와일드 에어리어 기간 (PKHeX EncounterSlot8GO.IsValidWildAreaDate와 같음)</summary>
    public static readonly (DateOnly From, DateOnly To, string Name)[] Windows =
    [
        (new(2024, 11, 15), new(2024, 11, 18), "2024 후쿠오카"),
        (new(2024, 11, 22), new(2024, 11, 25), "2024 글로벌"),
        (new(2025, 11, 6), new(2025, 11, 10), "2025 나가사키"),
        (new(2025, 11, 14), new(2025, 11, 17), "2025 글로벌"),
        (new(2026, 11, 5), new(2026, 11, 9), "2026 센다이·멕시코시티"),
        (new(2026, 11, 13), new(2026, 11, 16), "2026 글로벌"),
    ];

    public sealed record Entry(ushort Species, byte Form, string Events, bool Evolved);

    private static readonly Dictionary<EntityContext, List<Entry>> cache = new();

    /// <summary>이 세이브 게임에 있는 종만, 잡은 종 + 진화형. 이미 지난(또는 오늘까지의) 기간만.</summary>
    public static List<Entry> Possible(SaveFile sav)
    {
        lock (cache)
        {
            if (cache.TryGetValue(sav.Context, out var c)) return c;
            var res = Build(sav);
            cache[sav.Context] = res;
            return res;
        }
    }

    public static bool IsPossible(SaveFile sav, ushort species) => Possible(sav).Any(e => e.Species == species);

    private static IEnumerable<EncounterSlot8GO> GoSlots()
    {
        var t = typeof(EncounterArea8g).Assembly.GetType("PKHeX.Core.EncountersGO");
        var f = t?.GetField("SlotsGO", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (f?.GetValue(null) is not EncounterArea8g[] areas) yield break;
        foreach (var a in areas) foreach (var s in a.Slots) yield return s;
    }

    /// <summary>스트레인지볼로 잡을 수 있는 GO 조우와 그 날짜들 (오늘까지 열린 와일드 에어리어 기간 중 그 조우가 있던 날)</summary>
    private static List<(EncounterSlot8GO Slot, List<DateOnly> Days, string Events)> slotCache;
    private static List<(EncounterSlot8GO Slot, List<DateOnly> Days, string Events)> StrangeSlots(SaveFile sav)
    {
        if (slotCache != null) return slotCache;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var pk = sav.BlankPKM; var list = new List<(EncounterSlot8GO, List<DateOnly>, string)>();
        foreach (var s in GoSlots())
        {
            var days = new List<DateOnly>(); var evs = new SortedSet<string>();
            foreach (var w in Windows)
            {
                if (w.From > today) continue;
                for (var d = w.From; d <= w.To && d <= today; d = d.AddDays(1))
                {
                    if (!s.IsWithinStartEnd(d)) continue;
                    try { pk.MetDate = d; } catch { }
                    bool ok;
                    try { ok = s.IsBallValid(Ball.Strange, s.Species, pk); } catch { ok = false; }
                    if (ok) { days.Add(d); evs.Add(w.Name); }
                }
            }
            if (days.Count > 0) list.Add((s, days, string.Join("·", evs)));
        }
        return slotCache = list;
    }

    /// <summary>
    /// 스트레인지볼 주문 만들기: GO 와일드 에어리어 조우(이 종 또는 진화 전)를 HOME 형식으로 바꿔 이 게임 형식으로 → 볼·만난 날짜·HOME 기록·
    /// 받은 트레이너 정리 → 주문한 종으로 진화 → 나머지 조건(QuickGen.Finish). 합법이 될 때까지 날짜·조우를 바꿔 시도.
    /// </summary>
    public static (PKM Pk, string How) Make(OrderSpec o, SaveFile sav, CancellationToken tok)
    {
        if (!IsPossible(sav, o.Species))
            return (null, "스트레인지볼은 이 포켓몬에 불가능해요 (포켓몬 GO 와일드 에어리어에서 사파리볼로 잡은 포켓몬만 · '스트레인지볼 가능한 포켓몬' 목록 참고)");
        var line = new HashSet<ushort> { o.Species };
        try
        {
            var tree = EvolutionTree.GetEvolutionTree(sav.Context);
            foreach (var f in o.Form >= 0 ? new[] { (byte)o.Form } : Enumerable.Range(0, 8).Select(x => (byte)x))
                try { foreach (var x in tree.Reverse.GetPreEvolutions(o.Species, f)) line.Add(x.Species); } catch { }
        }
        catch { }
        var shiny = o.Shiny switch { 1 or 3 or 4 => Shiny.Always, 2 => Shiny.Never, _ => Shiny.Random };
        var me = GenHost.TrainerOf(sav);
        var rnd = Random.Shared;
        var srcs = StrangeSlots(sav).Where(x => line.Contains(x.Slot.Species)).OrderBy(x => x.Slot.Species == o.Species ? 0 : 1).ToList();
        foreach (var (slot, days, evs) in srcs)
        {
            if (shiny == Shiny.Always && slot.Shiny == Shiny.Never) continue;
            // 주문한 날짜가 있으면 그 범위 안의 날짜만
            var useDays = days.Where(d => o.MetFrom is not { } a || (d >= a && d <= (o.MetTo ?? a))).ToList();
            if (useDays.Count == 0) continue;
            for (int tr = 0; tr < 6 && !tok.IsCancellationRequested; tr++)
            {
                try
                {
                    var crit = EncounterCriteria.Unrestricted with { Shiny = shiny, Gender = o.Gender >= 0 ? (Gender)o.Gender : Gender.Random };
                    var raw = slot.ConvertToPKM(sav, crit);
                    if (raw == null) continue;
                    var pk = raw.GetType() == sav.PKMType ? raw : EntityConverter.ConvertToType(raw, sav.PKMType, out _);
                    if (pk == null) continue;
                    pk.Ball = (byte)Ball.Strange;
                    pk.MetDate = useDays[rnd.Next(useDays.Count)];
                    if (pk is IHomeTrack ht && ht.Tracker == 0) ht.Tracker = (ulong)rnd.NextInt64(1, long.MaxValue);
                    pk.CurrentHandler = 1; pk.HandlingTrainerName = me.OT; pk.HandlingTrainerGender = me.Gender; pk.SetHTLanguage((byte)me.Language);
                    pk.SetSuggestedMemories();
                    pk.RefreshChecksum();
                    var p = EvoUtil.ToTarget(pk, o.Species, o.Form);
                    if (p == null || p.Species != o.Species) continue;
                    var keepDate = o.MetFrom; o.MetFrom = null;   // 날짜는 위에서 이벤트 기간으로 정함
                    try { p = QuickGen.Finish(o, p, sav); } finally { o.MetFrom = keepDate; }
                    p.Ball = (byte)Ball.Strange; p.RefreshChecksum();
                    if (GenHost.IsLegal(p))
                        return (p, $"포켓몬 GO 와일드 에어리어({evs}) 사파리볼 → 스트레인지볼 · 만난 날짜 {p.MetDate:yyyy-MM-dd}");
                }
                catch { }
            }
        }
        return (null, "스트레인지볼로 합법인 개체를 만들지 못했어요 (GO 출신 조건: 이로치·성별·레벨 조합 확인)");
    }

    private static List<Entry> Build(SaveFile sav)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var caught = new Dictionary<(ushort, byte), SortedSet<string>>();
        var pk = sav.BlankPKM;
        foreach (var s in GoSlots())
        {
            foreach (var w in Windows)
            {
                if (w.From > today) continue;   // 아직 안 열린 이벤트
                for (var d = w.From; d <= w.To && d <= today; d = d.AddDays(1))
                {
                    if (!s.IsWithinStartEnd(d)) continue;
                    try { pk.MetDate = d; } catch { }
                    bool ok;
                    try { ok = s.IsBallValid(Ball.Strange, s.Species, pk); } catch { ok = false; }
                    if (!ok) continue;
                    var key = (s.Species, s.Form);
                    if (!caught.TryGetValue(key, out var set)) caught[key] = set = new SortedSet<string>();
                    set.Add(w.Name);
                    break;
                }
            }
        }

        // 잡은 종 + 진화형 (이 게임 진화 규칙), 이 게임에 있는 종만
        var result = new Dictionary<(ushort, byte), Entry>();
        EvolutionTree tree = null;
        try { tree = EvolutionTree.GetEvolutionTree(sav.Context); } catch { }
        void Add(ushort sp, byte f, string ev, bool evolved, int depth)
        {
            if (depth > 3) return;
            if (sav.Personal.IsPresentInGame(sp, f) && !result.ContainsKey((sp, f)))
                result[(sp, f)] = new Entry(sp, f, ev, evolved);
            if (tree == null) return;
            ReadOnlyMemory<EvolutionMethod> next;
            try { next = tree.Forward.GetForward(sp, f); } catch { return; }
            foreach (var m in next.ToArray()) Add(m.Species, m.GetDestinationForm(f), ev, true, depth + 1);
        }
        foreach (var (k, ev) in caught) Add(k.Item1, k.Item2, string.Join("·", ev), false, 0);
        return result.Values.OrderBy(e => e.Species).ThenBy(e => e.Form).ToList();
    }
}
