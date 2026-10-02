using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using static PKHeX.Core.Injection.LiveHeXVersion;

namespace PKHeX.Core.Injection;

public sealed class LPPointer : InjectionBase
{
    public static ReadOnlySpan<LiveHeXVersion> SupportedVersions =>
    [
        ZA_v101,
        ZA_v102,
        ZA_v103,
        ZA_v200,
        ZA_v201,
        ZA_v202,
        SV_v101,
        SV_v110,
        SV_v120,
        SV_v130,
        SV_v131,
        SV_v132,
        SV_v201,
        SV_v202,
        SV_v300,
        SV_v301,
        SV_v400,
        LA_v100,
        LA_v101,
        LA_v102,
        LA_v111,
    ];

    private static BlockData Get(uint key, string pointer, string name, string display) => new()
    {
        Name = name,
        Display = display,
        SCBKey = key,
        Pointer = pointer,
    };
    private static BlockData Get(uint key, string pointer, string name, string display, SCTypeCode type) => new()
    {
        Name = name,
        Display = display,
        SCBKey = key,
        Pointer = pointer,
        Type = type,
    };
    private static BlockData Get(uint key, string pointer, string name, string display, SCTypeCode type, RWMethod method) => new()
    {
        Name = name,
        Display = display,
        SCBKey = key,
        Pointer = pointer,
        Type = type,
        Method = method,
    };
    private const int LA_MYSTATUS_BLOCK_SIZE = 0x80;
    private const int SV_MYSTATUS_BLOCK_SIZE = 0x68;
    private const int ZA_MYSTATUS_BLOCK_SIZE = 0x78;
    public static readonly BlockData[] Blocks_ZA_v201 =
    [
        Get(0xE3E89BD1, "[[main+610A710]+A0]+40", "MyStatus", "Trainer Data"), 
        Get(0x21C9BD44, "[[main+610A710]+D0]+40", "KItem", "Items"),
        Get(0x4F35D0DD, "[[main+610A710]+38]+40", "KMoney", "Money", SCTypeCode.UInt32),
        Get(0x2D87BE5C, "[[[main+610A710]+68]+40]", "Zukan", "Pokedex"),
        Get(0x1D7EE369, "[main+610F5A0]", "KTicketPointsZARoyaleInfinite", "ZA Royale Ticket Points", SCTypeCode.UInt32, RWMethod.Main), //Thank you Anubis
        Get(0x0235471C, "[[main+610A710]+160]+50", "KHyperspaceSurveyPoints", "Hyperspace Survey Points", SCTypeCode.UInt32),//Thank you Anubis
        Get(0xBE007476, "[[[main+610A710]+150]+40]", "KDonuts", "Donuts"),//Thank you Anubis
        Get(0x9E14BFFB, "[[[main+610A498]+328]+130]+1C0", "KHyperspaceZones", "Hyperspace Special Scan Seed", SCTypeCode.UInt64),
        Get(0x9E14BFFB, "[[[main+610A498]+328]+130]+1B8", "KHyperspaceZones", "Hyperspace Special Scan Pity", SCTypeCode.Byte)
    ];
    public static readonly BlockData[] Blocks_ZA_v200 =
    [
        Get(0xE3E89BD1, "[[main+6105710]+A0]+40", "MyStatus", "Trainer Data"), 
        Get(0x21C9BD44, "[[main+6105710]+D0]+40", "KItem", "Items"),
        Get(0x4F35D0DD, "[[main+6105710]+38]+40", "KMoney", "Money", SCTypeCode.UInt32),
        Get(0x2D87BE5C, "[[[main+6105710]+68]+40]", "Zukan", "Pokedex"),
        Get(0x1D7EE369, "[main+610A5A0]", "KTicketPointsZARoyaleInfinite", "ZA Royale Ticket Points", SCTypeCode.UInt32, RWMethod.Main), //Thank you Anubis
        Get(0x0235471C, "[[main+6105710]+160]+50", "KHyperspaceSurveyPoints", "Hyperspace Survey Points", SCTypeCode.UInt32),//Thank you Anubis
        Get(0xBE007476, "[[[main+6105710]+150]+40]", "KDonuts", "Donuts")//Thank you Anubis
    ];
    public static readonly BlockData[] Blocks_ZA_v103 =
    [
        Get(0xE3E89BD1, "[[main+5F0E250]+A0]+40", "MyStatus", "Trainer Data"), //Thanks Anubis
        Get(0x21C9BD44, "[[main+5F0E250]+D0]+40", "KItem", "Items"),
        Get(0xF3A8569D, "[[[main+5F0E250]+120]+168]", "KStoredShinyEntity", "Shiny Stash"), //Thanks Berichan
        Get(0x4F35D0DD, "[[main+5F0E250]+38]+40", "KMoney", "Money", SCTypeCode.UInt32),
        Get(0x2D87BE5C, "[[[main+5F0E250]+68]+40]", "Zukan", "Pokedex") //Thanks Anubis
    ];
    public static readonly BlockData[] Blocks_ZA_v102 =
    [
        Get(0xE3E89BD1, "[[main+5F0C250]+A0]+40", "MyStatus", "Trainer Data"), //Thanks Anubis
        Get(0x21C9BD44, "[[main+5F0C250]+D0]+40", "KItem", "Items"),
        Get(0xF3A8569D, "[[[main+5F0C250]+120]+168]", "KStoredShinyEntity", "Shiny Stash"), //Thanks Berichan
        Get(0x4F35D0DD, "[[main+5F0C250]+38]+40", "KMoney", "Money", SCTypeCode.UInt32),
        Get(0x2D87BE5C, "[[[main+5F0C250]+68]+40]", "Zukan", "Pokedex") //Thanks Anubis
    ];
    public static readonly BlockData[] Blocks_ZA_v101 =
    [
        Get(0xE3E89BD1, "[[main+5F0B250]+A0]+40", "MyStatus", "Trainer Data"), //Thanks Anubis
        Get(0x21C9BD44, "[[main+5F0B250]+D0]+40", "KItem", "Items"),
        Get(0xF3A8569D, "[[[main+5F0B250]+120]+168]", "KStoredShinyEntity", "Shiny Stash"), //Thanks Berichan
        Get(0x4F35D0DD, "[[main+5F0B250]+38]+40", "KMoney", "Money", SCTypeCode.UInt32),
        Get(0x2D87BE5C, "[[[main+5F0B250]+68]+40]", "Zukan", "Pokedex") //Thanks Anubis
    ];
    public static readonly BlockData[] Blocks_SV_v300 =
    [
        Get(0xE3E89BD1, "[[[main+47350d8]+1C0]+0]+40", "MyStatus", "Trainer Data"),
        Get(0x21C9BD44, "[[[main+47350d8]+1C0]+C8]+40", "KItem", "Items"),
        Get(0xCAAC8800, "[[[main+47350d8]+1C0]+88]+40", "KTeraRaidPaldea", "Raid"),
        Get(0x100B93DA, "[[[main+47350d8]+1C0]+88]+CD8", "KTeraRaidDLC", "RaidKitakami"),
        Get(0x100B93DA, "[[[main+47350d8]+1C0]+88]+CD8", "KTeraRaidDLC", "RaidBlueberry"),

    ];

    public static readonly BlockData[] Blocks_SV_v202 =
    [
        Get(0xE3E89BD1, "[[[main+4623A30]+198]]+40", "MyStatus", "Trainer Data"),
        Get(0x21C9BD44, "[[[main+4623A30]+198]+C8]+40", "KItem", "Items"),
        Get(0xCAAC8800, "[[[main+4623A30]+198]+88]+40", "KTeraRaidPaldea", "Raid"),
        Get(0x100B93DA, "[[[main+4623A30]+198]+88]+CD8", "KTeraRaidKitakami", "RaidKitakami"),
    ];

    public static readonly BlockData[] Blocks_SV_v201 =
    [
        Get(0xE3E89BD1, "[[[main+4622A30]+198]]+40", "MyStatus", "Trainer Data"),
        Get(0x21C9BD44, "[[[main+4622A30]+198]+C8]+40", "KItem", "Items"),
        Get(0xCAAC8800, "[[[main+4622A30]+198]+88]+40", "KTeraRaidPaldea", "Raid"),
        Get(0x100B93DA, "[[[main+4622A30]+198]+88]+CD8", "KTeraRaidKitakami", "RaidKitakami"),
    ];

    public static readonly BlockData[] Blocks_SV_v132 =
    [
        Get(0xE3E89BD1, "[[main+44C1C18]+100]+40", "MyStatus", "Trainer Data"),
        Get(0x21C9BD44, "[[main+44C1C18]+1B0]+40", "KItem", "Items"),
        Get(0xCAAC8800, "[[main+44C1C18]+180]+40", "KTeraRaidPaldea", "Raid"),
    ];

    public static readonly BlockData[] Blocks_SV_v130 =
    [
        Get(0xE3E89BD1, "[[main+44BFBA8]+100]+40", "MyStatus", "Trainer Data"),
        Get(0x21C9BD44, "[[main+44BFBA8]+1B0]+40", "KItem", "Items"),
        Get(0xCAAC8800, "[[main+44BFBA8]+180]+40", "KTeraRaidPaldea", "Raid"),
    ];

    public static readonly BlockData[] Blocks_SV_v120 =
    [
        Get(0xE3E89BD1, "[[main+44A98C8]+100]+40", "MyStatus", "Trainer Data"),
        Get(0x21C9BD44, "[[main+44A98C8]+1B0]+40", "KItem", "Items"),
        Get(0xCAAC8800, "[[main+44A98C8]+180]+40", "KTeraRaidPaldea", "Raid"),
    ];

    public static readonly BlockData[] Blocks_SV_v101 =
    [
        Get( 0xE3E89BD1, "[[main+42DA8E8]+148]+40", "MyStatus", "Trainer Data"),
        Get(0x21C9BD44, "[[main+42DA8E8]+1B0]+40", "KItem", "Items"),
        Get(0xCAAC8800, "[[main+42DA8E8]+180]+40", "KTeraRaidPaldea", "Raid"),
    ];

    public static readonly BlockData[] Blocks_SV_v110 =
    [
        Get(0xE3E89BD1, "[[main+4384B18]+148]+40", "MyStatus", "Trainer Data"),
        Get(0x21C9BD44, "[[main+4384B18]+1B0]+40", "KItem", "Items"),
        Get(0xCAAC8800, "[[main+4384B18]+180]+40", "KTeraRaidPaldea", "Raid"),
    ];

    public static readonly BlockData[] Blocks_LA_v100 =
    [
        Get(0xF25C070E, "[[main+4275470]+218]+68", "MyStatus", "Trainer Data"),
        Get(0x3279D927, "[[main+4275470]+210]+6C", "KMoney", "Money Data", SCTypeCode.UInt32),
        Get(0x9FE2790A, "[[main+4275470]+230]+68", "KItemRegular", "Items"),
        Get(0x59A4D0C3, "[[main+4275470]+230]+AF4", "KItemKey", "Items"),
        Get(0x8E434F0D, "[[main+4275470]+1E8]+68", "KItemStored", "Items"),
        Get(0xF5D9F4A5, "[[main+4275470]+230]+C84", "KItemRecipe", "Items"),
        Get(0x75CE2CF6, "[[[[[main+4275470]+1D8]+1B8]+70]+270]+38", "KSatchelUpgrades", "Items", SCTypeCode.UInt32),
        Get(0x02168706, "[[[[main+4275470]+248]+58]+18]+1C", "KZukan", "Pokedex"),
    ];

    public static readonly BlockData[] Blocks_LA_v101 =
    [
        Get(0xF25C070E, "[[main+427B470]+218]+68", "MyStatus", "Trainer Data"),
        Get(0x3279D927, "[[main+427B470]+210]+6C", "KMoney", "Money Data", SCTypeCode.UInt32),
        Get(0x9FE2790A, "[[main+427B470]+230]+68", "KItemRegular", "Items"),
        Get(0x59A4D0C3, "[[main+427B470]+230]+AF4", "KItemKey", "Items"),
        Get(0x8E434F0D, "[[main+427B470]+1E8]+68", "KItemStored", "Items"),
        Get(0xF5D9F4A5, "[[main+427B470]+230]+C84", "KItemRecipe", "Items"),
        Get(0x75CE2CF6, "[[[[[main+427B470]+1D8]+1B8]+70]+270]+38", "KSatchelUpgrades", "Items", SCTypeCode.UInt32),
        Get(0x02168706, "[[[[main+427B470]+248]+58]+18]+1C", "KZukan", "Pokedex"),
    ];

    public static readonly BlockData[] Blocks_LA_v102 =
    [
        Get(0xF25C070E, "[[main+427C470]+218]+68", "MyStatus", "Trainer Data"),
        Get(0x3279D927, "[[main+427C470]+210]+6C", "KMoney", "Money Data", SCTypeCode.UInt32),
        Get(0x9FE2790A, "[[main+427C470]+230]+68", "KItemRegular", "Items"),
        Get(0x59A4D0C3, "[[main+427C470]+230]+AF4", "KItemKey", "Items"),
        Get(0x8E434F0D, "[[main+427C470]+1E8]+68", "KItemStored", "Items"),
        Get(0xF5D9F4A5, "[[main+427C470]+230]+C84", "KItemRecipe", "Items"),
        Get(0x75CE2CF6, "[[[[[main+427C470]+1D8]+1B8]+70]+270]+38", "KSatchelUpgrades", "Items", SCTypeCode.UInt32),
        Get(0x02168706, "[[[[main+427C470]+248]+58]+18]+1C", "KZukan", "Pokedex"),
    ];

    public static readonly BlockData[] Blocks_LA_v110 =
    [
        Get(0xF25C070E, "[[main+42BA6B0]+218]+68", "MyStatus", "Trainer Data"),
        Get(0x3279D927, "[[main+42BA6B0]+210]+6C", "KMoney", "Money Data", SCTypeCode.UInt32),
        Get(0x75CE2CF6, "[[[[[main+42BA6B0]+1D8]+1D8]]+428]+18", "KSatchelUpgrades", "Items", SCTypeCode.UInt32),
        Get(0x9FE2790A, "[[main+42BA6B0]+230]+68", "KItemRegular", "Items"),
        Get(0x59A4D0C3, "[[main+42BA6B0]+230]+AF4", "KItemKey", "Items"),
        Get(0x8E434F0D, "[[main+42BA6B0]+1E8]+68", "KItemStored", "Items"),
        Get(0xF5D9F4A5, "[[main+42BA6B0]+230]+C84", "KItemRecipe", "Items"),
        Get(0x02168706, "[[[[main+42BA6B0]+248]+58]+18]+1C", "KZukan", "Pokedex"),
    ];

    // LiveHexVersion -> Blockname -> List of <SCBlock Keys, OffsetValues>
    public static readonly Dictionary<LiveHeXVersion, BlockData[]> SCBlocks = new()
    {
        { ZA_v202, Blocks_ZA_v201 },
        { ZA_v201, Blocks_ZA_v201 },
        { ZA_v200, Blocks_ZA_v200 },
        { ZA_v103, Blocks_ZA_v103 },
        { ZA_v102, Blocks_ZA_v102 },
        { ZA_v101, Blocks_ZA_v101 },
        { SV_v400, Blocks_SV_v300 },
        { SV_v301, Blocks_SV_v300 },
        { SV_v300, Blocks_SV_v300 },
        { SV_v202, Blocks_SV_v202 },
        { SV_v201, Blocks_SV_v201 },
        { SV_v132, Blocks_SV_v132 },
        { SV_v131, Blocks_SV_v130 },
        { SV_v130, Blocks_SV_v130 },
        { SV_v120, Blocks_SV_v120 },
        { SV_v110, Blocks_SV_v110 },
        { SV_v101, Blocks_SV_v101 },
        { LA_v100, Blocks_LA_v100 },
        { LA_v101, Blocks_LA_v101 },
        { LA_v102, Blocks_LA_v102 },
        { LA_v111, Blocks_LA_v110 },
    };

    public override Dictionary<string, string> SpecialBlocks { get; } = new()
    {
        { "Items", "B_OpenItemPouch_Click" },
        { "Pokedex", "B_OpenPokedex_Click" },
        { "Raid", "B_OpenRaids_Click" },
        { "RaidKitakami", "B_OpenRaids_Click" },
        { "RaidBlueberry", "B_OpenRaids_Click" },
        //{ "Trainer Data", "B_OpenTrainerInfo_Click" },
        { "Donuts", "B_Donuts_Click" }
    };

    private static string GetB1S1Pointer(LiveHeXVersion lv) => lv switch
    {
        ZA_v202 => "[[[main+610A710]+B0]+978]",
        ZA_v201 => "[[[main+610A710]+B0]+978]", 
        ZA_v200 => "[[[main+6105710]+B0]+978]",
        ZA_v103 => "[[[main+5F0E250]+B0]+978]",
        ZA_v102 => "[[[main+5F0C250]+B0]+978]", 
        ZA_v101 => "[[[main+5F0B250]+B0]+978]", //Thanks Anubis
        SV_v300 or SV_v301 or SV_v400 => "[[[[main+47350d8]+1C0]+30]+9D0]",
        SV_v202 => "[[[[main+4623A30]+198]+30]+9D0]",
        SV_v201 => "[[[[main+4622A30]+198]+30]+9D0]",
        SV_v132 => "[[[main+44C1C18]+130]+9B0]",
        SV_v130 or SV_v131 => "[[[main+44BFBA8]+130]+9B0]",
        SV_v120 => "[[[main+44A98C8]+130]+9B0]",
        SV_v110 => "[[[main+4384B18]+128]+9B0]",
        SV_v101 => "[[[main+42DA8E8]+128]+9B0]",
        LA_v100 => "[[main+4275470]+1F0]+68",
        LA_v101 => "[[main+427B470]+1F0]+68",
        LA_v102 => "[[main+427C470]+1F0]+68",
        LA_v111 => "[[main+42BA6B0]+1F0]+68",
        _ => string.Empty,
    };

    public static string GetSaveBlockPointer(LiveHeXVersion lv) => lv switch
    {
        ZA_v202 => "[[main+610A670]+30]",
        ZA_v201 => "[[main+610A670]+30]",
        ZA_v200 => "[[main+6105670]+30]",
        ZA_v103 => "[[main+5F0E1B0]+30]",
        ZA_v102 => "[[main+5F0C1B0]+30]",
        ZA_v101 => "[[main+5F0B1B0]+30]",
        SV_v300 or SV_v301 or SV_v400 => "[[[[[main+47350d8]+D8]]]+30]",
        SV_v202 => "[[[[[main+4617648]+D8]]]+30]",
        SV_v201 => "[[[[[main+4616648]+D8]]]+30]",
        SV_v132 => "[[[[[main+44B71A8]+D8]]]+30]",
        SV_v130 or SV_v131 => "[[[[[main+44B5158]+D8]]]+30]",
        SV_v120 => "[[[[[main+449EEE8]+D8]]]+30]",
        _ => string.Empty,
    };

    public override Span<byte> ReadBox(PokeSysBotMini psb, int box, int _, List<byte[]> allpkm)
    {
        if (psb.com is not ICommunicatorNX sb)
            return ArrayUtil.ConcatAll(allpkm.ToArray());

        var lv = psb.Version;
        var b1s1 = sb.GetPointerAddress(GetB1S1Pointer(lv));
        var boxsize = RamOffsets.GetSlotCount(lv) * RamOffsets.GetSlotSize(lv);
        var boxstart = b1s1 + (ulong)(box * boxsize);
        return psb.com.ReadBytes(boxstart, boxsize);
    }

    public override Span<byte> ReadSlot(PokeSysBotMini psb, int box, int slot)
    {
        if (psb.com is not ICommunicatorNX sb)
            return new byte[psb.SlotSize];

        var lv = psb.Version;
        var slotsize = RamOffsets.GetSlotSize(lv);
        var b1s1 = sb.GetPointerAddress(GetB1S1Pointer(lv));
        var boxsize = RamOffsets.GetSlotCount(lv) * slotsize;
        var boxstart = b1s1 + (ulong)(box * boxsize);
        var slotstart = boxstart + (ulong)(slot * slotsize);
        return psb.com.ReadBytes(slotstart, slotsize);
    }

    public override void SendSlot(PokeSysBotMini psb, ReadOnlySpan<byte> data, int box, int slot)
    {
        if (psb.com is not ICommunicatorNX sb)
            return;

        var lv = psb.Version;
        var slotsize = RamOffsets.GetSlotSize(lv);
        var b1s1 = sb.GetPointerAddress(GetB1S1Pointer(lv));
        var boxsize = RamOffsets.GetSlotCount(lv) * slotsize;
        var boxstart = b1s1 + (ulong)(box * boxsize);
        var slotstart = boxstart + (ulong)(slot * slotsize);
        psb.com.WriteBytes(data, slotstart);
    }

    public override void SendBox(PokeSysBotMini psb, Span<byte> boxData, int box)
    {
        if (psb.com is not ICommunicatorNX sb)
            return;
        
        var lv = psb.Version;
        if (lv >= LiveHeXVersion.ZA_v101 && lv <= LiveHeXVersion.ZA_v202)
        {
            for ( var i = 0; i < 30; i++)
            {
                var data = boxData.Slice(i * 408, 408);
                data[344] = (byte)(data[0x08] == 0 ? data[344] : 1);
            }
        }
        var b1s1 = sb.GetPointerAddress(GetB1S1Pointer(lv));
        var boxsize = RamOffsets.GetSlotCount(lv) * RamOffsets.GetSlotSize(lv);
        var boxstart = b1s1 + (ulong)(box * boxsize);
        psb.com.WriteBytes(boxData, boxstart);
    }

    public override bool ReadBlockFromString(PokeSysBotMini psb, SaveFile sav, string block, out List<byte[]>? read)
    {
        read = null;
        if (psb.com is not ICommunicatorNX sb)
            return false;

        try
        {
            var offsets = SCBlocks[psb.Version].Where(z => z.Display == block);
            var blocks = sav.GetType().GetProperty("Blocks");
            var allblocks = blocks?.GetValue(sav);
            if (allblocks is not SCBlockAccessor scba)
                return false;

            foreach (var sub in offsets)
            {
                var scbkey = sub.SCBKey;
                var offset = sub.Pointer;
                var scb = scba.GetBlock(scbkey);
                if (scb.Type == SCTypeCode.None && sub.Type != SCTypeCode.None)
                    ReflectUtil.SetValue(scb, "Type", sub.Type);

                var ram = psb.com.ReadBytes(sb.GetPointerAddress(offset), scb.Data.Length);
                scb.ChangeData(ram);
                if (read is null)
                {
                    read = [ram.ToArray()];
                    continue;
                }

                read.Add(ram.ToArray());
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.StackTrace);
            return false;
        }
    }

    public override void WriteBlocksFromSAV(PokeSysBotMini psb, string block, SaveFile sav)
    {
        if (psb.com is not ICommunicatorNX sb)
            return;

        var blocks = sav.GetType().GetProperty("Blocks");
        var allblocks = blocks?.GetValue(sav);
        if (allblocks is not SCBlockAccessor scba)
            return;

        var offsets = SCBlocks[psb.Version].Where(z => z.Display == block);
        foreach (var sub in offsets)
        {
            var scbkey = sub.SCBKey;
            var offset = sub.Pointer;
            var scb = scba.GetBlock(scbkey);
            if (sub.Method == RWMethod.Main)
                sb.WriteBytesMain(scb.Data, sb.GetPointerAddress(offset));
            else
                sb.WriteBytes(scb.Data, sb.GetPointerAddress(offset));
        }
    }

    public static readonly Func<PokeSysBotMini, byte[]?> GetTrainerDataLA = psb =>
    {
        if (psb.com is not ICommunicatorNX sb)
            return null;

        var lv = psb.Version;
        var ptr = SCBlocks[lv].First(z => z.Name == "MyStatus").Pointer;
        var ofs = sb.GetPointerAddress(ptr);
        return ofs == 0 ? null : psb.com.ReadBytes(ofs, LA_MYSTATUS_BLOCK_SIZE).ToArray();
    };

    public static readonly Func<PokeSysBotMini, byte[]?> GetTrainerDataSV = psb =>
    {
        if (psb.com is not ICommunicatorNX sb)
            return null;

        var lv = psb.Version;
        var ptr = SCBlocks[lv].First(z => z.Name == "MyStatus").Pointer;
        var ofs = sb.GetPointerAddress(ptr);
        return ofs == 0 ? null : psb.com.ReadBytes(ofs, SV_MYSTATUS_BLOCK_SIZE).ToArray();
    };
    public static readonly Func<PokeSysBotMini, byte[]?> GetTrainerDataZA = psb =>
    {
        if (psb.com is not ICommunicatorNX sb)
            return null;

        var lv = psb.Version;
        var ptr = SCBlocks[lv].First(z => z.Name == "MyStatus").Pointer;
        var ofs = sb.GetPointerAddress(ptr);
        return ofs == 0 ? null : psb.com.ReadBytes(ofs, ZA_MYSTATUS_BLOCK_SIZE).ToArray();
    };
}
