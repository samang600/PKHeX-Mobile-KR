namespace PKHeX.Core.Injection;

public record BlockData
{
    public string Name { get; set; } = string.Empty;
    public string Display { get; set; } = string.Empty;
    public uint SCBKey { get; set; }
    public string Pointer { get; set; } = string.Empty;
    public ulong Offset { get; set; }
    public RWMethod Method { get; set; } = RWMethod.Heap;
    public SCTypeCode Type { get; set; } = SCTypeCode.None;
    public bool IsSecured { get; set; } = false;
}
