using System.Buffers.Binary;
using System.Text.Json;
using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// SV 테라 레이드 보상·이벤트(배포) 레이드.
/// 보상 표: PokeLDN(pokeldn/sv/data/raid_base.json, Tera-Finder 표·스칼렛 4.0.0 RomFS, GPL-3.0)을 줄인 raid9.json.
/// 보상 뽑기: 시드로 xoroshiro → 100면 굴림으로 개수, 그다음 가중치 뽑기 [Tera-Finder RewardUtil, PokeLDN raid_encounter.reward_rows].
/// 이벤트: 세이브의 BCAT 블록(raid_enemy_array 등 FlatBuffers)을 읽어 배포 그룹·진행도로 조우를 고름 [PKHeX EncounterDist9, PokeLDN raid_event].
/// </summary>
public static class RaidData9
{
    public sealed record Row(ushort Species, byte Form, int Stars, int Rate, int MinS, int MinV, ulong Fixed, ulong Lottery, int Gem);
    public sealed record Fix(int Category, int Item, int Amount, int Subject);
    public sealed record Lot(int Category, int Item, int Amount, int Prob, bool Rare);
    /// <summary>받는 보상 한 줄. Subject: 0 모두, 1 호스트, 2 손님, 3 한 번만, 4 레이드 파워 추가, 5 빛나는 부적.</summary>
    public sealed record Item(int Id, int Amount, int Subject, bool Rare);

    private static Dictionary<string, Row[]> enc;
    private static Dictionary<ulong, Fix[]> fixedT;
    private static Dictionary<ulong, Lot[]> lotteryT;
    private static Dictionary<int, int> material;
    private static readonly object gate = new();

    private static void Load()
    {
        if (enc != null) return;
        lock (gate)
        {
            if (enc != null) return;
            using var st = typeof(RaidData9).Assembly.GetManifestResourceStream("raid9.json");
            using var doc = JsonDocument.Parse(st);
            var r = doc.RootElement;
            var e = new Dictionary<string, Row[]>();
            foreach (var kv in r.GetProperty("enc").EnumerateObject())
                e[kv.Name] = kv.Value.EnumerateArray().Select(a => new Row((ushort)a[0].GetInt32(), (byte)a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32(), a[4].GetInt32(), a[5].GetInt32(),
                    ulong.Parse(a[6].GetString()), ulong.Parse(a[7].GetString()), a[8].GetInt32())).ToArray();
            var f = new Dictionary<ulong, Fix[]>();
            foreach (var kv in r.GetProperty("fixed").EnumerateObject())
                f[ulong.Parse(kv.Name)] = kv.Value.EnumerateArray().Select(a => new Fix(a[0].GetInt32(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32())).ToArray();
            var l = new Dictionary<ulong, Lot[]>();
            foreach (var kv in r.GetProperty("lottery").EnumerateObject())
                l[ulong.Parse(kv.Name)] = kv.Value.EnumerateArray().Select(a => new Lot(a[0].GetInt32(), a[1].GetInt32(), a[2].GetInt32(), a[3].GetInt32(), a[4].GetInt32() != 0)).ToArray();
            var m = new Dictionary<int, int>();
            foreach (var kv in r.GetProperty("material").EnumerateObject()) m[int.Parse(kv.Name)] = kv.Value.GetInt32();
            fixedT = f; lotteryT = l; material = m; enc = e;
        }
    }

    private static string MapKey(TeraRaidMapParent map) => map switch { TeraRaidMapParent.Kitakami => "kitakami", TeraRaidMapParent.Blueberry => "blueberry", _ => "paldea" };

    /// <summary>PKHeX 조우 → 보상 표가 붙은 행 (같은 별·종·폼·가중치 구간).</summary>
    public static Row Find(TeraRaidMapParent map, EncounterTera9 e)
    {
        Load();
        if (!enc.TryGetValue(MapKey(map) + (e.Stars == 6 ? "_black" : "_standard"), out var rows)) return null;
        return rows.FirstOrDefault(r => r.Species == e.Species && r.Form == e.Form && r.Stars == e.Stars && r.MinS == e.RandRateMinScarlet && r.MinV == e.RandRateMinViolet)
            ?? rows.FirstOrDefault(r => r.Species == e.Species && r.Form == e.Form && r.Stars == e.Stars);
    }

    private static readonly int[][] Slots = [[4, 5, 6, 7, 8], [4, 5, 6, 7, 8], [5, 6, 7, 8, 9], [5, 6, 7, 8, 9], [6, 7, 8, 9, 10], [7, 8, 9, 10, 11], [7, 8, 9, 10, 11]];
    private static readonly int[] Columns = [10, 40, 70, 90];
    /// <summary>테라 타입별 테라피스.</summary>
    public static readonly int[] Shards = [1862, 1868, 1871, 1869, 1870, 1874, 1873, 1875, 1878, 1863, 1864, 1866, 1865, 1872, 1867, 1876, 1877, 1879];
    private static readonly int[] ShardBoost = [0, 0, 2, 5, 10, 12, 0];
    private const int PowerDraws = 3;

    /// <summary>시드의 보상 (고정 보상 + 뽑기 + 레이드 파워 추가분 + 빛나는 부적 테라피스). 이벤트는 그 이벤트의 표.</summary>
    public static List<Item> Rewards(uint seed, ushort species, int stars, int tera, ulong fixedId, ulong lotteryId, Dictionary<ulong, Fix[]> fx = null, Dictionary<ulong, Lot[]> lt = null)
    {
        Load();
        fx ??= fixedT; lt ??= lotteryT;
        var outp = new List<Item>();
        int Resolve(int category, int item) => item != 0 ? item : category switch
        {
            1 => material.TryGetValue(species, out var m) ? m : 0,
            2 => tera is >= 0 and < 18 ? Shards[tera] : 0,
            _ => 0,
        };
        if (fx.TryGetValue(fixedId, out var fixes))
            foreach (var f in fixes) { var id = Resolve(f.Category, f.Item); if (id != 0 && f.Amount != 0) outp.Add(new Item(id, f.Amount, f.Subject, false)); }
        if (lt.TryGetValue(lotteryId, out var draws))
        {
            int total = draws.Sum(d => d.Prob);
            if (total > 0)
            {
                var rand = new Xoroshiro128Plus(seed);
                int roll = (int)rand.NextInt(100);
                int col = Columns.Count(edge => roll >= edge);
                int count = Slots[Math.Clamp(stars, 1, 7) - 1][col];
                for (int k = 0; k < count + PowerDraws; k++)
                {
                    int th = (int)rand.NextInt((ulong)total);
                    foreach (var d in draws)
                    {
                        if (d.Prob > th)
                        {
                            var id = Resolve(d.Category, d.Item);
                            if (id != 0 && d.Amount != 0) outp.Add(new Item(id, d.Amount, k < count ? 0 : 4, d.Rare));
                            break;
                        }
                        th -= d.Prob;
                    }
                }
            }
        }
        if (ShardBoost[Math.Clamp(stars, 1, 7) - 1] > 0 && tera is >= 0 and < 18) outp.Add(new Item(Shards[tera], ShardBoost[Math.Clamp(stars, 1, 7) - 1], 5, false));
        return outp;
    }

    /// <summary>호스트가 받는 보상만 묶어서 (같은 아이템 합침): 고정(모두·호스트·한 번만) + 뽑기.</summary>
    public static List<(int Id, int Amount)> HostSummary(IEnumerable<Item> items)
        => items.Where(i => i.Subject is 0 or 1 or 3).GroupBy(i => i.Id).Select(g => (g.Key, g.Sum(x => x.Amount))).ToList();

    public static string Text(IEnumerable<Item> items, int max = 99)
    {
        var s = GameInfo.Strings.itemlist;
        var list = HostSummary(items).Select(x => $"{(x.Id < s.Length ? s[x.Id] : x.Id.ToString())}×{x.Amount}").ToList();
        return list.Count == 0 ? "" : string.Join(", ", list.Take(max)) + (list.Count > max ? " …" : "");
    }

    /// <summary>모든 보상 표에 나오는 아이템 (검색 고르기용).</summary>
    public static List<int> AllItems(SaveEvent ev = null)
    {
        Load();
        var set = new HashSet<int>();
        foreach (var f in fixedT.Values.SelectMany(x => x)) if (f.Item != 0) set.Add(f.Item);
        foreach (var l in lotteryT.Values.SelectMany(x => x)) if (l.Item != 0) set.Add(l.Item);
        foreach (var m in material.Values) set.Add(m);
        foreach (var s in Shards) set.Add(s);
        if (ev != null) { foreach (var f in ev.Fixed.Values.SelectMany(x => x)) if (f.Item != 0) set.Add(f.Item); foreach (var l in ev.Lottery.Values.SelectMany(x => x)) if (l.Item != 0) set.Add(l.Item); }
        return [.. set];
    }

    // ───── 이벤트 (배포) 레이드 ─────
    public const uint KEnemy = 0x0520A1B0, KFixed = 0x7D6C2B82, KLottery = 0xA52B4811, KPriority = 0x095451E4, KIdentifier = 0x37B99B4D;
    public static readonly (string File, uint Key)[] Files = [("raid_enemy_array", KEnemy), ("fixed_reward_item_array", KFixed), ("lottery_reward_item_array", KLottery), ("raid_priority_array", KPriority), ("event_raid_identifier", KIdentifier)];
    /// <summary>1.3.0부터 배포 파일은 패치별로 다시 들어 있음: 가장 새 것 [pkNX TeraRaidRipper].</summary>
    public static readonly string[] Patches = ["_3_0_0", "_2_0_0", "_1_3_0", ""];

    public sealed record EventRow(ushort Species, byte Form, int Level, int Stars, int Rate, int Rom, int Group, int CaptureRate, ulong Fixed, ulong Lottery,
        int Gem, int Gender, int Nature, int Ability, int Flawless, sbyte[] IVs, int Shiny, int ScaleType, int Scale, int Item, ushort[] Moves, int No);

    public sealed class SaveEvent
    {
        public int Identifier;
        public List<EventRow> Rows = [];
        public Dictionary<ulong, Fix[]> Fixed = [];
        public Dictionary<ulong, Lot[]> Lottery = [];
        public int[] Dens = new int[10];   // 배포 그룹 1~10의 굴 수
        public bool Any => Rows.Count > 0;
    }

    /// <summary>세이브에 받아 둔 이벤트 레이드 (포켓포털 뉴스). 없거나 못 읽으면 null.</summary>
    public static SaveEvent ReadSave(SAV9SV sav)
    {
        try
        {
            byte[] Get(uint k) { var b = sav.Accessor.GetBlockSafe(k); return b.Type == SCTypeCode.None ? null : b.Data.ToArray(); }
            var enemy = Get(KEnemy); var fix = Get(KFixed); var lot = Get(KLottery); var pri = Get(KPriority);
            if (enemy == null || pri == null) return null;
            return Parse(enemy, fix, lot, pri);
        }
        catch { return null; }
    }

    public static SaveEvent Parse(byte[] enemy, byte[] fix, byte[] lot, byte[] pri)
    {
        var ev = new SaveEvent();
        var p = Fb.Root(pri).Vector(0);
        if (p.Count > 0)
        {
            ev.Identifier = p[0].I32(0);
            var g = p[0].Table(1);
            if (g != null) for (int i = 0; i < 10; i++) ev.Dens[i] = g.I8(i);
        }
        foreach (var t in Fb.Root(enemy).Vector(0))
        {
            var info = t.Table(0); if (info == null) continue;
            var para = info.Table(9); if (para == null) continue;
            var dev = para.U16(0); if (dev == 0) continue;
            int talent = para.I32(14);
            sbyte[] ivs = null;
            if (talent == 2 && para.Table(15) is { } tv) ivs = [(sbyte)tv.I32(0), (sbyte)tv.I32(1), (sbyte)tv.I32(2), (sbyte)tv.I32(3), (sbyte)tv.I32(4), (sbyte)tv.I32(5)];   // HP ATK DEF SPA SPD SPE
            var moves = new ushort[4];
            for (int i = 0; i < 4; i++) moves[i] = para.Table(7 + i)?.U16(0) ?? 0;
            int sex = para.I32(2), seikaku = para.I32(12);
            ev.Rows.Add(new EventRow(SpeciesConverter.GetNational9(dev), (byte)para.I16(1), para.I32(4), info.I32(3), info.I8(4), info.I16(0), info.I8(2), info.I8(7),
                info.U64(5), info.U64(6), para.I32(11), sex == 0 ? -1 : sex - 1, seikaku == 0 ? -1 : seikaku - 1, para.I32(13), talent == 1 ? para.I8(16) : 0, ivs,
                para.I32(18), para.I32(19), para.I16(20), para.I32(3), moves, info.I32(1)));
        }
        if (fix != null)
            foreach (var t in Fb.Root(fix).Vector(0))
            {
                var name = t.U64(0); var list = new List<Fix>();
                for (int i = 0; i < 15; i++) if (t.Table(1 + i) is { } it) { var f = new Fix(it.I32(0), it.I32(2), it.I8(3), it.I32(1)); if (f.Item != 0 || f.Category != 0) list.Add(f); }
                ev.Fixed.TryAdd(name, [.. list]);
            }
        if (lot != null)
            foreach (var t in Fb.Root(lot).Vector(0))
            {
                var name = t.U64(0); var list = new List<Lot>();
                for (int i = 0; i < 30; i++) if (t.Table(1 + i) is { } it) { var l = new Lot(it.I32(0), it.I32(1), it.I8(2), it.I32(3), it.Bool(4)); if (l.Item != 0 || l.Category != 0 || l.Prob != 0) list.Add(l); }
                ev.Lottery.TryAdd(name, [.. list]);
            }
        return ev;
    }

    /// <summary>진행도(0~4) → 그 단계에서 나오는 별 [pkNX TeraRaidRipper.StageStars].</summary>
    private static int[] StageStars(int progress) => progress switch { 0 => [1, 2], 1 => [1, 2, 3], 2 => [1, 2, 3, 4], _ => [3, 4, 5, 6, 7] };

    private static IEnumerable<EventRow> Candidates(SaveEvent ev, GameVersion ver, int progress, int group, bool might)
    {
        int rom = ver == GameVersion.VL ? 2 : 1;
        var stars = StageStars(progress);
        return ev.Rows.Where(r => r.Group == group && r.Rate > 0 && (r.Rom == 0 || r.Rom == rom) && stars.Contains(r.Stars) && (r.Stars == 7) == might);
    }

    /// <summary>
    /// 이 굴의 배포 그룹: 같은 목록에서 배포·최강 굴을 앞에서부터 세고, 그룹별 굴 수(raid_priority_array)를 차례로 빼서 정함
    /// [Tera-Finder EventUtil.GetDeliveryGroupID]. 그 진행도·버전에서 조우가 없는 그룹은 건너뜀.
    /// </summary>
    public static int GroupOf(SaveEvent ev, RaidSpawnList9 list, int index, GameVersion ver, int progress)
    {
        var r0 = list.GetRaid(index); bool might = r0.Content == TeraRaidContentType.Might7;
        int count = 0;
        for (int i = 0; i <= index; i++) { var c = list.GetRaid(i).Content; if (c is TeraRaidContentType.Distribution or TeraRaidContentType.Might7) count++; }
        for (int g = 1; g <= 10; g++)
        {
            int dens = ev.Dens[g - 1];
            if (dens <= 0 || !Candidates(ev, ver, progress, g, might).Any()) continue;
            if (count <= dens) return g;
            count -= dens;
        }
        for (int g = 1; g <= 10; g++) if (Candidates(ev, ver, progress, g, might).Any()) return g;   // 그룹을 못 정하면 조우가 있는 첫 그룹
        return 0;
    }

    /// <summary>시드 → 이 그룹의 조우: 별 굴림(버림) 다음, 가중치 합 안에서 뽑기 [PKHeX EncounterDist9.GetIsPossibleSlot].</summary>
    public static EventRow Select(SaveEvent ev, uint seed, GameVersion ver, int progress, int group, bool might)
    {
        var rows = Candidates(ev, ver, progress, group, might).ToList();
        int total = rows.Sum(r => r.Rate);
        if (total <= 0) return null;
        var rand = new Xoroshiro128Plus(seed);
        rand.NextInt(100);
        int choice = (int)rand.NextInt((ulong)total);
        foreach (var r in rows) { if (choice < r.Rate) return r; choice -= r.Rate; }
        return null;
    }

    /// <summary>이벤트 조우의 개체 (PKHeX Encounter9RNG에 이벤트가 정한 값을 넣어 만듦).</summary>
    public static PK9 Make(EventRow r, uint seed, ITrainerInfo tr, int identifier = 0)
    {
        var pi = PersonalTable.SV.GetFormEntry(r.Species, r.Form);
        byte ratio = r.Gender switch { 0 => PersonalInfo.RatioMagicMale, 1 => PersonalInfo.RatioMagicFemale, _ => pi.Gender };
        var ab = r.Ability switch { 1 => AbilityPermission.Any12H, 2 => AbilityPermission.OnlyFirst, 3 => AbilityPermission.OnlySecond, 4 => AbilityPermission.OnlyHidden, _ => AbilityPermission.Any12 };
        var shiny = r.Shiny switch { 1 => Shiny.Never, 2 => Shiny.Always, _ => Shiny.Random };
        var ivs = r.IVs == null ? default : new IndividualValueSet(r.IVs[0], r.IVs[1], r.IVs[2], r.IVs[5], r.IVs[3], r.IVs[4]);
        var param = new GenerateParam9(r.Species, ratio, (byte)r.Flawless, 1, 0, 0, (SizeType9)Math.Clamp(r.ScaleType, 0, 6), (byte)r.Scale, ab, shiny,
            r.Nature >= 0 ? (Nature)r.Nature : Nature.Random, ivs);
        var pk = new PK9
        {
            Species = r.Species, Form = r.Form, CurrentLevel = (byte)Math.Clamp(r.Level, 1, 100), MetLevel = (byte)Math.Clamp(r.Level, 1, 100),
            OriginalTrainerName = tr.OT, ID32 = tr.ID32, OriginalTrainerGender = tr.Gender, Language = tr.Language > 0 ? tr.Language : 2,
            Version = tr.Version is GameVersion.SL or GameVersion.VL ? tr.Version : GameVersion.SL, MetLocation = Locations.TeraCavern9, Ball = (byte)Ball.Poke,
            ObedienceLevel = (byte)Math.Clamp(r.Level, 1, 100), OriginalTrainerFriendship = pi.BaseFriendship,
        };
        // 만난 날짜: 이벤트 번호(배포 시작일 yyyyMMdd)가 날짜면 그날, 아니면 오늘
        var id = identifier.ToString();
        pk.MetDate = id.Length == 8 && DateOnly.TryParseExact(id, "yyyyMMdd", out var d) ? d : DateOnly.FromDateTime(DateTime.Now);
        pk.Nickname = SpeciesName.GetSpeciesNameGeneration(r.Species, pk.Language, 9);
        Encounter9RNG.GenerateData(pk, param, EncounterCriteria.Unrestricted, seed);
        pk.TeraTypeOriginal = (MoveType)Tera9RNG.GetTeraType(seed, (GemType)r.Gem, r.Species, r.Form);
        if (r.Moves.Any(m => m != 0)) pk.SetMoves(r.Moves);
        if (r.Item > 0) pk.HeldItem = r.Item;
        pk.ResetPartyStats(); pk.RefreshChecksum();
        return pk;
    }

    /// <summary>파일에서 가져온 이벤트를 세이브 블록에 씀. 반환: 쓴 블록 수. 블록보다 큰 파일은 거절, 작으면 뒤를 0으로.</summary>
    public static int Import(SAV9SV sav, IReadOnlyDictionary<string, byte[]> files, out string error)
    {
        error = null; int n = 0;
        foreach (var (name, key) in Files)
        {
            byte[] data = null;
            foreach (var patch in Patches) if (files.TryGetValue(name + patch, out data)) break;
            if (data == null) { if (name == "raid_enemy_array") { error = "raid_enemy_array 파일이 없습니다"; return 0; } continue; }
            var b = sav.Accessor.GetBlockSafe(key);
            if (b.Type == SCTypeCode.None) { error = $"세이브에 {name} 블록이 없습니다"; continue; }
            var dst = b.Data;
            if (data.Length > dst.Length) { error = $"{name} 파일이 블록보다 큽니다 ({data.Length} > {dst.Length})"; continue; }
            var buf = new byte[dst.Length]; data.CopyTo(buf, 0);
            b.ChangeData(buf); n++;
        }
        return n;
    }

    /// <summary>아주 작은 FlatBuffers 읽기 (테이블 필드 번호로).</summary>
    public sealed class Fb
    {
        private readonly byte[] b; private readonly int pos;
        private Fb(byte[] buf, int p) { b = buf; pos = p; }
        public static Fb Root(byte[] buf) => new(buf, BinaryPrimitives.ReadInt32LittleEndian(buf));
        private int Off(int field)
        {
            int vt = pos - BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(pos));
            int len = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(vt));
            int slot = 4 + 2 * field;
            return slot < len ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(vt + slot)) : 0;
        }
        public int I32(int f) { var o = Off(f); return o == 0 ? 0 : BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(pos + o)); }
        public int I16(int f) { var o = Off(f); return o == 0 ? 0 : BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(pos + o)); }
        public ushort U16(int f) { var o = Off(f); return o == 0 ? (ushort)0 : BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(pos + o)); }
        public int I8(int f) { var o = Off(f); return o == 0 ? 0 : (sbyte)b[pos + o]; }
        public bool Bool(int f) { var o = Off(f); return o != 0 && b[pos + o] != 0; }
        public ulong U64(int f) { var o = Off(f); return o == 0 ? 0 : BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(pos + o)); }
        public Fb Table(int f)
        {
            var o = Off(f); if (o == 0) return null;
            int at = pos + o; return new Fb(b, at + BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at)));
        }
        public List<Fb> Vector(int f)
        {
            var list = new List<Fb>(); var o = Off(f); if (o == 0) return list;
            int at = pos + o; int vec = at + BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
            int count = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(vec));
            for (int i = 0; i < count; i++) { int item = vec + 4 + 4 * i; list.Add(new Fb(b, item + BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(item)))); }
            return list;
        }
    }
}
