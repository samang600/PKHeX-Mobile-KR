using System.Text;
using System.Text.Json;
using PKHeX.Core;

namespace PKHeXKR;

/// <summary>
/// PokeLDN 교환 (ESP32 보드, LDN판 빌드). LDN 교환 앱(ldnapp, github.com/Decryptu/pokeldn 기반 AGPLv3)을 라이브러리(AAR)로 넣고,
/// 화면은 이 앱 안(LdnSheet)에서. 실제 통신은 AAR 안의 파이썬(PokeLDN, Chaquopy)이 하고, 진행 상황은 LdnBridge.poll()로 가져옴.
/// 파일은 앱 폴더의 ldn/ 아래 (prod.keys, 받은 포켓몬, 세이브 백업, 파이썬 데이터).
/// </summary>
public static class LdnTrade
{
#if WITH_LDN
    public static bool Available => OperatingSystem.IsAndroidVersionAtLeast(26);   // 안드로이드 8 이상 (보드 USB 읽기 API)
#else
    public static bool Available => false;
#endif
    public static string Home { get { var d = Path.Combine(FileSystem.AppDataDirectory, "ldn"); Directory.CreateDirectory(d); return d; } }
    public static string KeysPath => Path.Combine(Home, "prod.keys");
    public const string FwVersion = "1.6.2";

    /// <summary>교환할 포켓몬 (최대 6, 지금 세이브 게임 형식). 시트를 닫아도 유지.</summary>
    public static readonly List<PKM> Offers = [];

    public record LdnGame(string Key, string Name, string HostGuide, string JoinGuide);
    public static readonly LdnGame[] Games =
    [
        new("frlg", "파이어레드·리프그린", "시작을 누른 뒤 게임에서 무선 통신 → 다이렉트 코너 → 그룹에 참가 → 이 앱의 트레이너를 고르세요.", "게임에서 다이렉트 코너 → 그룹 만들기(리더)를 연 뒤 시작을 누르세요."),
        new("lgpe", "레츠고 피카츄·이브이", "시작 → 게임에서 통신 → 근처의 사람과 교환 → 링크 코드 피카츄×3 → 검색 화면에서 기다리세요.", "게임에서 통신 → 근처의 사람과 교환 → 링크 코드(아무거나)로 검색 화면을 연 뒤 시작을 누르세요."),
        new("swsh", "소드·실드", "시작 → 게임에서 Y통신 → 링크 교환 → 로컬 통신 → 두 메시지 모두 A → 검색 화면에서 기다리세요.", "게임에서 Y통신 → 링크 교환 → 로컬 통신(코드 없이)으로 검색을 연 뒤 시작을 누르세요."),
        new("bdsp", "브릴리언트다이아몬드·샤이닝펄", "시작 → 포켓몬센터 2층 왼쪽 접수원 → '예'(암호·그룹 아님) → 유니언룸에서 Y → 통신 메뉴 → 포켓몬 교환.", "포켓몬센터 2층 왼쪽 접수원 → '예' → 유니언룸 벽에서 떨어져 기다린 뒤 시작. 캐릭터가 나타나면 Y → 포켓몬 교환."),
        new("pla", "레전드 아르세우스", "시작 → 게임에서 코트키 마을 시마 → 교환 → 근처의 사람과 → 코드 00000000 입력.", "게임에서 코트키 마을 시마 → 교환 → 근처의 사람과 → 아무 코드나 입력해 검색을 연 뒤 시작을 누르세요."),
        new("sv", "스칼렛·바이올렛", "시작 → 게임에서 X → 포케포털 → 링크 교환 → 오프라인, 코드 없이 → 검색.", "게임에서 X → 포케포털 → 링크 교환 → 오프라인으로 검색(코드 없이 또는 아무 코드)을 연 뒤 시작을 누르세요."),
        new("za", "레전드 Z-A", "시작 → 게임에서 링크 교환 → 로컬 통신 → 코드 00000000으로 검색.", "게임에서 링크 교환 → 로컬 통신 → 아무 코드로 검색을 연 뒤 시작을 누르세요."),
    ];

    public record ModeInfo(string Key, string Name, string Start, string Games);
    public static readonly ModeInfo[] Modes =
    [
        new("trade", "포켓몬 교환", "교환 시작", "frlg lgpe swsh bdsp pla sv za"),
        new("evolve", "교환 진화 도우미 (게임이 내민 포켓몬을 그대로 돌려보내 진화)", "진화 도우미 시작", "lgpe swsh"),
        new("gift", "이상한 소포 보내기", "소포 보내기 시작", "frlg swsh"),
        new("save_backup", "세이브 백업 (콘솔 → 휴대폰)", "백업 시작", "frlg"),
        new("save_restore", "세이브 복원 (휴대폰 → 콘솔)", "복원 시작", "frlg"),
    ];
    public const string FrlgMenu = "타이틀 화면에서 이상한 소포(Mystery Gift) → 이상한 카드(Wonder Cards) → 친구(Friend)를 열고";

    public static string KeyOf(SaveFile sav) => sav switch
    {
        SAV3FRLG => "frlg", SAV7b => "lgpe", SAV8SWSH => "swsh", SAV8BS => "bdsp", SAV8LA => "pla", SAV9SV => "sv", SAV9ZA => "za", _ => null,
    };

    /// <summary>prod.keys 형식 확인. 문제가 없으면 null.</summary>
    public static string CheckKeys(byte[] data)
    {
        string text;
        try { text = new UTF8Encoding(false, true).GetString(data); }
        catch { return "텍스트 파일이 아니에요. Lockpick_RCM이 만든 prod.keys(메모장으로 열면 'master_key_00 = …' 같은 줄이 보이는 파일)를 골라 주세요."; }
        foreach (var need in new[] { "aes_kek_generation_source", "aes_key_generation_source", "master_key_00" })
            if (!text.Contains(need)) return $"필요한 키({need})가 없어요. 커펌 스위치에서 Lockpick_RCM으로 뽑은 prod.keys를 골라 주세요.";
        return null;
    }
    public static bool HasKeys => File.Exists(KeysPath) && CheckKeys(File.ReadAllBytes(KeysPath)) == null;

    /// <summary>교환 화면 열기 (mons: 보낼 목록을 이것으로 바꿈, null이면 그대로).</summary>
    public static Task Open(IReadOnlyList<PKM> mons)
    {
        if (!Available) throw new PlatformNotSupportedException("이 빌드에는 PokeLDN 교환이 없습니다 (LDN판에서 사용)");
        if (mons != null)
        {
            Offers.Clear();
            var game = mons.Count > 0 ? LdnExport.GameOf(mons[0])?.Game : null;
            foreach (var p in mons.Take(6)) if (LdnExport.GameOf(p)?.Game == game) Offers.Add(p.Clone());
        }
        SheetHost.Show(new LdnSheet());
        return Task.CompletedTask;
    }

    /// <summary>받은 포켓몬 파일들 → 박스 빈 칸에 넣을지 물어봄.</summary>
    public static async Task ImportReceived(IReadOnlyList<string> files)
    {
        var page = MainPage.Instance; if (page == null || AppState.Sav == null) return;
        var mons = new List<PKM>();
        foreach (var f in files)
            try
            {
                var pk = EntityFormat.GetFromBytes(await File.ReadAllBytesAsync(f), EntityFileExtension.GetContextFromExtension(f));
                if (pk != null && pk.Species != 0) mons.Add(pk);
            }
            catch { }
        if (mons.Count == 0) return;
        var fit = mons.Select(AppState.ForSave).Where(p => p != null).ToList();
        if (fit.Count == 0) { Note.Show($"받은 포켓몬 {mons.Count}마리는 지금 세이브에 넣을 수 없습니다"); return; }
        var names = string.Join(", ", fit.Take(6).Select(p => PKHeX.Core.GameInfo.Strings.Species[p.Species]));
        if (!await page.DisplayAlertAsync("PokeLDN 교환", $"받은 포켓몬 {fit.Count}마리를 박스 빈 칸에 넣을까요?\n{names}", "넣기", "나중에")) return;
        var sav = AppState.Sav; int placed = 0;
        for (int b = 0; b < sav.BoxCount && placed < fit.Count; b++)
        {
            int box = (AppState.Box + b) % sav.BoxCount;
            for (int i = 0; i < sav.BoxSlotCount && placed < fit.Count; i++)
            {
                if (sav.GetBoxSlotAtIndex(box, i).Species != 0) continue;
                sav.SetBoxSlotAtIndex(fit[placed++], box, i);
            }
        }
        AppState.NotifyBox();
        Note.Show(placed < fit.Count ? $"{placed}마리 넣음 (빈 칸 부족)" : $"{placed}마리를 박스에 넣었습니다 (세이브 저장 필요)");
    }

    /// <summary>받은 포켓몬 (최근 것부터).</summary>
    public static List<FileInfo> ReceivedFiles(string game)
    {
        var d = Path.Combine(Home, "received", game);
        return Directory.Exists(d) ? new DirectoryInfo(d).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).ToList() : [];
    }

    public static List<FileInfo> SavedBackups()
    {
        var d = Path.Combine(Home, "saves");
        return Directory.Exists(d) ? new DirectoryInfo(d).GetFiles("*.sav").OrderByDescending(f => f.LastWriteTimeUtc).ToList() : [];
    }

    /// <summary>이상한 소포 목록 (앱에 들어 있는 gifts/게임.json: key, group, label, note).</summary>
    public static List<(string Key, string Group, string Label, string Note)> Gifts(string game)
    {
        var list = new List<(string, string, string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(Bridge.Asset($"gifts/{game}.json"));
            foreach (var e in doc.RootElement.EnumerateArray())
                list.Add((e.GetProperty("key").GetString(), e.TryGetProperty("group", out var g) ? g.GetString() : "", e.GetProperty("label").GetString(), e.TryGetProperty("note", out var n) ? n.GetString() : ""));
        }
        catch { }
        return list;
    }

    /// <summary>AAR 안의 Java 입구(org.pkhexkr.ldntrade.LdnBridge)를 JNI로 부름.</summary>
    public static class Bridge
    {
#if WITH_LDN
        private static IntPtr cls;
        private static IntPtr Cls
        {
            get
            {
                if (cls != IntPtr.Zero) return cls;
                using var c = Java.Lang.Class.ForName("org.pkhexkr.ldntrade.LdnBridge", true, Android.App.Application.Context.ClassLoader);
                cls = Android.Runtime.JNIEnv.NewGlobalRef(c.Handle);
                return cls;
            }
        }
        private static IntPtr M(string name, string sig) => Android.Runtime.JNIEnv.GetStaticMethodID(Cls, name, sig);
        private static Android.Runtime.JValue Ctx => new(Android.App.Application.Context);
        private static string Str(IntPtr h) => Android.Runtime.JNIEnv.GetString(h, Android.Runtime.JniHandleOwnership.TransferLocalRef) ?? "";
        private const string C = "Landroid/content/Context;", S = "Ljava/lang/String;";

        public static string UsbState() => Str(Android.Runtime.JNIEnv.CallStaticObjectMethod(Cls, M("usbState", $"({C}){S}"), Ctx));
        public static void RequestUsb() => Android.Runtime.JNIEnv.CallStaticVoidMethod(Cls, M("requestUsbPermission", $"({C})V"), Ctx);
        public static bool Busy => Android.Runtime.JNIEnv.CallStaticBooleanMethod(Cls, M("isBusy", "()Z"));
        public static string Poll() => Str(Android.Runtime.JNIEnv.CallStaticObjectMethod(Cls, M("poll", $"(){S}")));
        public static void Cancel() => Android.Runtime.JNIEnv.CallStaticVoidMethod(Cls, M("cancel", "()V"));
        public static string Asset(string name)
        {
            var s = Android.Runtime.JNIEnv.NewString(name);
            try { return Str(Android.Runtime.JNIEnv.CallStaticObjectMethod(Cls, M("asset", $"({C}{S}){S}"), Ctx, new Android.Runtime.JValue(s))); }
            finally { Android.Runtime.JNIEnv.DeleteLocalRef(s); }
        }
        public static bool Start(string request, bool ble)
        {
            var s = Android.Runtime.JNIEnv.NewString(request);
            try { return Android.Runtime.JNIEnv.CallStaticBooleanMethod(Cls, M("start", $"({C}{S}Z)Z"), Ctx, new Android.Runtime.JValue(s), new Android.Runtime.JValue(ble)); }
            finally { Android.Runtime.JNIEnv.DeleteLocalRef(s); }
        }
        public static bool Flash(bool bleFirmware) => Android.Runtime.JNIEnv.CallStaticBooleanMethod(Cls, M("flash", $"({C}Z)Z"), Ctx, new Android.Runtime.JValue(bleFirmware));
#else
        public static string UsbState() => "none";
        public static void RequestUsb() { }
        public static bool Busy => false;
        public static string Poll() => "{\"lines\":[],\"status\":[],\"done\":true}";
        public static void Cancel() { }
        public static string Asset(string name) => "";
        public static bool Start(string request, bool ble) => false;
        public static bool Flash(bool bleFirmware) => false;
#endif
    }

    // ===== 실행 중인 작업 (시트를 닫아도 계속) =====
    public static readonly StringBuilder FullLog = new();
    public static readonly List<string> StatusLines = [];
    public static string BoardLine = "", ProgressLine = "";
    public static bool Running { get; private set; }
    public static string RunMode { get; private set; }
    public static string RunGame { get; private set; }
    public static event Action Changed;
    private static IDispatcherTimer timer;

    public static bool Begin(string request, string mode, string game, bool ble, bool flash = false)
    {
        if (Running || Bridge.Busy) return false;
        FullLog.Clear(); StatusLines.Clear(); BoardLine = ""; ProgressLine = "";
        bool ok = flash ? Bridge.Flash(ble) : Bridge.Start(request, ble);
        if (!ok) return false;
        Running = true; RunMode = flash ? "flash" : mode; RunGame = game;
        timer ??= Application.Current.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(400);
        timer.Tick -= Tick; timer.Tick += Tick;
        timer.Start();
        DeviceDisplay.Current.KeepScreenOn = true;
        Changed?.Invoke();
        return true;
    }

    private static async void Tick(object s, EventArgs e)
    {
        string json;
        try { json = Bridge.Poll(); } catch { return; }
        bool done; string result = null, error = null;
        using (var doc = JsonDocument.Parse(json))
        {
            var r = doc.RootElement;
            foreach (var l in r.GetProperty("lines").EnumerateArray()) FullLog.Append(l.GetString()).Append('\n');
            if (FullLog.Length > 80000) FullLog.Remove(0, FullLog.Length - 60000);
            foreach (var st in r.GetProperty("status").EnumerateArray())
            {
                var t = st.GetString();
                if (t.StartsWith("@board:")) BoardLine = t[7..];
                else if (t.StartsWith("@progress:")) ProgressLine = t[10..];
                else if (StatusLines.Count == 0 || StatusLines[^1] != t) StatusLines.Add(t);
            }
            done = r.GetProperty("done").GetBoolean();
            if (r.TryGetProperty("result", out var rr)) result = rr.GetString();
            if (r.TryGetProperty("error", out var er)) error = er.GetString();
        }
        if (!done) { Changed?.Invoke(); return; }
        timer.Stop(); Running = false; DeviceDisplay.Current.KeepScreenOn = false;
        var files = new List<string>();
        if (result != null) try { using var rd = JsonDocument.Parse(result); files = rd.RootElement.EnumerateArray().Select(x => x.GetString()).Where(x => x != null).ToList(); } catch { }
        if (error != null) StatusLines.Add(error.StartsWith("중지") || error.StartsWith("설치 실패") ? error : "실패: " + error);
        else if (RunMode == "trade") StatusLines.Add(files.Count > 0 ? $"받은 포켓몬 {files.Count}마리" : "받은 포켓몬이 없어요");
        else if (RunMode == "save_backup") StatusLines.Add(files.Count > 0 ? "세이브를 백업했어요: " + Path.GetFileName(files[0]) : "백업한 세이브가 없어요");
        Changed?.Invoke();
        if (error != null) return;
        if (RunMode == "trade" && files.Count > 0)
        {
            int n = Math.Min(files.Count, Offers.Count);
            if (n > 0) Offers.RemoveRange(0, n);   // 교환을 마친 포켓몬은 보낼 목록에서 뺌
            Changed?.Invoke();
            await ImportReceived(files);
        }
        else if (RunMode == "save_backup" && files.Count > 0 && MainPage.Instance is { } page
                 && await page.DisplayAlertAsync("세이브 백업", "받은 세이브를 지금 열까요?\n" + Path.GetFileName(files[0]), "열기", "나중에"))
            await page.OpenBytes(await File.ReadAllBytesAsync(files[0]), Path.GetFileName(files[0]));
    }

    public static void Stop() { if (Running) Bridge.Cancel(); }
}
