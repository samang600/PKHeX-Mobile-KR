using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// 스위치판 파이어레드·리프그린 기준 합법성.
/// PKHeX(26.10.18~)는 세이브 파일 이름(FireRed_*.sav · LeafGreen_*.sav, 스위치판이 쓰는 이름)으로 스위치판을 알아보고
/// 그 기준(루비·사파이어·에메랄드·XD 출신과 옛 배포 불가, 볼·유전 제한 등)으로 검사함.
/// 이 토글은 파일 이름이 달라도(main 등) 스위치판 기준을 강제로 적용함 (예전의 자체 판정은 PKHeX 기준으로 대체).
/// </summary>
public static class SwitchFrlg
{
    public static bool Enabled { get => Preferences.Get("sw_frlg", false); set { Preferences.Set("sw_frlg", value); AppState.ApplyParse(); } }

    /// <summary>PKHeX가 파일 이름으로 스위치판이라고 본 세이브인지</summary>
    public static bool DetectedByName(SaveFile sav) => sav is SAV3 { IsVirtualConsole: true };

    /// <summary>ParseSettings.InitFromSaveFileData 다음에 부름: 토글이 켜져 있으면 FRLG 세이브를 스위치판 기준으로.</summary>
    public static void Apply(SaveFile sav)
    {
        if (!Enabled || sav is not SAV3FRLG) return;
        ParseSettings.AllowEraCartGBA = false;
        ParseSettings.AllowEraSwitchGBA = true;
    }

    public static bool IsVirtualConsoleName(string path) => path != null && SAV3.IsVirtualConsoleFileName(Path.GetFileName(path));

    /// <summary>지금 세이브에 스위치판 기준이 적용되는지</summary>
    public static bool Active(SaveFile sav) => sav is SAV3FRLG && (DetectedByName(sav) || Enabled);

    /// <summary>DBI 백업 zip의 정보 파일로 스위치판 FRLG 세이브인지 확인 (TitleName에 FireRed/LeafGreen).</summary>
    public static bool IsSwitchZip(byte[] zip)
    {
        try
        {
            using var za = new System.IO.Compression.ZipArchive(new MemoryStream(zip), System.IO.Compression.ZipArchiveMode.Read);
            var e = za.Entries.FirstOrDefault(x => x.Name.Equals(".dbi_save_info.ini", StringComparison.OrdinalIgnoreCase));
            if (e == null) return false;
            using var r = new StreamReader(e.Open());
            var t = r.ReadToEnd();
            return t.Contains("FireRed", StringComparison.OrdinalIgnoreCase) || t.Contains("LeafGreen", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
