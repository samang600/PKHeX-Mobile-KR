using System.IO.Compression;
namespace PKHeXKR;

/// <summary>
/// 앱 데이터 통째로 내보내기·가져오기 (Android): 설정(트레이너·토글 등), 즐겨찾기, 세이브 백업, 최근 세이브, 내 사전, 받은 배포 데이터.
/// 서명 키를 바꿀 때(앱을 지우고 새로 설치) 데이터를 옮기는 용도.
/// </summary>
public static class AppDataBackup
{
    public static bool Supported => DeviceInfo.Platform == DevicePlatform.Android;
    private const string PrefsDir = "_shared_prefs/", FilesDir = "files/";

#if ANDROID
    private static string PrefsPath => Path.Combine(Android.App.Application.Context.ApplicationInfo!.DataDir!, "shared_prefs");
#else
    private static string PrefsPath => "";
#endif

    /// <summary>zip 바이트. (파일 수, 크기)는 안내용.</summary>
    public static (byte[] Data, int Files) Export()
    {
        using var ms = new MemoryStream(); int n = 0;
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var root = FileSystem.AppDataDirectory;
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.StartsWith("mgdb_builtin/") || rel.StartsWith(".")) continue;   // 앱에 들어 있는 카드는 새 앱이 다시 만듦
                // PokeLDN 교환: 파이썬(앱이 다시 풂)·펌웨어·임시 데이터는 빼고, prod.keys(개인 키)는 절대 넣지 않음
                if (rel.StartsWith("chaquopy/") || rel == "ldn/prod.keys" || rel.StartsWith("ldn/prod.keys") || rel.StartsWith("ldn/firmware/") || rel.StartsWith("ldn/pokeldn/")) continue;
                z.CreateEntryFromFile(f, FilesDir + rel, CompressionLevel.Fastest); n++;
            }
            if (Directory.Exists(PrefsPath))
                foreach (var f in Directory.EnumerateFiles(PrefsPath, "*.xml"))
                {
                    // FTP 비밀번호는 백업 파일에 넣지 않음 (백업 zip을 다른 곳에 옮기거나 공유해도 새지 않게)
                    var xml = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(f), "<string name=\"ftp_pass\"(?:\\s*/>|>.*?</string>)\\s*", "", System.Text.RegularExpressions.RegexOptions.Singleline);
                    using (var w = new StreamWriter(z.CreateEntry(PrefsDir + Path.GetFileName(f), CompressionLevel.Fastest).Open())) w.Write(xml);
                    n++;
                }
            var info = z.CreateEntry("pkhexkr_backup.txt");
            using (var w = new StreamWriter(info.Open())) w.Write($"PKHeX 모바일 데이터 백업\n버전 {AppInfo.Current.VersionString}\n{DateTime.Now:yyyy-MM-dd HH:mm}\n");
        }
        return (ms.ToArray(), n);
    }

    /// <summary>가져오기: 파일을 덮어쓰고 설정 파일을 바꿈. 끝나면 앱을 다시 시작해야 설정이 적용됨. 반환: 가져온 파일 수 (백업 파일이 아니면 -1).</summary>
    public static int Import(Stream zip)
    {
        using var z = new ZipArchive(zip, ZipArchiveMode.Read);
        if (z.GetEntry("pkhexkr_backup.txt") == null) return -1;
        int n = 0; var root = FileSystem.AppDataDirectory;
        // 경로 이탈 방지: 폴더 이름 + 구분자로 비교 (그냥 StartsWith면 "files"가 "files_evil" 같은 옆 폴더도 통과)
        static string Dir(string p) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)) + Path.DirectorySeparatorChar;
        var rootDir = Dir(root); var prefsDir = PrefsPath.Length > 0 ? Dir(PrefsPath) : null;
        foreach (var e in z.Entries)
        {
            if (e.FullName.EndsWith('/')) continue;
            string dest;
            if (e.FullName.StartsWith(FilesDir)) dest = Path.Combine(root, e.FullName[FilesDir.Length..]);
            else if (e.FullName.StartsWith(PrefsDir) && PrefsPath.Length > 0) dest = Path.Combine(PrefsPath, Path.GetFileName(e.FullName));
            else continue;
            var full = Path.GetFullPath(dest);
            if (!full.StartsWith(rootDir, StringComparison.Ordinal) && !(prefsDir != null && full.StartsWith(prefsDir, StringComparison.Ordinal))) continue;   // 경로 이탈 방지
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            e.ExtractToFile(full, true); n++;
        }
        return n;
    }

    /// <summary>설정 파일을 바꾼 뒤에는 앱을 완전히 끝내야 함 (실행 중인 설정이 덮어쓰지 않게).</summary>
    public static void Quit()
    {
#if ANDROID
        Platform.CurrentActivity?.FinishAffinity();
        Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
#endif
    }
}
