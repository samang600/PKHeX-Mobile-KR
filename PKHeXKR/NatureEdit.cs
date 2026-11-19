using PKHeX.Core;

namespace PKHeXKR;

/// <summary>
/// 성격 바꾸기. 3~4세대는 성격이 PID(성격값)로 정해져서 <see cref="PKM.Nature"/>에 넣어도 바뀌지 않음 →
/// 그 성격이 나오는 PID를 새로 고름. 이로치·성별·특성 칸(PID 마지막 비트)·안농 폼은 가능한 한 그대로 둠.
/// 맞는 PID를 못 찾으면 성격만 맞춘 PID로 (합법성은 따지지 않음).
/// </summary>
public static class NatureEdit
{
    public static void Set(PKM pk, Nature n)
    {
        if (pk.Format is 3 or 4) SetPID(pk, n);
        else pk.Nature = n;
        if (pk.Format < 8) pk.StatAlignment = n;
    }

    private static void SetPID(PKM pk, Nature n)
    {
        if (pk.Nature == n) return;
        uint old = pk.PID;
        bool shiny = pk.IsShiny;
        byte gender = pk.Gender;
        byte form = pk.Form;
        uint ab = old & 1;
        uint tsv = (uint)(pk.TID16 ^ pk.SID16);
        var rnd = Util.Rand;
        bool Ok(bool strict) =>
            pk.Nature == n && (!strict || ((pk.PID & 1) == ab && pk.Gender == gender && pk.Form == form && pk.IsShiny == shiny));
        for (int strictPass = 1; strictPass >= 0; strictPass--)
        {
            bool strict = strictPass == 1;
            for (int i = 0; i < 2_000_000; i++)
            {
                uint lo = (uint)rnd.Next(0x10000);
                uint hi = shiny ? (lo ^ tsv ^ (uint)rnd.Next(8)) & 0xFFFF : (uint)rnd.Next(0x10000);
                pk.PID = (hi << 16) | lo;
                if (Ok(strict)) { Finish(pk, old); return; }
            }
        }
        // 이론상 여기 오지 않음: 25로 나눈 나머지만 맞춤
        pk.PID = old - (old % 25) + (uint)n;
        Finish(pk, old);
    }

    private static void Finish(PKM pk, uint old)
    {
        if (pk.Format >= 6 && (pk.Gen3 || pk.Gen4 || pk.Gen5) && pk.EncryptionConstant == old) pk.EncryptionConstant = pk.PID;
        pk.RefreshChecksum();
    }
}
