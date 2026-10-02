using System;

namespace PKHeX.Core.Injection;

public sealed class LiveHeXController
{
    private readonly ISaveFileProvider SAV;
    public readonly IPKMView Editor;
    public PokeSysBotMini Bot;

    public LiveHeXController(ISaveFileProvider boxes, IPKMView editor, InjectorCommunicationType ict)
    {
        SAV = boxes;
        Editor = editor;
        var ValidVers = RamOffsets.GetValidVersions(boxes.SAV);
        var com = RamOffsets.GetCommunicator(SAV.SAV, ict);
        Bot = new PokeSysBotMini(ValidVers[0], com);
    }

    public void ChangeBox(int box)
    {
        if (!Bot.Connected)
            return;

        var sav = SAV.SAV;
        if ((uint)box >= sav.BoxCount)
            return;

        ReadBox(box);
    }

    public void ReadBox(int box)
    {
        var sav = SAV.SAV;
        var len = sav.BoxSlotCount * (RamOffsets.GetSlotSize(Bot.Version) + RamOffsets.GetGapSize(Bot.Version));
        var data = Bot.ReadBox(box, len);
        if (data.Length == 0)
            return;
        sav.SetBoxBinary(data, box);
        SAV.ReloadSlots();
    }

    public void WriteBox(int box)
    {
        var boxData = SAV.SAV.GetBoxBinary(box);
        Bot.SendBox(boxData, box);
    }

    public bool ReadOffset(ulong offset, RWMethod method = RWMethod.Heap)
    {
        var data = ReadData(offset, method);
        var pkm = SAV.SAV.GetDecryptedPKM(data.ToArray());

        // Since data might not actually exist at the user-specified offset, double check that the pkm data is valid.
        if (!pkm.ChecksumValid)
            return false;

        Editor.PopulateFields(pkm);
        return true;
    }

    private Span<byte> ReadData(ulong offset, RWMethod method)
    {
        return Bot.com is not ICommunicatorNX nx ? Bot.ReadOffset(offset) : method switch
        {
            RWMethod.Heap => Bot.ReadOffset(offset),
            RWMethod.Main => nx.ReadBytesMain(offset, Bot.SlotSize),
            RWMethod.Absolute => nx.ReadBytesAbsolute(offset, Bot.SlotSize),
            _ => Bot.ReadOffset(offset),
        };
    }

    public Span<byte> ReadRAM(ulong offset, int size) => Bot.com.ReadBytes(offset, size);

    public void WriteRAM(ulong offset, byte[] data) => Bot.com.WriteBytes(data, offset);
}
