using PKHeX.Core;

namespace PKHeXKR;

/// <summary>
/// PokeLDN 교환 파일: PokeLDN(휴대폰 교환 앱·PC)이 교환에 그대로 쓰는 형식으로 포켓몬을 내보냄.
/// PokeLDN의 PKHeX 도우미(services/pkhex, 'check')와 같은 처리: 능력치 재계산·체크섬 갱신 후 게임별 형식으로 기록
/// (파이어레드·리프그린/Z-A: 복호화 파티, 레츠고·소드실드·아르세우스·SV: 암호화 파티, BDSP: 암호화 보관).
/// </summary>
public static class LdnExport
{
    /// <summary>-> (게임 키, 확장자), 교환할 수 없는 형식이면 null</summary>
    public static (string Game, string Ext)? GameOf(PKM pk) => pk switch
    {
        PK3 => ("frlg", "pk3"),
        PB7 => ("lgpe", "pb7"),
        PB8 => ("bdsp", "pb8"),
        PK8 => ("swsh", "pk8"),
        PA8 => ("pla", "pa8"),
        PK9 => ("sv", "pk9"),
        PA9 => ("za", "pa9"),
        _ => null,
    };

    public static byte[] Prepare(PKM source)
    {
        var pk = source.Clone();
        pk.ResetPartyStats();
        pk.RefreshChecksum();
        if (pk is PB7 pb7)
        {
            pb7.ResetPartyStats();
            pb7.ResetCalculatedValues();
            pb7.RefreshChecksum();
        }
        var game = GameOf(pk)?.Game ?? throw new NotSupportedException("이 형식은 PokeLDN 교환을 지원하지 않습니다");
        byte[] data;
        switch (game)
        {
            case "frlg" or "za":
                data = new byte[pk.SIZE_PARTY]; pk.WriteDecryptedDataParty(data); break;
            case "bdsp":
                data = new byte[pk.SIZE_STORED]; pk.WriteEncryptedDataStored(data); break;
            default:
                data = new byte[pk.SIZE_PARTY]; pk.WriteEncryptedDataParty(data); break;
        }
        return data;
    }

    /// <summary>소드·실드: 다른 게임 출신(HOME을 거쳐 온 꼴)인데 HOME 트래커가 없는 포켓몬.
    /// PKHeX 설정에 따라 합법으로 보여도 소드·실드 콘솔이 교환 중에 거부해서 끊길 수 있음 (트래커는 만들어 넣지 않음).</summary>
    public static bool SwshNoTracker(PKM pk) => pk is PK8 pk8 && !pk8.IsEgg && pk8.Version is not (GameVersion.SW or GameVersion.SH) && pk8.Tracker == 0;

    public static string FileName(PKM pk)
    {
        var ext = GameOf(pk)?.Ext ?? "bin";
        var name = SpeciesName.GetSpeciesNameGeneration(pk.Species, (int)LanguageID.Korean, pk.Format);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return $"{pk.Species:0000}_{name}_LDN.{ext}";
    }
}
