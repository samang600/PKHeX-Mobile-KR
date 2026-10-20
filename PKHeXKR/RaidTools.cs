using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// 레이드 편집 (SV 테라 레이드, 소드실드 맥스 레이드 굴). PKHeX 레이드 편집기·Tera-Finder·RaidCrawler 참고:
/// SV는 시드 → 별 개수(진행도) → 조우(가중치) → 개체(EC·PID·개체값)를 PKHeX 조우 표로 그대로 계산해 미리보기.
/// </summary>
public static class RaidTools
{
    // ───── SV ─────
    /// <summary>레이드 미리보기: 별·조우(일반) 또는 이벤트 행·개체·테라 타입·보상.</summary>
    public sealed record Tera(int Stars, EncounterTera9 Enc, PK9 Pk, int TeraType)
    {
        public bool Shiny => Pk.IsShiny;
        public List<RaidData9.Item> Rewards { get; init; } = [];
        public RaidData9.EventRow Ev { get; init; }
        public int Group { get; init; }
        public ushort Species => Pk.Species;
        public byte Form => Pk.Form;
    }

    public static (string Name, TeraRaidMapParent Map, RaidSpawnList9 List)[] Lists(SAV9SV sav)
    {
        var r = new List<(string, TeraRaidMapParent, RaidSpawnList9)>();
        void Add(string n, TeraRaidMapParent m, Func<RaidSpawnList9> f) { try { var l = f(); if (l != null && l.CountAll > 0) r.Add((n, m, l)); } catch { } }
        Add("팔데아", TeraRaidMapParent.Paldea, () => sav.RaidPaldea);
        Add("북신의 고장", TeraRaidMapParent.Kitakami, () => sav.RaidKitakami);
        Add("블루베리 학원", TeraRaidMapParent.Blueberry, () => sav.RaidBlueberry);
        return [.. r];
    }

    private const uint KDiff3 = 0xEC95D8EF, KDiff4 = 0xA9428DFE, KDiff5 = 0x9535F471, KDiff6 = 0x6E7F8220;

    /// <summary>진행도 0~4 (별 3·4·5·6 레이드 해금 수). 별 개수 뽑기 표가 달라짐.</summary>
    public static int Progress(SAV9SV sav)
    {
        bool On(uint k) { try { return sav.Accessor.GetBlockSafe(k).Type == SCTypeCode.Bool2; } catch { return false; } }
        int p = 0;
        foreach (var k in new[] { KDiff3, KDiff4, KDiff5, KDiff6 }) { if (!On(k)) break; p++; }
        return p;
    }

    /// <summary>별 개수 (RaidCrawler 표, PKHeX Tera9RNG 범위와 같음).</summary>
    public static int StarFor(uint rand, int progress) => progress switch
    {
        0 => rand > 80 ? 2 : 1,
        1 => rand > 70 ? 3 : rand > 30 ? 2 : 1,
        2 => rand > 70 ? 4 : rand > 40 ? 3 : rand > 20 ? 2 : 1,
        3 => rand > 75 ? 5 : rand > 40 ? 4 : 3,
        _ => rand > 70 ? 5 : rand > 30 ? 4 : 3,
    };

    /// <summary>시드로 나오는 조우 (개체는 만들지 않음, 빠름).</summary>
    public static (int Stars, EncounterTera9 Enc) Select(GameVersion ver, TeraRaidMapParent map, uint seed, TeraRaidContentType content, int progress)
    {
        if (content is not (TeraRaidContentType.Base05 or TeraRaidContentType.Black6)) return (0, null);
        var rand = new Xoroshiro128Plus(seed);
        int stars = content == TeraRaidContentType.Black6 ? 6 : StarFor((uint)rand.NextInt(100), progress);
        bool sl = ver != GameVersion.VL;
        int total = sl ? EncounterTera9.GetRateTotalSL(stars, map) : EncounterTera9.GetRateTotalVL(stars, map);
        if (total <= 0) return (stars, null);
        int rr = (int)rand.NextInt((uint)total);
        foreach (var e in TeraRaidTables.Get(map))
        {
            if (e.Stars != stars) continue;
            int min = sl ? e.RandRateMinScarlet : e.RandRateMinViolet;
            if (min >= 0 && (uint)(rr - min) < e.RandRate) return (stars, e);
        }
        return (stars, null);
    }

    /// <summary>시드로 나오는 개체 (지금 세이브 트레이너가 잡았을 때).</summary>
    public static Tera Preview(SAV9SV sav, TeraRaidMapParent map, uint seed, TeraRaidContentType content, int progress, bool rewards = true)
    {
        var (stars, e) = Select(sav.Version, map, seed, content, progress);
        if (e == null) return null;
        try
        {
            var pk = e.ConvertToPKM(sav);
            e.GenerateSeed32(pk, seed);
            var tera = Tera9RNG.GetTeraType(seed, e.TeraType, e.Species, e.Form);
            pk.TeraTypeOriginal = (MoveType)tera;
            pk.ResetPartyStats(); pk.RefreshChecksum();
            var list = new List<RaidData9.Item>();
            if (rewards && RaidData9.Find(map, e) is { } row) list = RaidData9.Rewards(seed, e.Species, stars, tera, row.Fixed, row.Lottery);
            return new Tera(stars, e, pk, tera) { Rewards = list };
        }
        catch { return null; }
    }

    /// <summary>이벤트(배포·최강) 레이드 미리보기: 세이브의 이벤트에서 그 굴의 배포 그룹으로 뽑음.</summary>
    public static Tera PreviewEvent(SAV9SV sav, RaidData9.SaveEvent ev, int group, uint seed, TeraRaidContentType content, int progress, bool rewards = true)
    {
        if (ev == null || group <= 0) return null;
        var r = RaidData9.Select(ev, seed, sav.Version, progress, group, content == TeraRaidContentType.Might7);
        if (r == null) return null;
        try
        {
            var pk = RaidData9.Make(r, seed, sav, ev.Identifier);
            int tera = (int)pk.TeraTypeOriginal;
            var list = rewards ? RaidData9.Rewards(seed, r.Species, r.Stars, tera, r.Fixed, r.Lottery, ev.Fixed, ev.Lottery) : [];
            return new Tera(r.Stars, null, pk, tera) { Rewards = list, Ev = r, Group = group };
        }
        catch { return null; }
    }

    /// <summary>레이드 검색 조건 (비워 둔 항목은 상관없음).</summary>
    public sealed class Criteria
    {
        public ushort Species; public int Form = -1; public int Stars;
        public int Shiny;                 // 0 상관없음, 1 이로치, 2 이로치 아님
        public int TeraType = -1, Nature = -1, Gender = -1, Ability = -1;
        public int[] IvMin = new int[6], IvMax = [31, 31, 31, 31, 31, 31];   // H A B C D S
        public int ScaleMin, ScaleMax = 255;
        public int RewardItem, RewardMin = 1;
        public bool Match(Tera t)
        {
            var p = t.Pk;
            if (Species != 0 && (p.Species != Species || (Form >= 0 && p.Form != Form))) return false;
            if (Stars != 0 && t.Stars != Stars) return false;
            if (Shiny == 1 && !p.IsShiny || Shiny == 2 && p.IsShiny) return false;
            if (TeraType >= 0 && t.TeraType != TeraType) return false;
            if (Nature >= 0 && (int)p.Nature != Nature) return false;
            if (Gender >= 0 && p.Gender != Gender) return false;
            if (Ability >= 0 && p.AbilityNumber != (1 << Ability)) return false;
            int[] iv = [p.IV_HP, p.IV_ATK, p.IV_DEF, p.IV_SPA, p.IV_SPD, p.IV_SPE];
            for (int i = 0; i < 6; i++) if (iv[i] < IvMin[i] || iv[i] > IvMax[i]) return false;
            if (p.Scale < ScaleMin || p.Scale > ScaleMax) return false;
            if (RewardItem > 0 && RaidData9.HostSummary(t.Rewards).Where(x => x.Id == RewardItem).Sum(x => x.Amount) < RewardMin) return false;
            return true;
        }
        /// <summary>개체를 만들기 전에 거를 수 있는 것 (종·폼·별).</summary>
        public bool Quick(ushort species, byte form, int stars) => (Species == 0 || species == Species && (Form < 0 || form == Form)) && (Stars == 0 || stars == Stars);
    }

    /// <summary>
    /// 무작위 시드에서 조건에 맞는 레이드 찾기 (여러 스레드). quick: 시드 → (종, 폼, 별) 또는 null (빠른 거르기), make: 시드 → 미리보기.
    /// 찾은 수가 want에 이르거나 시간이 다 되거나 취소되면 끝. progress(시도한 시드 수, 찾은 수).
    /// </summary>
    public static List<(uint Seed, Tera T)> Search(Func<uint, (ushort, byte, int)?> quick, Func<uint, Tera> make, Criteria c, int want, TimeSpan limit, CancellationToken tok, Action<long, int> progress = null, bool seedShiny = true)
    {
        var found = new List<(uint, Tera)>(); long tried = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int threads = Math.Max(1, Environment.ProcessorCount - 1);
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, _ =>
        {
            var rnd = new Random();
            long local = 0;
            while (!tok.IsCancellationRequested && sw.Elapsed < limit)
            {
                lock (found) if (found.Count >= want) break;
                uint seed = (uint)rnd.NextInt64(0, 1L << 32);
                if (++local % 4096 == 0) { Interlocked.Add(ref tried, 4096); int fc; lock (found) fc = found.Count; progress?.Invoke(Interlocked.Read(ref tried), fc); }
                if (seedShiny && c.Shiny == 1 && !ShinySeed9(seed)) continue;   // 시드로 정해지는 이로치 (이로치 고정 이벤트는 제외)
                var q = quick(seed);
                if (q == null || !c.Quick(q.Value.Item1, q.Value.Item2, q.Value.Item3)) continue;
                var t = make(seed);
                if (t == null || !c.Match(t)) continue;
                lock (found) if (found.Count < want && !found.Any(f => f.Item1 == seed)) found.Add((seed, t));
            }
        });
        progress?.Invoke(Interlocked.Read(ref tried), found.Count);
        return found;
    }

    /// <summary>시드로 이로치인지 (가짜 ID로 정해짐, 트레이너와 무관).</summary>
    public static bool ShinySeed9(uint seed)
    {
        var rand = new Xoroshiro128Plus(seed);
        rand.NextInt(); var fake = (uint)rand.NextInt(); var pid = (uint)rand.NextInt();
        return Xor(pid, fake) < 16;
    }

    /// <summary>원하는 조우(종·폼·별)가 나오는 시드 찾기. 이로치만 원하면 shiny.</summary>
    public static uint? Find9(GameVersion ver, TeraRaidMapParent map, TeraRaidContentType content, int progress, EncounterTera9 want, bool shiny, CancellationToken tok, int max = 30_000_000)
    {
        if (shiny && want != null && want.Shiny == PKHeX.Core.Shiny.Never) return null;
        var rnd = Random.Shared;
        for (int i = 0; i < max && !tok.IsCancellationRequested; i++)
        {
            uint seed = (uint)rnd.NextInt64(0, 1L << 32);
            if (shiny && !ShinySeed9(seed)) continue;
            var (_, e) = Select(ver, map, seed, content, progress);
            if (e == null) continue;
            if (want != null && (e.Species != want.Species || e.Form != want.Form || e.Stars != want.Stars)) continue;
            return seed;
        }
        return null;
    }

    /// <summary>이 지역·별 개수에서 이 게임(스칼렛·바이올렛)으로 나올 수 있는 조우 목록.</summary>
    public static List<EncounterTera9> Possible(GameVersion ver, TeraRaidMapParent map, int stars)
    {
        bool sl = ver != GameVersion.VL;
        return TeraRaidTables.Get(map).Where(e => e.Stars == stars && (sl ? e.IsAvailableHostScarlet : e.IsAvailableHostViolet))
            .GroupBy(e => (e.Species, e.Form)).Select(g => g.First()).ToList();
    }

    public static string ContentName(TeraRaidContentType c) => c switch
    {
        TeraRaidContentType.Base05 => "일반 (1~5★)", TeraRaidContentType.Black6 => "검은 결정 (6★)",
        TeraRaidContentType.Distribution => "이벤트 (배포)", TeraRaidContentType.Might7 => "최강 (7★)", _ => c.ToString(),
    };

    // ───── 소드실드 ─────
    public static (string Name, RaidSpawnList8 List)[] Lists(SAV8SWSH sav)
    {
        var r = new List<(string, RaidSpawnList8)>();
        void Add(string n, Func<RaidSpawnList8> f) { try { var l = f(); if (l != null && l.CountAll > 0) r.Add((n, l)); } catch { } }
        Add("가라르", () => sav.RaidGalar);
        Add("갑옷의 외딴섬", () => sav.RaidArmor);
        Add("왕관의 설원", () => sav.RaidCrown);
        return [.. r];
    }

    /// <summary>소드실드 굴 시드의 이로치: 0 아님, 1 별, 2 네모.</summary>
    public static int Shiny8(ulong seed)
    {
        var rng = new Xoroshiro128Plus(seed);
        rng.NextInt(); var tr = (uint)rng.NextInt(); var pid = (uint)rng.NextInt();
        var x = Xor(pid, tr);
        return x == 0 ? 2 : x < 16 ? 1 : 0;
    }

    /// <summary>이로치 시드 찾기 (square: 네모만).</summary>
    public static ulong? Find8(bool square, CancellationToken tok, int max = 50_000_000)
    {
        var rnd = Random.Shared;
        for (int i = 0; i < max && !tok.IsCancellationRequested; i++)
        {
            var seed = (ulong)rnd.NextInt64() ^ ((ulong)rnd.Next() << 63);
            var s = Shiny8(seed);
            if (s == 2 || (s == 1 && !square)) return seed;
        }
        return null;
    }

    public static string DenName(RaidType t) => t switch
    {
        RaidType.None => "비활성", RaidType.Common => "일반", RaidType.Rare => "희귀(빛기둥 보라)", RaidType.CommonWish => "일반 · 소원조각",
        RaidType.RareWish => "희귀 · 소원조각", RaidType.Event => "이벤트", RaidType.DynamaxCrystal => "다이맥스 결정", _ => t.ToString(),
    };

    /// <summary>소드실드 이벤트 굴(맥스 레이드 이벤트) BCAT 파일 → 세이브 블록 [PKHeX SaveBlockAccessor8SWSH, EventsGallery 'Wild Area Events/Index NNN'].</summary>
    public static readonly (string File, uint Key)[] Files8 =
    [
        ("normal_encount", 0xAD9DFA6A), ("normal_encount_rigel1", 0x0E615A8C), ("normal_encount_rigel2", 0x11615F45),
        ("drop_rewards", 0x680EEB85), ("bonus_rewards", 0xEFCAE04E), ("dai_encount", 0xAD3920F5),
    ];

    /// <summary>이벤트 파일을 소드실드 세이브 블록에 씀 (블록보다 큰 파일은 거절, 작으면 뒤를 0으로). 반환: 쓴 블록 이름들.</summary>
    public static List<string> Import8(SAV8SWSH sav, IReadOnlyDictionary<string, byte[]> files, List<string> errors)
    {
        var done = new List<string>();
        foreach (var (name, key) in Files8)
        {
            if (!files.TryGetValue(name, out var data)) continue;
            var b = sav.Accessor.GetBlockSafe(key);
            if (b.Type == SCTypeCode.None) { errors.Add($"세이브에 {name} 블록이 없음"); continue; }
            if (data.Length > b.Data.Length) { errors.Add($"{name}: 파일이 블록보다 큼 ({data.Length} > {b.Data.Length})"); continue; }
            var buf = new byte[b.Data.Length]; data.CopyTo(buf, 0); b.ChangeData(buf); done.Add(name);
        }
        return done;
    }

    private static uint Xor(uint pid, uint id) => (pid >> 16) ^ (pid & 0xFFFF) ^ (id >> 16) ^ (id & 0xFFFF);
}
