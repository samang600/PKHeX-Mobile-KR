using PKHeX.Core;
namespace PKHeXKR;

/// <summary>생성 조건 (원본 PKHeX의 인카운터 DB 조건처럼). 시트를 닫아도 유지.</summary>
public static class EncCond
{
    public static int Gender = -1;          // -1 무관, 0 수컷, 1 암컷
    public static int ShinyMode = 0;        // 0 무관, 1 이로치, 2 일반, 3 별, 4 네모
    public static int Nature = -1;          // -1 무관
    public static int Ability = -1;         // -1 무관, 0/1/2(숨)
    public static int[] IVMin = new int[6];          // 개체값 최솟값 (31이면 V)
    public static bool IVExact;                       // true: 입력한 값과 정확히 같은 개체값 (빈 칸 = 무관)
    public static bool AnyIV => IVExact ? IVMin.Any(v => v >= 0) : IVMin.Any(v => v > 0);
    public static ulong EndSeed;                      // 0 = 끝 없음
    public static bool HasAny => Mark >= 0 || Gender >= 0 || ShinyMode > 0 || Nature >= 0 || Ability >= 0 || AnyIV || HasSize;
    public static int SeedMode;                       // 0 무작위, 1 처음부터(지정 시드부터 순서대로)
    public static ulong StartSeed, LastSeed;
    public static int ResultMode;                     // 0 첫 개체, 1 여러 개, 2 전체(32비트 조우)
    public static int MaxResults = 20;
    public static bool ZACharm, ZAPower;
    public static int Mark = -1;                      // 증표 (RibbonIndex), -1 = 무관
    public static RibbonIndex MarkIndex => (RibbonIndex)Mark;
    public static string MarkName(int m) { if (m < 0) return "무관"; var n = "Ribbon" + ((RibbonIndex)m).ToString(); return GenHost.Strings.Ribbons.GetNameSafe(n, out var k) ? k : ((RibbonIndex)m).ToString(); }
    public static List<ComboItem> MarkItems() { var l = new List<ComboItem> { new("무관", -1) }; l.AddRange(Enum.GetValues<RibbonIndex>().Where(x => x.ToString().StartsWith("Mark")).Select(x => new ComboItem(MarkName((int)x), (int)x))); return l; }

    /// <summary>소드실드 야생 슬롯 전체 (PKHeX 조우 데이터).</summary>
    private static List<EncounterSlot8> allSlots8;
    /// <summary>PKHeX 조우 데이터에서 특정 종류 전부 (배열로 들어 있는 것).</summary>
    public static List<TEnc> AllOf<TEnc>()
    {
        var list = new List<TEnc>();
        try
        {
            foreach (var f in typeof(EncounterArea8).Assembly.GetTypes().SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)).Where(f => f.FieldType == typeof(TEnc[])))
                if (f.GetValue(null) is TEnc[] arr) list.AddRange(arr);
        }
        catch { }
        return list;
    }
    public static IEnumerable<EncounterSlot8> AllSlots8()
    {
        if (allSlots8 != null) return allSlots8;
        var list = new List<EncounterSlot8>();
        try
        {
            foreach (var f in typeof(EncounterArea8).Assembly.GetTypes().SelectMany(t => t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)).Where(f => f.FieldType == typeof(EncounterArea8[])))
                if (f.GetValue(null) is EncounterArea8[] areas) foreach (var a in areas) list.AddRange(a.Slots);
        }
        catch { }
        return allSlots8 = list;
    }
    /// <summary>고른 증표가 가능한 소드실드 야생 조우가 있는 포켓몬 (카레·낚시·날씨 증표).</summary>
    public static List<ushort> MarkSpecies()
    {
        if (Mark < 0) return [];
        if (MarkIndex == RibbonIndex.MarkMightiest) return AllOf<EncounterMight9>().Select(x => x.Species).Distinct().OrderBy(x => x).ToList();
        return AllSlots8().Where(x => MarkPossible(x)).Select(x => x.Species).Distinct().OrderBy(x => x).ToList();
    }

    /// <summary>이 조우에서 고른 증표가 가능한지 (PKHeX 증표 규칙 기준).</summary>
    public static bool MarkPossible(IEncounterInfo e)
    {
        if (Mark < 0) return true;
        var m = MarkIndex;
        if (m == RibbonIndex.MarkCurry) return e is EncounterSlot8 { CanEncounterViaCurry: true };
        if (m == RibbonIndex.MarkFishing) return e is EncounterSlot8 { CanEncounterViaFishing: true };
        if (m == RibbonIndex.MarkMightiest) return e is EncounterMight9;   // 최강의증표: SV 7성 최강 레이드
        if (e is EncounterSlot8 s8)
        {
            var need = m switch
            {
                RibbonIndex.MarkCloudy => AreaWeather8.Overcast, RibbonIndex.MarkRainy => AreaWeather8.Raining, RibbonIndex.MarkStormy => AreaWeather8.Thunderstorm,
                RibbonIndex.MarkSnowy => AreaWeather8.Snowing, RibbonIndex.MarkBlizzard => AreaWeather8.Snowstorm, RibbonIndex.MarkDry => AreaWeather8.Intense_Sun,
                RibbonIndex.MarkSandstorm => AreaWeather8.Sandstorm, RibbonIndex.MarkMisty => AreaWeather8.Heavy_Fog, _ => AreaWeather8.None,
            };
            if (need != AreaWeather8.None && (s8.Weather & need) == 0) return false;
        }
        // 증표는 소드실드·SV의 야생·대량발생·고정 조우에서만 (세부 조건은 생성 후 합법성 검사로 확인)
        return e is EncounterSlot8 or EncounterStatic8 or EncounterSlot9 or EncounterOutbreak9 or EncounterStatic9;
    }
    /// <summary>만든 개체에 증표를 붙임 (카레의증표는 몬스터·수퍼·하이퍼볼만 가능하므로 볼도 맞춤).</summary>
    public static void ApplyMark(PKM p)
    {
        if (Mark < 0) return;
        try
        {
            p.GetType().GetProperty("Ribbon" + MarkIndex.ToString())?.SetValue(p, true);
            if (MarkIndex == RibbonIndex.MarkCurry)
            {
                if ((uint)(p.Ball - 2) > 2) p.Ball = (byte)Ball.Poke;
                // 카레로 온 포켓몬은 야생 시드 연동(PID·개체값)이 없음 → 연동 없이 다시 뽑음 (이로치·개체값 조건은 반영)
                var rnd = Random.Shared;
                p.EncryptionConstant = (uint)rnd.NextInt64();
                uint pid = (uint)rnd.NextInt64(), low = pid & 0xFFFF, tsv = (uint)(p.TID16 ^ p.SID16);
                pid = ShinyMode switch
                {
                    1 or 4 => ((tsv ^ low) << 16) | low,
                    3 => ((tsv ^ low ^ 1) << 16) | low,
                    _ => ShinyUtil.GetShinyXor(p.ID32, pid) < 16 ? pid ^ 0x10000000 : pid,
                };
                p.PID = pid;
                for (int i = 0; i < 6; i++)
                {
                    int v = IVExact ? (IVMin[i] >= 0 ? IVMin[i] : rnd.Next(32)) : IVMin[i] >= 31 ? 31 : rnd.Next(32);
                    switch (i) { case 0: p.IV_HP = v; break; case 1: p.IV_ATK = v; break; case 2: p.IV_DEF = v; break; case 3: p.IV_SPA = v; break; case 4: p.IV_SPD = v; break; default: p.IV_SPE = v; break; }
                }
                p.ResetPartyStats();
            }
            p.RefreshChecksum();
        }
        catch { }
    }               // Z-A: 빛나는부적, 이차원 이로치 파워 (야생 이로치 확률)
    public static bool Unlimited;   // 찾을 때까지 계속 (모바일: 앱 설정에서 읽어 넣음)

    /// <summary>조건을 얼마나 만족하는지 (가장 가까운 개체 고르기용).</summary>
    public static int Score(PKM p)
    {
        int s = 0;
        if (Gender < 0 || p.Gender == Gender) s += 3;
        bool shinyOk = ShinyMode switch { 1 => p.IsShiny, 2 => !p.IsShiny, 3 => p.IsShiny && p.ShinyXor != 0, 4 => p.IsShiny && p.ShinyXor == 0, _ => true };
        if (shinyOk) s += 5;
        if (Nature < 0 || (int)p.Nature == Nature) s += 3;
        if (Ability < 0 || p.AbilityNumber == (1 << Ability)) s += 2;
        s += Enumerable.Range(0, 6).Count(i => IVExact ? IVMin[i] < 0 || IV(p, i) == IVMin[i] : IV(p, i) >= Math.Min(IVMin[i], p.MaxIV));
        return s;
    }

    /// <summary>조우 자체가 조건을 절대 만족할 수 없으면 이유를 돌려줌.</summary>
    public static string Impossible(IEncounterInfo e)
    {
        var sh = e.Shiny;
        if (ShinyMode is 1 or 3 or 4 && sh == Shiny.Never) return "이 조우는 이로치가 잠겨 있습니다";
        if (ShinyMode == 2 && sh is Shiny.Always or Shiny.AlwaysStar or Shiny.AlwaysSquare) return "이 조우는 항상 이로치입니다";
        if (ShinyMode == 3 && sh == Shiny.AlwaysSquare) return "이 조우는 네모 이로치로 고정입니다";
        if (ShinyMode == 4 && sh == Shiny.AlwaysStar) return "이 조우는 별 이로치로 고정입니다";
        if (Nature >= 0 && e is IFixedNature fn && fn.IsFixedNature && (int)fn.Nature != Nature) return $"성격이 {GenHost.Strings.natures[(int)fn.Nature]}(으)로 고정된 조우입니다";
        if (Gender >= 0 && e is IFixedGender fg && fg.Gender < 3 && fg.Gender != Gender) return "성별이 고정된 조우입니다";
        if (Ability >= 0 && e is IFixedAbilityNumber fa)
        {
            var need = Ability switch { 0 => AbilityPermission.OnlyFirst, 1 => AbilityPermission.OnlySecond, _ => AbilityPermission.OnlyHidden };
            if (fa.Ability is AbilityPermission.OnlyFirst or AbilityPermission.OnlySecond or AbilityPermission.OnlyHidden && fa.Ability != need) return "특성이 고정된 조우입니다";
        }
        if (e is IAlphaReadOnly { IsAlpha: true } && e.Context == EntityContext.Gen9a && (SMin > 255 || SMax < 255)) return "Z-A 우두머리는 배율이 255로 고정입니다";
        if (!MarkPossible(e)) return $"이 조우에서는 {MarkName(Mark)}을(를) 붙일 수 없습니다";
        return null;
    }
    public static int HMin = 0, HMax = 255, WMin = 0, WMax = 255, SMin = 0, SMax = 255;
    public static bool HasSize => HMin > 0 || HMax < 255 || WMin > 0 || WMax < 255 || SMin > 0 || SMax < 255;

    private static sbyte Exact(int i) => (sbyte)(IVExact ? (IVMin[i] >= 0 ? IVMin[i] : -1) : IVMin[i] >= 31 ? 31 : -1);   // 31은 V로 고정 요청, 나머지는 생성 후 확인
    public static bool IVSatisfied(PKM p)
    {
        for (int i = 0; i < 6; i++)
        {
            if (IVExact) { if (IVMin[i] >= 0 && IV(p, i) != IVMin[i]) return false; }
            else if (IV(p, i) < Math.Min(IVMin[i], p.MaxIV)) return false;
        }
        return true;
    }
    public static int IV(PKM p, int i) => i switch { 0 => p.IV_HP, 1 => p.IV_ATK, 2 => p.IV_DEF, 3 => p.IV_SPA, 4 => p.IV_SPD, _ => p.IV_SPE };

    public static EncounterCriteria Criteria() => new()
    {
        Gender = Gender < 0 ? PKHeX.Core.Gender.Random : (Gender)Gender,
        Shiny = ShinyMode switch { 1 => Shiny.Always, 2 => Shiny.Never, 3 => Shiny.AlwaysStar, 4 => Shiny.AlwaysSquare, _ => Shiny.Random },
        Nature = Nature < 0 ? PKHeX.Core.Nature.Random : (Nature)Nature,
        Ability = Ability switch { 0 => AbilityPermission.OnlyFirst, 1 => AbilityPermission.OnlySecond, 2 => AbilityPermission.OnlyHidden, _ => AbilityPermission.Any12H },
        IV_HP = Exact(0), IV_ATK = Exact(1), IV_DEF = Exact(2), IV_SPA = Exact(3), IV_SPD = Exact(4), IV_SPE = Exact(5),
    };

    public static bool Satisfied(PKM p)
    {
        if (Gender >= 0 && p.Gender != Gender) return false;
        if (ShinyMode == 1 && !p.IsShiny) return false;
        if (ShinyMode == 2 && p.IsShiny) return false;
        if (ShinyMode == 3 && !(p.IsShiny && p.ShinyXor != 0)) return false;
        if (ShinyMode == 4 && !(p.IsShiny && p.ShinyXor == 0)) return false;
        if (Nature >= 0 && (int)p.Nature != Nature) return false;
        if (Ability >= 0 && p.AbilityNumber != (1 << Ability)) return false;
        for (int i = 0; i < 6; i++)
        {
            if (IVExact) { if (IVMin[i] >= 0 && IV(p, i) != IVMin[i]) return false; }
            else if (IV(p, i) < Math.Min(IVMin[i], p.MaxIV)) return false;
        }
        if (p is IScaledSize ss && (ss.HeightScalar < HMin || ss.HeightScalar > HMax || ss.WeightScalar < WMin || ss.WeightScalar > WMax)) return false;
        if (p is IScaledSize3 s3 && (s3.Scale < SMin || s3.Scale > SMax)) return false;
        return true;
    }
}
