namespace PKHeX.Core;

/// <summary>
/// (PKHeX 한국어 모바일 추가) 레이드 편집기에서 시드 → 조우 미리보기에 쓰는 테라 레이드 조우 표.
/// </summary>
public static class TeraRaidTables
{
    public static EncounterTera9[] Get(TeraRaidMapParent map) => map switch
    {
        TeraRaidMapParent.Kitakami => Encounters9.TeraDLC1,
        TeraRaidMapParent.Blueberry => Encounters9.TeraDLC2,
        _ => Encounters9.TeraBase,
    };
}
