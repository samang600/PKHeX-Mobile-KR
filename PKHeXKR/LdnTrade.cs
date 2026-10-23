using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using PKHeX.Core;

namespace PKHeXKR;

/// <summary>
/// PokeLDN 교환 (ESP32 보드, LDN판 빌드). Android는 AAR/Chaquopy, iOS는 내장 CPython/CoreBluetooth BLE 경로를 사용.
/// 화면은 이 앱 안(LdnSheet)에서. 요청·진행 로그·결과 파일은 공통 LdnTrade 인터페이스로 전달.
/// 파일은 앱 전용 폴더의 ldn/ 아래 (prod.keys, 받은 포켓몬, 세이브 백업, 파이썬 데이터).
/// </summary>
public static class LdnTrade
{
#if WITH_LDN
    public static bool Available => OperatingSystem.IsAndroidVersionAtLeast(26);   // 안드로이드 8 이상 (보드 USB 읽기 API)
#elif WITH_POKELDN_IOS
    public static bool Available => OperatingSystem.IsIOS();
#else
    public static bool Available => false;
#endif
    public static string Home { get { var d = Path.Combine(FileSystem.AppDataDirectory, "ldn"); Directory.CreateDirectory(d); return d; } }
    public static string KeysPath => Path.Combine(Home, "prod.keys");
    public const string FwVersion = "1.7.1";

    /// <summary>교환할 포켓몬 (보통 최대 6, 리빙덱스 교환은 수백 마리). 시트를 닫아도 유지.</summary>
    public static readonly List<PKM> Offers = [];

    // ───── 리빙덱스 교환 (커펌 없이 콘솔 세이브 채우기) ─────
    /// <summary>보낼 목록이 리빙덱스 대기열이면 그 게임 (교환을 마칠 때마다 대기열 파일도 줄임 → 다음에 이어서).</summary>
    public static string QueueGame;
    private static string QueueDir(string game) => Path.Combine(Home, "queue", game);
    public static int QueueCount(string game) { try { var d = QueueDir(game); return Directory.Exists(d) ? Directory.GetFiles(d).Length : 0; } catch { return 0; } }

    public static void SaveQueue()
    {
        if (QueueGame == null) return;
        var d = QueueDir(QueueGame);
        try
        {
            if (Directory.Exists(d)) Directory.Delete(d, true);
            if (Offers.Count == 0) { QueueGame = null; return; }
            Directory.CreateDirectory(d);
            for (int i = 0; i < Offers.Count; i++) File.WriteAllBytes(Path.Combine(d, $"{i:0000}.{Offers[i].Extension}"), ShareUtil.Stored(Offers[i]));
        }
        catch { }
    }

    public static int LoadQueue(string game)
    {
        var d = QueueDir(game); if (!Directory.Exists(d)) return 0;
        var list = new List<PKM>();
        foreach (var f in Directory.GetFiles(d).OrderBy(x => x))
            try { if (EntityFormat.GetFromBytes(File.ReadAllBytes(f), EntityFileExtension.GetContextFromExtension(f)) is { Species: > 0 } pk) list.Add(pk); } catch { }
        Offers.Clear(); Offers.AddRange(list); QueueGame = game;
        return list.Count;
    }

    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>보낼 목록의 한 마리: 편집기로 불러오기 · 편집 중인 포켓몬으로 바꾸기 · 빼기.</summary>
    public static async Task OfferActions(int i, Action closeAll)
    {
        if (i < 0 || i >= Offers.Count) return;
        var page = Application.Current.Windows[0].Page; var p = Offers[i];
        const string load = "편집기로 불러오기 (확인·편집)", put = "편집 중인 포켓몬으로 바꾸기", del = "목록에서 빼기";
        var pick = await page.DisplayActionSheetAsync($"{i + 1}. {GameInfo.Strings.Species[p.Species]} Lv.{p.CurrentLevel}{(p.IsShiny ? " ★" : "")}", "취소", null, load, put, del);
        switch (pick)
        {
            case load:
                if (AppState.Load(p.Clone())) { Note.Show($"교환 목록 {i + 1}번을 편집기로 불러왔어요 · 고친 뒤 '편집 중인 포켓몬으로 바꾸기'로 목록에 반영"); closeAll?.Invoke(); }
                break;
            case put:
                var cur = AppState.Pk;
                if (cur == null || cur.Species == 0) { Note.Show("편집 중인 포켓몬이 없어요"); return; }
                if (LdnExport.GameOf(cur)?.Game != LdnExport.GameOf(p)?.Game) { Note.Show("같은 게임 형식의 포켓몬만 바꿀 수 있어요"); return; }
                Offers[i] = cur.Clone(); if (QueueGame != null) SaveQueue(); Changed?.Invoke(); Note.Show($"{i + 1}번을 편집 중인 포켓몬으로 바꿨어요");
                break;
            case del:
                Offers.RemoveAt(i); if (QueueGame != null) SaveQueue(); Changed?.Invoke();
                break;
        }
    }

    public static void DropQueue(string game) { try { var d = QueueDir(game); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { } if (QueueGame == game) QueueGame = null; }

    /// <summary>
    /// 리빙덱스를 만들어 보낼 목록(대기열)으로. 지금 세이브가 그 게임이면 그 트레이너·박스 기준(이미 있는 종 건너뛰기), 아니면 빈 세이브.
    /// 세이브 사본의 빈 박스에 채워서 꺼내므로 지금 세이브는 바뀌지 않음.
    /// </summary>
    public static int BuildLivingDex(string game, LivingDexOptions o, CancellationToken tok, Action<int, int, ushort> progress)
    {
        var cur = AppState.Sav;
        SaveFile baseSav = cur != null && KeyOf(cur) == game ? cur : BlankFor(game);
        if (baseSav == null) return 0;
        var have = new HashSet<(ushort, byte)>();
        if (o.SkipExisting) for (int b = 0; b < baseSav.BoxCount; b++) foreach (var p in baseSav.GetBoxData(b)) if (p.Species != 0) have.Add((p.Species, o.Forms ? p.Form : (byte)0));
        var tmp = baseSav.Clone();
        var blank = tmp.BlankPKM;
        var made = new List<PKM>(); var got = new HashSet<(ushort, byte)>();
        for (int round = 0; round < 4 && !tok.IsCancellationRequested; round++)
        {
            for (int b = 0; b < tmp.BoxCount; b++) for (int i = 0; i < tmp.BoxSlotCount; i++) tmp.SetBoxSlotAtIndex(blank, b, i);
            var opt = new LivingDexOptions
            {
                Shiny = o.Shiny, Forms = o.Forms, Alpha = o.Alpha, Transfer = o.Transfer, OTs = o.OTs, Presets = o.Presets, From = o.From, To = o.To,
                SkipExisting = false, StartBox = 0,
                SpeciesFilter = s => o.Forms || (!have.Contains((s, 0)) && !got.Contains((s, 0))),
            };
            var r = LivingDexGen.Run(tmp, opt, tok, progress);
            int before = made.Count;
            for (int b = 0; b < tmp.BoxCount; b++)
                foreach (var p in tmp.GetBoxData(b))
                {
                    if (p.Species == 0) continue;
                    var key = (p.Species, o.Forms ? p.Form : (byte)0);
                    if (have.Contains(key) || got.Contains(key)) continue;
                    got.Add(key); made.Add(p);
                }
            if (!r.BoxFull || made.Count == before) break;   // 박스가 가득 차서 멈췄으면 남은 종으로 한 번 더
        }
        Offers.Clear(); Offers.AddRange(made); QueueGame = game; SaveQueue();
        return made.Count;
    }

    public static SaveFile BlankFor(string game)
    {
        var v = game switch { "frlg" => GameVersion.FR, "lgpe" => GameVersion.GP, "swsh" => GameVersion.SW, "bdsp" => GameVersion.BD, "pla" => GameVersion.PLA, "sv" => GameVersion.SL, "za" => GameVersion.ZA, _ => GameVersion.Any };
        if (v == GameVersion.Any) return null;
        try { return BlankSaveFile.Get(v, "PKHeX", LanguageID.Korean); } catch { return null; }
    }

    public record LdnGame(string Key, string Name, string HostGuide, string JoinGuide);
    public static readonly LdnGame[] Games =
    [
        new("frlg", "파이어레드·리프그린", "시작을 누른 뒤 게임에서 무선 통신 → 다이렉트 코너 → 그룹에 참가 → 이 앱의 트레이너를 고르세요.", "게임에서 다이렉트 코너 → 그룹 만들기(리더)를 연 뒤 시작을 누르세요."),
        new("lgpe", "레츠고 피카츄·이브이", "시작 → 게임에서 통신 → 근처의 사람과 교환 → 링크 코드 피카츄×3 → 검색 화면에서 기다리세요.", "게임에서 통신 → 근처의 사람과 교환 → 링크 코드(아무거나)로 검색 화면을 연 뒤 시작을 누르세요."),
        new("swsh", "소드·실드", "시작 → 게임에서 Y통신 → 링크 교환 → 로컬 통신 → 두 메시지 모두 A → 검색 화면에서 기다리세요.", "게임에서 Y통신 → 링크 교환 → 로컬 통신(코드 없이)으로 검색을 연 뒤 시작을 누르세요."),
        new("bdsp", "브릴리언트다이아몬드·샤이닝펄", "시작 → 포켓몬센터 2층 왼쪽 접수원 → '예'(암호·그룹 아님) → 유니언룸에서 Y → 통신 메뉴 → 포켓몬 교환.", "포켓몬센터 2층 왼쪽 접수원 → '예' → 유니언룸 벽에서 떨어져 기다린 뒤 시작. 캐릭터가 나타나면 Y → 포켓몬 교환."),
        new("pla", "레전드 아르세우스", "시작 → 게임에서 축복마을의 사문 → 교환 → 근처의 사람과 → 코드 00000000 입력.", "게임에서 축복마을의 사문 → 교환 → 근처의 사람과 → 아무 코드나 입력해 검색을 연 뒤 시작을 누르세요."),
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
        // Opening the configuration/list screen must not initialize embedded Python or
        // start CoreBluetooth. The runtime is initialized when the user starts a job.
        // This keeps the screen usable without an ESP32 and avoids an unhandled native
        // initialization failure during navigation.
        if (mons != null)
        {
            Offers.Clear(); QueueGame = null;   // 리빙덱스 대기열 파일은 남겨 둠 (다음에 '이어서 보내기')
            var game = mons.Count > 0 ? LdnExport.GameOf(mons[0])?.Game : null;
            foreach (var p in mons) if (LdnExport.GameOf(p)?.Game == game) Offers.Add(p.Clone());
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

    /// <summary>Android AAR/JNI 또는 iOS 네이티브 BLE·CPython 호스트를 공통 인터페이스로 부름.</summary>
    public static class Bridge
    {
#if WITH_LDN
        public static string Error { get; private set; } = "";
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
        public static string RadioState() => "";
        /// <summary>"native" 내장 USB, "cp210x" 클래식 ESP32, "" 없음.</summary>
        public static string UsbKind() { try { return Str(Android.Runtime.JNIEnv.CallStaticObjectMethod(Cls, M("usbKind", $"({C}){S}"), Ctx)); } catch { return ""; } }
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
            Error = "";
            var s = Android.Runtime.JNIEnv.NewString(request);
            try { return Android.Runtime.JNIEnv.CallStaticBooleanMethod(Cls, M("start", $"({C}{S}Z)Z"), Ctx, new Android.Runtime.JValue(s), new Android.Runtime.JValue(ble)); }
            finally { Android.Runtime.JNIEnv.DeleteLocalRef(s); }
        }
        public static bool Flash(bool bleFirmware) => Android.Runtime.JNIEnv.CallStaticBooleanMethod(Cls, M("flash", $"({C}Z)Z"), Ctx, new Android.Runtime.JValue(bleFirmware));
#elif WITH_POKELDN_IOS
        [DllImport("@rpath/PokeLdnRuntime.framework/PokeLdnRuntime", EntryPoint = "pkhex_pokeldn_init")]
        [return: MarshalAs(UnmanagedType.I1)] private static extern bool NativeInit();
        [DllImport("@rpath/PokeLdnRuntime.framework/PokeLdnRuntime", EntryPoint = "pkhex_pokeldn_error")]
        private static extern IntPtr NativeError();
        [DllImport("@rpath/PokeLdnRuntime.framework/PokeLdnRuntime", EntryPoint = "pkhex_pokeldn_start")]
        [return: MarshalAs(UnmanagedType.I1)] private static extern bool NativeStart(IntPtr requestUtf8);
        [DllImport("@rpath/PokeLdnRuntime.framework/PokeLdnRuntime", EntryPoint = "pkhex_pokeldn_poll")]
        private static extern IntPtr NativePoll();
        [DllImport("@rpath/PokeLdnRuntime.framework/PokeLdnRuntime", EntryPoint = "pkhex_pokeldn_busy")]
        [return: MarshalAs(UnmanagedType.I1)] private static extern bool NativeBusy();
        [DllImport("@rpath/PokeLdnRuntime.framework/PokeLdnRuntime", EntryPoint = "pkhex_pokeldn_cancel")]
        private static extern void NativeCancel();
        [DllImport("@rpath/PokeLdnRuntime.framework/PokeLdnRuntime", EntryPoint = "pkhex_pokeldn_radio_state")]
        private static extern IntPtr NativeRadioState();
        [DllImport("@rpath/PokeLdnRuntime.framework/PokeLdnRuntime", EntryPoint = "pkhex_pokeldn_free")]
        private static extern void NativeFree(IntPtr value);

        private static bool initialized;
        public static string Error { get; private set; } = "";
        public static bool Initialize()
        {
            if (initialized) return true;
            Error = "";
            try
            {
                initialized = NativeInit();
                if (!initialized)
                {
                    var ptr = NativeError();
                    Error = ptr == IntPtr.Zero ? "네이티브 Python 초기화가 실패했습니다 (상세 오류 없음)." : Marshal.PtrToStringUTF8(ptr) ?? "네이티브 Python 초기화가 실패했습니다.";
                    if (ptr != IntPtr.Zero) NativeFree(ptr);
                }
            }
            catch (Exception ex)
            {
                initialized = false;
                Error = $"{ex.GetType().Name}: {ex.Message}";
            }
            return initialized;
        }
        public static string UsbState() => "none";
        public static string UsbKind() => "";
        public static string RadioState()
        {
            if (!initialized) return "off";
            var ptr = NativeRadioState();
            if (ptr == IntPtr.Zero) return "off";
            try { return Marshal.PtrToStringUTF8(ptr) ?? "off"; }
            finally { NativeFree(ptr); }
        }
        public static void RequestUsb() { }
        public static bool Busy => initialized && NativeBusy();
        public static string Poll()
        {
            if (!initialized) return "{\"lines\":[],\"status\":[],\"done\":true}";
            var ptr = NativePoll();
            if (ptr == IntPtr.Zero) return "{\"lines\":[],\"status\":[],\"done\":true}";
            try { return Marshal.PtrToStringUTF8(ptr) ?? "{\"lines\":[],\"status\":[],\"done\":true}"; }
            finally { NativeFree(ptr); }
        }
        public static void Cancel() { if (initialized) NativeCancel(); }
        public static string Asset(string name)
        {
            var file = Path.GetFileName(name);
            using var stream = typeof(LdnTrade).Assembly.GetManifestResourceStream("PKHeXKR.PokeLDN." + file);
            if (stream == null) return "";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        public static bool Start(string request, bool ble)
        {
            if (!ble) { Error = "iOS에서는 Bluetooth LE 연결이 필요합니다."; return false; }
            if (!Initialize()) return false;
            var ptr = Marshal.StringToCoTaskMemUTF8(request);
            try
            {
                bool ok = NativeStart(ptr);
                if (!ok) Error = "PokeLDN Python 작업이 시작을 거부했습니다. 이전 작업이 남아 있는지 확인하세요.";
                return ok;
            }
            catch (Exception ex) { Error = $"{ex.GetType().Name}: {ex.Message}"; return false; }
            finally { Marshal.FreeCoTaskMem(ptr); }
        }
        public static bool Flash(bool bleFirmware) => false;
#else
        public static bool Initialize() => false;
        public static string Error => "이 빌드에는 iOS PokeLDN 런타임이 없습니다.";
        public static string UsbState() => "none";
        public static string UsbKind() => "";
        public static string RadioState() => "off";
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
    private static BgTask bg;

    public static bool Begin(string request, string mode, string game, bool ble, bool flash = false)
    {
        if (Running || Bridge.Busy) return false;
        FullLog.Clear(); StatusLines.Clear(); BoardLine = ""; ProgressLine = "";
        bool ok = flash ? Bridge.Flash(ble) : Bridge.Start(request, ble);
        if (!ok) return false;
        Running = true; RunMode = flash ? "flash" : mode; RunGame = game;
        bg?.Dispose();
        bg = BgTask.Begin(flash ? "보드 펌웨어 설치" : mode switch { "trade" => $"PokeLDN 교환 ({Offers.Count}마리)", "evolve" => "PokeLDN 교환 진화", "gift" => "PokeLDN 이상한 소포", "save_backup" => "FRLG 세이브 백업", "save_restore" => "FRLG 세이브 복원", _ => "PokeLDN" });
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
        if (!done) { bg?.Report(ProgressLine.Length > 0 ? ProgressLine : StatusLines.LastOrDefault() ?? "진행 중…"); Changed?.Invoke(); return; }
        timer.Stop(); Running = false; DeviceDisplay.Current.KeepScreenOn = false;
        var files = new List<string>();
        if (result != null) try { using var rd = JsonDocument.Parse(result); files = rd.RootElement.EnumerateArray().Select(x => x.GetString()).Where(x => x != null).ToList(); } catch { }
        if (error != null) StatusLines.Add(error.StartsWith("중지") || error.StartsWith("설치 실패") ? error : "실패: " + error);
        else if (RunMode == "trade") StatusLines.Add(files.Count > 0 ? $"받은 포켓몬 {files.Count}마리" : "받은 포켓몬이 없어요");
        else if (RunMode == "save_backup") StatusLines.Add(files.Count > 0 ? "세이브를 백업했어요: " + Path.GetFileName(files[0]) : "백업한 세이브가 없어요");
        bg?.Done(StatusLines.LastOrDefault() ?? "끝"); bg = null;
        Changed?.Invoke();
        if (error != null) return;
        if (RunMode == "trade" && files.Count > 0)
        {
            int n = Math.Min(files.Count, Offers.Count);
            if (n > 0) Offers.RemoveRange(0, n);   // 교환을 마친 포켓몬은 보낼 목록에서 뺌
            if (QueueGame == RunGame) SaveQueue();   // 리빙덱스 대기열: 남은 것만 (다음에 이어서)
            Changed?.Invoke();
            await ImportReceived(files);
        }
        else if (RunMode == "save_backup" && files.Count > 0 && MainPage.Instance is { } page
                 && await page.DisplayAlertAsync("세이브 백업", "받은 세이브를 지금 열까요?\n" + Path.GetFileName(files[0]), "열기", "나중에"))
            await page.OpenBytes(await File.ReadAllBytesAsync(files[0]), Path.GetFileName(files[0]));
    }

    public static Task StopAsync() => Running ? Task.Run(Bridge.Cancel) : Task.CompletedTask;
}
