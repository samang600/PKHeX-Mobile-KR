using System;
using System.Collections.Generic;
using System.Linq;
using static PKHeX.Core.Injection.LiveHeXVersion;

namespace PKHeX.Core.Injection;

public abstract class InjectionBase
{
    public static readonly System.Version BotbaseVersion = new(2, 5);

    private const ulong Ovlloader_ID = 0x420000000007e51a;
    private const ulong Dmnt_ID = 0x010000000000000d;

    private const string LetsGoPikachu_ID = "010003F003A34000";
    private const string LetsGoEevee_ID = "0100187003A36000";

    private const string Sword_ID = "0100ABF008968000";
    private const string Shield_ID = "01008DB008C2C000";

    private const string ShiningPearl_ID = "010018E011D92000";
    private const string BrilliantDiamond_ID = "0100000011D90000";

    private const string LegendsArceus_ID = "01001F5010DFA000";

    private const string Scarlet_ID = "0100A3D008C5C000";
    private const string Violet_ID = "01008F6008C5E000";

    private const string ZA_ID = "0100F43008C44000";
    private const string FR_E_ID = "0100554023408000";
    private const string LG_E_ID = "010034D02340E000";
    private const string FR_S_ID = "0100EB702342C000";
    private const string LG_S_ID = "01002B5023434000";
    private const string FR_F_ID = "01004B3023412000";
    private const string LG_F_ID = "010087C02342E000";
    private const string FR_D_ID = "01007F8023416000";
    private const string LG_D_ID = "0100FD6023430000";
    private const string FR_I_ID = "010092302342A000";
    private const string LG_I_ID = "01005C7023432000";
    private const string FR_J_ID = "01006FA0233F8000";
    private const string LG_J_ID = "0100F1E0233FA000";
    private static readonly Dictionary<string, LiveHeXVersion[]> SupportedTitleVersions = new()
    {
        { LetsGoPikachu_ID, [LGPE_v102] },
        { LetsGoEevee_ID, [LGPE_v102] },
        { Sword_ID,  [SWSH_v111, SWSH_v121, SWSH_v132] },
        { Shield_ID, [SWSH_v111, SWSH_v121, SWSH_v132] },
        { ShiningPearl_ID,     [SP_v100, SP_v110, BDSP_v112, BDSP_v113, BDSP_v120, SP_v130] },
        { BrilliantDiamond_ID, [BD_v100, BD_v110, BDSP_v112, BDSP_v113, BDSP_v120, BD_v130] },
        { LegendsArceus_ID, [LA_v100, LA_v101, LA_v102, LA_v111] },
        { Scarlet_ID, [SV_v101, SV_v110, SV_v120, SV_v130, SV_v131, SV_v132, SV_v201, SV_v202, SV_v300, SV_v301, SV_v400] },
        { Violet_ID,  [SV_v101, SV_v110, SV_v120, SV_v130, SV_v131, SV_v132, SV_v201, SV_v202, SV_v300, SV_v301, SV_v400] },
        { ZA_ID, [ZA_v101, ZA_v102, ZA_v103, ZA_v200, ZA_v201, ZA_v202] },
        { FR_E_ID, [FRLG_E_v100] },
        { LG_E_ID, [FRLG_E_v100] },
        { FR_S_ID, [FRLG_S_v100] },
        { LG_S_ID, [FRLG_S_v100] },
        { FR_F_ID, [FRLG_F_v100] },
        { LG_F_ID, [FRLG_F_v100] },
        { FR_D_ID, [FRLG_D_v100] },
        { LG_D_ID, [FRLG_D_v100] },
        { FR_I_ID, [FRLG_I_v100] },
        { LG_I_ID, [FRLG_I_v100] },
        { FR_J_ID, [FRLG_J_v100] },
        { LG_J_ID, [FRLG_J_v100] },
    };

    public virtual Dictionary<string, string> SpecialBlocks { get; } = [];

    protected static InjectionBase GetInjector(LiveHeXVersion version)
    {
        if (LPLGPE.SupportedVersions.Contains(version))
            return new LPLGPE();

        if (LPBDSP.SupportedVersions.Contains(version))
            return new LPBDSP();

        if (LPPointer.SupportedVersions.Contains(version))
            return new LPPointer();
        if (LPFRLG.SupportedVersions.Contains(version))
            return new LPFRLG();
        if (!LPBasic.SupportedVersions.Contains(version))
            throw new ArgumentOutOfRangeException(nameof(version), version, $"Unknown {nameof(LiveHeXVersion)}.");

        return new LPBasic();
    }

    public virtual Span<byte> ReadBox(PokeSysBotMini psb, int box, int len, List<byte[]> allpkm) => [];

    public virtual Span<byte> ReadSlot(PokeSysBotMini psb, int box, int slot) => [];

    public virtual void SendBox(PokeSysBotMini psb, Span<byte> boxData, int box) { }

    public virtual void SendSlot(PokeSysBotMini psb, ReadOnlySpan<byte> data, int box, int slot) { }

    public virtual void WriteBlocksFromSAV(PokeSysBotMini psb, string block, SaveFile sav) { }
    public virtual void WriteBlocksFromStringSAV(PokeSysBotMini psb, string block, SaveFile sav, Span<byte> data) { }
    public virtual void WriteBlockFromString(PokeSysBotMini psb, string block, ReadOnlySpan<byte> data, object sb) { }

    public virtual bool ReadBlockFromString(PokeSysBotMini psb, SaveFile sav, string block, out List<byte[]>? read)
    {
        read = null;
        return false;
    }

    public static bool SaveCompatibleWithTitle(SaveFile sav, string titleID) => sav switch
    {
        SAV3FRLG when titleID is FR_D_ID or FR_E_ID or FR_F_ID or FR_I_ID or FR_J_ID or FR_S_ID or LG_D_ID or LG_E_ID or LG_F_ID or LG_I_ID or LG_J_ID or LG_S_ID => true,
        SAV9ZA when titleID is ZA_ID => true,
        SAV9SV when titleID is Scarlet_ID or Violet_ID => true,
        SAV8LA when titleID is LegendsArceus_ID => true,
        SAV8BS when titleID is BrilliantDiamond_ID or ShiningPearl_ID => true,
        SAV8SWSH when titleID is Sword_ID or Shield_ID => true,
        SAV7b when titleID is LetsGoPikachu_ID or LetsGoEevee_ID => true,
        _ => false,
    };

    public static LiveHeXVersion GetVersionFromTitle(string titleID, string gameVersion)
    {
        if (!SupportedTitleVersions.TryGetValue(titleID, out var versions))
            return Unknown;

        versions = [.. versions.Reverse()];
        var sanitized = gameVersion.Replace(".", "");
        foreach (var version in versions)
        {
            var name = Enum.GetName(version);
            if (name is null)
                continue;

            name = name.Split('v')[1];
            if (name == sanitized)
                return version;
        }
        return Unknown;
    }

    public static bool CheckRAMShift(PokeSysBotMini psb, out string msg)
    {
        msg = "";
        if (psb.com is not ICommunicatorNX nx)
            return false;

        if (nx.IsProgramRunning(Ovlloader_ID))
            msg += "Tesla overlay";

        if (nx.IsProgramRunning(Dmnt_ID))
            msg += msg != "" ? " and dmnt (cheats?)" : "Dmnt (cheats?)";

        bool detected = msg != "";
        msg += detected ? " detected.\n\nPlease remove or close the interfering applications and reboot your Switch." : "";
        return detected;
    }
}
