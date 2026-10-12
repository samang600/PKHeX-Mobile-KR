using PKHeX.Core;
namespace PKHeXKR;

public sealed class Seeder
{
    public PKM Template; public bool Is32; private Func<PKM, ulong, bool> gen;
    public bool Gen(PKM p, ulong seed) => gen(p, seed);
    /// <summary>불러오기 전 마무리: PLA는 크기 배율·기술 마스터 기록을 시드 결과에 맞춤.</summary>
    public PKM Finish(PKM p)
    {
        if (p is PA8 a)
        {
            if (!a.IsAlpha) { a.Scale = a.HeightScalar; a.ResetHeight(); a.ResetWeight(); }
            a.RefreshChecksum();
            if (!new LegalityAnalysis(a).Valid) { a.ClearMoveShopFlags(); if (a.IsAlpha) a.AlphaMove = 0; a.SetMoveShopFlags(a); }
        }
        p.RefreshChecksum();
        return p;
    }
    /// <summary>
    /// 소드실드 야생(심볼·숨은 조우·낚시 등): 32비트 시드 → 암호화 상수 → PID → 개체값 → 키 → 몸무게 (게임 순서 그대로).
    /// hexbyt3/SWSHSeedFinderPlugin 방식 참고. 성격·성별·특성은 이 시드와 무관해 조건대로 정함. 카레·나무 전용 조우는 시드를 쓰지 않아 제외.
    /// </summary>
    private static Seeder SWSHWild(EncounterSlot8 slot, ITrainerInfo sav, Seeder s)
    {
        PKM t;
        var crit = EncCond.Criteria() with { Shiny = Shiny.Random, IV_HP = -1, IV_ATK = -1, IV_DEF = -1, IV_SPA = -1, IV_SPD = -1, IV_SPE = -1 };
        try { t = slot.ConvertToPKM(sav, crit); } catch { return null; }
        if (t is not PK8 || slot.GetRequirement(t) == OverworldCorrelation8Requirement.MustNotHave) return null;
        s.Template = t; s.Is32 = true;
        bool brilliantOk = slot.Parent.PermitCrossover || (slot.Weather & AreaWeather8.Fishing) != 0;   // 빛나는 오라는 심볼·낚시만
        byte baseLevel = t.CurrentLevel;
        s.gen = (p, x) =>
        {
            var rng = new Xoroshiro128Plus((uint)x);
            p.EncryptionConstant = (uint)rng.NextInt();
            uint pid = (uint)rng.NextInt();
            bool natural = ShinyUtil.GetShinyXor(p.ID32, pid) < 16;
            uint low = pid & 0xFFFF, tsv = (uint)(p.TID16 ^ p.SID16);
            switch (EncCond.ShinyMode)
            {
                case 1: if (!natural) pid = ((tsv ^ low) << 16) | low; break;          // 이로치 (게임은 이로치 판정 후 PID를 맞춤)
                case 3: pid = ((tsv ^ low ^ 1) << 16) | low; break;                   // 별
                case 4: pid = ((tsv ^ low) << 16) | low; break;                       // 네모
                case 2: if (natural) pid ^= 0x10000000; break;                          // 일반 색
                default: if (natural) pid ^= 0x10000000; break;
            }
            p.PID = pid;
            bool Apply(int flawless)
            {
                var r = new Xoroshiro128Plus((uint)x); r.NextInt(); r.NextInt();   // 암호화 상수·PID 다음 상태
                Span<int> iv = stackalloc int[6]; iv.Fill(-1);
                for (int i = 0; i < flawless; i++) { int idx = (int)r.NextInt(6); while (iv[idx] != -1) idx = (int)r.NextInt(6); iv[idx] = 31; }
                for (int i = 0; i < 6; i++) if (iv[i] == -1) iv[i] = (int)r.NextInt(32);
                p.IV_HP = iv[0]; p.IV_ATK = iv[1]; p.IV_DEF = iv[2]; p.IV_SPA = iv[3]; p.IV_SPD = iv[4]; p.IV_SPE = iv[5];
                if (p is PK8 k) { k.HeightScalar = (byte)(r.NextInt(0x81) + r.NextInt(0x80)); k.WeightScalar = (byte)(r.NextInt(0x81) + r.NextInt(0x80)); }
                p.MetLevel = p.CurrentLevel = flawless == 0 ? baseLevel : slot.LevelMax;   // 빛나는 오라 개체는 최대 레벨
                return EncCond.IVSatisfied(p);
            }
            if (Apply(0) || !brilliantOk) return true;
            foreach (var f in new[] { 2, 3 }) if (Apply(f)) return true;   // 빛나는 오라: V 확정 2·3개 (게임은 1개는 없음)
            Apply(0); return true;
        };
        return s;
    }

    public static Seeder For(IEncounterInfo e, ITrainerInfo sav)
    {
        PKM t;
        try { t = e.ConvertToPKM(sav, EncounterCriteria.Unrestricted); } catch { return null; }
        if (t == null || t.Context != e.Context) return null;   // 다른 게임 형식으로 변환된 경우는 순차 계산 불가
        var s = new Seeder { Template = t };
        switch (e)
        {
            case ITeraRaid9 tr9:
                s.Is32 = true;
                s.gen = (p, x) =>
                {
                    if (tr9 is EncounterTera9 t9 && !t9.CanBeEncountered((uint)x)) return false;   // 이 시드는 다른 포켓몬을 뽑음 (별·버전 확률표)
                    if (!tr9.GenerateSeed32(p, (uint)x)) return false;
                    if (p is PK9 k9) k9.TeraTypeOriginal = (MoveType)Tera9RNG.GetTeraType((uint)x, tr9.TeraType, tr9.Species, tr9.Form);   // 시드로 정해지는 테라스탈 타입
                    return true;
                };
                return s;
            case EncounterSlot8 slot8:
                if (EncCond.Mark >= 0 && EncCond.MarkIndex == RibbonIndex.MarkCurry) return null;   // 카레 조우는 야생 시드 계산을 쓰지 않음
                return SWSHWild(slot8, sav, s);
            case IGenerateSeed32 g32: s.Is32 = true; s.gen = (p, x) => g32.GenerateSeed32(p, (uint)x); return s;
            case IGenerateSeed64 g64: s.gen = (p, x) => { g64.GenerateSeed64(p, sav, x); return true; }; return s;
            case WA9 { IsHOMEGift: true }: return null;
            case IEncounter9a z when t is PA9:
                var pi = PersonalTable.ZA[e.Species, e.Form];
                var param = z is EncounterSlot9a slot ? slot.GetParams(pi, EncCond.ZACharm, EncCond.ZAPower) : z.GetParams(pi);
                s.gen = (p, x) => LumioseRNG.GenerateData((PA9)p, param, EncounterCriteria.Unrestricted, x); return s;
        }
        if (e.Generation is 3 or 4 && !e.IsEgg)
        {
            // 3·4세대 Method 1/2/4 (PID → 개체값이 연속된 LCRNG에서 나옴). J/K처럼 성격을 먼저 뽑는 방식은 무작위 탐색으로
            var type = MethodFinder.Analyze(t).Type;
            if (type is not (PIDType.Method_1 or PIDType.Method_2 or PIDType.Method_4)) return null;
            s.Is32 = true;
            s.gen = (p, x) =>
            {
                uint r = (uint)x;
                uint Next() { r = unchecked(r * 0x41C64E6D + 0x6073); return r >> 16; }
                uint lo = Next(), hi = Next(); uint pid = (hi << 16) | lo;
                if (type == PIDType.Method_2) Next();
                uint a = Next(); if (type == PIDType.Method_4) Next(); uint b = Next();
                p.PID = pid;
                p.IV_HP = (int)(a & 31); p.IV_ATK = (int)((a >> 5) & 31); p.IV_DEF = (int)((a >> 10) & 31);
                p.IV_SPE = (int)(b & 31); p.IV_SPA = (int)((b >> 5) & 31); p.IV_SPD = (int)((b >> 10) & 31);
                if (p.Format == 3) p.RefreshAbility((int)(pid & 1)); else if (p.Format >= 4) { p.Nature = (Nature)(pid % 25); p.RefreshAbility((int)(pid & 1)); }
                if (p.Format >= 3) p.Gender = EntityGender.GetFromPID(p.Species, pid);
                return true;
            };
            return s;
        }
        return null;
    }
}
