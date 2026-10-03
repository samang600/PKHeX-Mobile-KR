using System.Text;
using FluentFTP;
using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// 스위치 세이브 (무선 FTP, DBI 'Run FTP server'). 범용 FTP 탐색기:
/// 세이브 폴더가 보이면 바로 가져오기·덮어쓰기, SD 카드만 보이면 DBI 백업 폴더(switch/DBI/saves)로 반자동.
/// </summary>
public class FtpSheet : Sheet
{
    internal static readonly Dictionary<string, string> Titles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["0100A3D008C5C000"] = "스칼렛", ["01008F6008C5E000"] = "바이올렛", ["0100ABF008968000"] = "소드", ["01008DB008C2C000"] = "실드",
        ["0100000011D90000"] = "브릴리언트 다이아몬드", ["010018E011D92000"] = "샤이닝 펄", ["01001F5010DFA000"] = "레전즈 아르세우스",
        ["010003F003A34000"] = "레츠고 피카츄", ["0100187003A36000"] = "레츠고 이브이", ["0100F43008C44000"] = "레전즈 Z-A",
    };
    private static AsyncFtpClient client;
    private static string cwd = "/";
    private readonly Entry hostE = T.Input(placeholder: "스위치 IP (예: 192.168.0.23)"), portE = T.Input(Keyboard.Numeric), userE = T.Input(placeholder: "anonymous"), passE = T.Input(placeholder: "비밀번호 (없으면 비움)");
    private readonly VerticalStackLayout list = new() { Spacing = 4 };
    private readonly Entry pathE = T.Input(placeholder: "/backup/saves");
    private readonly Label pathL = T.L("", 13, sub: true), status = T.L("", 13, sub: true);
    private static string DbiBackup => Preferences.Get("ftp_backup", "/backup/saves");
    private bool busy;

    public FtpSheet() : base("스위치 세이브 (무선 FTP)")
    {
        hostE.Text = Preferences.Get("ftp_host", ""); portE.Text = Preferences.Get("ftp_port", "5000"); userE.Text = Preferences.Get("ftp_user", ""); passE.Text = Preferences.Get("ftp_pass", "");
        passE.IsPassword = true;
        var connect = T.Pill("연결", primary: true); connect.Clicked += async (_, _) => await Connect();
        var up = T.Pill("상위 폴더"); up.Clicked += async (_, _) => await Safe(async () => { if (cwd != "/") { cwd = cwd.TrimEnd('/'); cwd = cwd[..Math.Max(1, cwd.LastIndexOf('/'))]; } await Refresh(); });
        var dbi = T.Pill("백업 폴더"); dbi.Clicked += async (_, _) => await Safe(async () => { cwd = DbiBackup; await Refresh(); });
        var here = T.Pill("이곳을 백업 폴더로"); here.Clicked += (_, _) => { Preferences.Set("ftp_backup", cwd); pathE.Text = cwd; Note.Show("백업 폴더로 지정했습니다"); };
        var off = T.Pill("연결 해제"); off.Clicked += (_, _) => { Drop(); list.Children.Clear(); status.Text = "연결을 해제했습니다"; pathL.Text = ""; };
        pathE.Text = DbiBackup;
        pathE.Completed += (_, _) => { var v = (pathE.Text ?? "").Trim(); if (v.Length == 0) v = "/backup/saves"; if (!v.StartsWith('/')) v = "/" + v.Replace("sdmc:/", ""); Preferences.Set("ftp_backup", v.TrimEnd('/')); pathE.Text = DbiBackup; };
        var root = T.Pill("맨 위"); root.Clicked += async (_, _) => await Safe(async () => { cwd = "/"; await Refresh(); });
        var jksv = T.Pill("JKSV"); jksv.Clicked += async (_, _) => await Safe(async () => { cwd = "/JKSV"; await Refresh(); });
        var ckpt = T.Pill("Checkpoint"); ckpt.Clicked += async (_, _) => await Safe(async () => { cwd = "/switch/Checkpoint/saves"; await Refresh(); });
        var mk = T.Pill("새 폴더"); mk.Clicked += async (_, _) => await Safe(NewFolder);
        pasteBtn = T.Pill("붙여넣기", primary: true); pasteBtn.Clicked += async (_, _) => await Safe(async () => { await Paste(); }); UpdatePaste();
        var hint = T.L("스위치 DBI에서 'Run FTP server'를 켜면 화면에 IP와 포트가 나옵니다(기본 5000). 휴대폰과 스위치가 같은 와이파이에 있어야 합니다.\n• 세이브 폴더가 보이면: 파일을 눌러 바로 열고, 편집 후 같은 자리에 덮어쓸 수 있습니다.\n• SD 카드만 보이면: 스위치 DBI → Browse saves → 게임 → Backup 후, 'DBI 백업 폴더'에서 파일을 열어 편집·덮어쓰기 → 스위치 DBI에서 그 백업으로 복원(Restore).", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap; status.LineBreakMode = LineBreakMode.WordWrap; pathL.LineBreakMode = LineBreakMode.WordWrap;
        var form = T.Cols(2, 6); form.Add(T.Field("IP", hostE), 0); form.Add(T.Field("포트", portE), 1);
        var form2 = T.Cols(2, 6); form2.Add(T.Field("사용자", userE), 0); form2.Add(T.Field("비밀번호", passE), 1);
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, form, form2, connect,
            new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = new HorizontalStackLayout { Spacing = 8, Children = { up, root, dbi, jksv, ckpt, here, mk, pasteBtn, off } } }, T.Field("백업 폴더 경로 (SD 카드 기준, sdmc:/ = /)", pathE), status, pathL, list } } });
        if (client?.IsConnected == true) _ = Safe(Refresh);
    }

    private async Task Connect()
    {
        var host = (hostE.Text ?? "").Trim();
        if (host.Length == 0) { status.Text = "스위치 IP를 입력하세요"; return; }
        int port = int.TryParse(portE.Text, out var p) ? p : 5000;
        Preferences.Set("ftp_host", host); Preferences.Set("ftp_port", port.ToString()); Preferences.Set("ftp_user", userE.Text ?? ""); Preferences.Set("ftp_pass", passE.Text ?? "");
        if (busy) return; busy = true;
        status.Text = "연결 중…";
        try
        {
            Drop();   // 기존 연결은 기다리지 않고 백그라운드에서 정리 (연결 중에 다시 누르면 멈추던 문제)
            var user = string.IsNullOrWhiteSpace(userE.Text) ? "anonymous" : userE.Text.Trim();
            client = new AsyncFtpClient(host, user, passE.Text ?? "", port);
            client.Encoding = Encoding.UTF8;
            client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
            client.Config.ConnectTimeout = 8000; client.Config.ReadTimeout = 15000; client.Config.DataConnectionConnectTimeout = 8000;
            await client.Connect();
            status.Text = $"{host}:{port}에 연결했습니다"; cwd = "/";
            await Refresh();
        }
        catch (Exception ex) { status.Text = "연결 실패: " + ex.Message + " (IP·포트, 같은 와이파이인지, DBI FTP가 켜져 있는지 확인)"; client = null; }
        finally { busy = false; }
    }

    private static void Drop()
    {
        var c = client; client = null;
        if (c != null) _ = Task.Run(async () => { try { await c.Disconnect().WaitAsync(TimeSpan.FromSeconds(3)); } catch { } try { c.Dispose(); } catch { } });
    }

    public record Item(string Name, string Full, bool Dir, long Size, DateTime Modified);
    private static string lastMethod = "";

    /// <summary>
    /// 폴더 내용 읽기. 서버마다 목록 형식이 달라서(DBI 등) 여러 방법을 차례로 시도:
    /// ① 기본 목록(MLSD/LIST) ② LIST 강제 ③ 이름만 받기(NLST) + 폴더인지 하나씩 확인.
    /// 종류를 알 수 없는 항목은 실제로 들어가 보며 폴더 여부를 확인.
    /// </summary>
    private static async Task<List<Item>> ListDir(string path)
    {
        string Join(string n) => n.StartsWith('/') ? n : path.TrimEnd('/') + "/" + n;
        async Task<List<Item>> FromListing(FtpListOption opt, string how)
        {
            bool noPath = opt.HasFlag(FtpListOption.NoPath);
            var raw = await client.GetListing(path, opt);
            var list = new List<Item>();
            foreach (var i in raw)
            {
                if (i.Name is "." or ".." || string.IsNullOrEmpty(i.Name)) continue;
                var full = noPath || string.IsNullOrEmpty(i.FullName) ? Join(i.Name) : i.FullName;   // 현재 폴더 기준 목록이면 경로를 직접 붙임
                bool dir = i.Type == FtpObjectType.Directory;
                if (i.Type != FtpObjectType.Directory && i.Type != FtpObjectType.File) { try { dir = await client.DirectoryExists(full); } catch { } }
                list.Add(new Item(i.Name, full, dir, i.Size, i.Modified));
            }
            if (list.Count > 0) lastMethod = how;
            return list;
        }
        List<Item> r = [];
        // DBI FTP: "LIST 경로"에는 빈 목록을 주고, 폴더로 이동(CWD)한 뒤 "LIST"(경로 없이)에만 답함 → 이 방식을 먼저
        try { await client.SetWorkingDirectory(path); r = await FromListing(FtpListOption.ForceList | FtpListOption.AllFiles | FtpListOption.NoPath, "폴더 이동 후 LIST"); } catch { }
        if (r.Count == 0) try { r = await FromListing(FtpListOption.AllFiles, "기본 목록"); } catch { }
        if (r.Count == 0) try { r = await FromListing(FtpListOption.ForceList | FtpListOption.AllFiles, "LIST"); } catch { }
        if (r.Count == 0)
        {
            try
            {
                foreach (var n0 in await client.GetNameListing(path))
                {
                    var name = n0.TrimEnd('/'); name = name[(name.LastIndexOf('/') + 1)..];
                    if (name is "" or "." or "..") continue;
                    var full = Join(name); bool dir = false; long size = 0;
                    try { dir = await client.DirectoryExists(full); } catch { }
                    if (!dir) try { size = await client.GetFileSize(full); } catch { }
                    r.Add(new Item(name, full, dir, size, default));
                }
                if (r.Count > 0) lastMethod = "이름 목록(NLST)";
            }
            catch { }
        }
        return r.OrderByDescending(x => x.Dir).ThenBy(x => x.Name).ToList();
    }

    private static (string Dir, string Name) Split(string path) { var i = path.TrimEnd('/').LastIndexOf('/'); return i <= 0 ? ("/", path.Trim('/')) : (path[..i], path[(i + 1)..]); }
    private static async Task<byte[]> Get(string path)
    {
        try { var b = await client.DownloadBytes(path, CancellationToken.None); if (b != null && b.Length > 0) return b; } catch { }
        var (dir, name) = Split(path);
        await client.SetWorkingDirectory(dir);
        return await client.DownloadBytes(name, CancellationToken.None);
    }
    private static async Task<FtpStatus> Put(byte[] data, string path)
    {
        FtpStatus r = FtpStatus.Failed;
        try { r = await client.UploadBytes(data, path, FtpRemoteExists.Overwrite, false, null, CancellationToken.None); } catch { }
        if (r == FtpStatus.Success) return r;
        var (dir, name) = Split(path);
        await client.SetWorkingDirectory(dir);
        return await client.UploadBytes(data, name, FtpRemoteExists.Overwrite, false, null, CancellationToken.None);
    }

    private async Task Refresh()
    {
        list.Children.Clear();
        if (client?.IsConnected != true) { status.Text = "연결되어 있지 않습니다"; return; }
        await client.GetWorkingDirectory().WaitAsync(TimeSpan.FromSeconds(10));   // 살아 있는지 확인
        pathL.Text = "위치: " + cwd;
        try
        {
            status.Text = "목록 읽는 중…";
            var items = await ListDir(cwd);
            status.Text = items.Count > 0 ? $"{items.Count}개 · 읽은 방식: {lastMethod}" : "";
            if (items.Count == 0)
            {
                var empty = T.L(cwd.StartsWith(DbiBackup, StringComparison.OrdinalIgnoreCase) ? "(비어 있음) 스위치 DBI에서 먼저 세이브를 Backup 하세요" : "(비어 있음)", 13, sub: true);
                var diag = T.Pill("목록 진단", size: 12);
                diag.Clicked += async (_, _) => await Diagnose();
                list.Children.Add(empty); list.Children.Add(diag);
            }
            foreach (var it in items)
            {
                bool dir = it.Dir;
                var game = FtpSheet.Titles.FirstOrDefault(t => it.Name.Contains(t.Key, StringComparison.OrdinalIgnoreCase)).Value;
                bool star = dir && (it.Name.Contains("save", StringComparison.OrdinalIgnoreCase) || it.Name.Contains("Pok", StringComparison.OrdinalIgnoreCase) || it.Name.Contains("포켓몬") || game != null);
                var label = (dir ? "📁 " : "📄 ") + (star ? "★ " : "") + it.Name + (game != null ? $"  ({game})" : "");
                var path = it.Full;
                list.Children.Add(RowM(it, label, dir ? "폴더" : $"{it.Size:N0} 바이트{(it.Modified != default ? $" · {it.Modified:yyyy-MM-dd HH:mm}" : "")}", async () =>
                {
                    if (dir) { cwd = path; await Refresh(); }
                    else await OnFile(path, it.Name, it.Size);
                }));
            }
        }
        catch (Exception ex) when (!IsNet(ex)) { status.Text = "목록 오류: " + ex.Message; }
    }

    /// <summary>목록이 비었을 때 원인 확인: 방법별 결과와 서버 응답을 그대로 보여줌.</summary>
    private async Task Diagnose()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"위치: {cwd}");
        try { sb.AppendLine($"폴더 존재: {await client.DirectoryExists(cwd)}"); } catch (Exception ex) { sb.AppendLine("폴더 확인 오류: " + ex.Message); }
        foreach (var (opt, name) in new[] { (FtpListOption.AllFiles, "기본"), (FtpListOption.ForceList | FtpListOption.AllFiles, "LIST"), (FtpListOption.ForceList | FtpListOption.AllFiles | FtpListOption.NoPath, "LIST(경로 없이)") })
        {
            try { var l = await client.GetListing(cwd, opt); sb.AppendLine($"{name}: {l.Length}개 " + string.Join(", ", l.Take(8).Select(x => $"{x.Name}[{x.Type}]"))); }
            catch (Exception ex) { sb.AppendLine($"{name}: 오류 {ex.Message}"); }
        }
        try { var n = await client.GetNameListing(cwd); sb.AppendLine($"NLST: {n.Length}개 " + string.Join(", ", n.Take(8))); } catch (Exception ex) { sb.AppendLine("NLST: 오류 " + ex.Message); }
        try { var r = await client.Execute("STAT " + cwd); sb.AppendLine($"STAT: {r.Code} {r.Message} {string.Join(" | ", (r.InfoMessages ?? "").Split('\n').Take(8))}"); } catch (Exception ex) { sb.AppendLine("STAT: 오류 " + ex.Message); }
        try { sb.AppendLine("서버: " + client.ServerType + " / " + client.SystemType); } catch { }
        await Application.Current.Windows[0].Page.DisplayAlertAsync("목록 진단 (이 화면을 캡처해 알려주세요)", sb.ToString(), "확인");
    }

    private View RowM(Item it, string text, string sub, Func<Task> tap) => Row(text, sub, tap, () => ItemMenu(it));
    private View Row(string text, string sub, Func<Task> tap, Func<Task> menu = null)
    {
        var t1 = T.L(text, 14, bold: true); t1.LineBreakMode = LineBreakMode.WordWrap;
        var v = new VerticalStackLayout { Spacing = 1, Padding = new Thickness(10, 8), Children = { t1, T.L(sub, 11, sub: true) } };
        var b = new Border { Content = v, StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
        b.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F9FAFB"), Color.FromArgb("#22262F"));
        var g = new TapGestureRecognizer(); g.Tapped += async (_, _) => await Safe(tap); b.GestureRecognizers.Add(g);
        if (menu == null) return b;
        // 꾹 누르기 = 메뉴, 오른쪽 ⋯ 버튼도 같은 메뉴
        b.Behaviors.Add(new CommunityToolkit.Maui.Behaviors.TouchBehavior { LongPressDuration = 500, LongPressCommand = new Command(async () => await Safe(menu)) });
        var more = new Button { Text = "⋯", FontSize = 16, Padding = 0, WidthRequest = 36, HeightRequest = 36, BackgroundColor = Colors.Transparent, BorderWidth = 0, TextColor = T.Accent, VerticalOptions = LayoutOptions.Center };
        more.Clicked += async (_, _) => await Safe(menu);
        var row = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 2 };
        row.Add(b, 0); row.Add(more, 1);
        return row;
    }

    private async Task OnFile(string path, string name, long size)
    {
        var page = Application.Current.Windows[0].Page;
        bool zip = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        const string open = "앱에서 열기", keep = "앱에 복사해 두기", put = "편집 중인 세이브로 덮어쓰기";
        var pick = await page.DisplayActionSheetAsync(name + (zip ? " (압축 세이브)" : ""), "취소", null, open, keep, put);
        try
        {
            switch (pick)
            {
                case open:
                case keep:
                    status.Text = "가져오는 중…";
                    var data = await Get(path) ?? throw new IOException("받지 못했습니다");
                    SaveStore.Backup(data, path.Trim('/').Replace('/', '_'), "스위치FTP");
                    if (pick == keep) { status.Text = $"{data.Length:N0} 바이트를 앱 백업 목록에 저장했습니다"; break; }
                    if (zip && ZipSave.Extract(data) is not { } z0) { status.Text = "zip 안에서 세이브 파일을 찾지 못했습니다"; break; }
                    Close();
                    await MainPage.Instance.OpenBytes(data, name);   // zip이면 안의 세이브(main 등)를 꺼내 엶
                    Note.Show("스위치 세이브를 열었습니다 (원본은 백업 목록에 보관)");
                    break;
                case put:
                    var sav = AppState.Sav;
                    if (string.IsNullOrEmpty(sav.Metadata.FileName)) { status.Text = "앱에 열린 세이브가 없습니다"; break; }
                    if (!await page.DisplayAlertAsync("덮어쓰기", $"'{path}'을(를) 지금 편집 중인 {AppState.GameName(sav.Version)} 세이브로 바꿉니다.{(zip ? " zip은 원래 구성 그대로 다시 묶고 세이브 파일만 바꿉니다." : "")} 원본은 먼저 앱 백업 목록에 저장합니다. 계속할까요?", "덮어쓰기", "취소")) break;
                    status.Text = "원본 백업 중…";
                    var orig = await Get(path);
                    if (orig != null) SaveStore.Backup(orig, path.Trim('/').Replace('/', '_'), "스위치FTP 원본");
                    var bytes = sav.Write(sav.Metadata.GetSuggestedFlags(sav.Metadata.GetSuggestedExtension())).ToArray();
                    int origLen = orig?.Length ?? 0;
                    if (zip)
                    {
                        // zip: 원래 묶음에서 세이브 파일만 교체 (다른 파일·폴더 경로·압축 방식은 그대로)
                        if (orig == null || ZipSave.Extract(orig) is not { } ze) { status.Text = "원본 zip에서 세이브 파일을 찾지 못해 올리지 않았습니다"; break; }
                        origLen = ze.Data.Length;
                        if (bytes.Length != origLen && !await page.DisplayAlertAsync("크기 다름", $"zip 속 원본 세이브 {origLen:N0} 바이트, 새 세이브 {bytes.Length:N0} 바이트로 다릅니다. 다른 게임일 수 있습니다. 그래도 올릴까요?", "올리기", "취소")) { status.Text = "취소했습니다"; break; }
                        var rebuilt = ZipSave.Replace(orig, ze.Entry, bytes);
                        if (ZipSave.Extract(rebuilt) is not { } chk || !chk.Data.AsSpan().SequenceEqual(bytes)) { status.Text = "zip을 다시 묶는 데 실패해 올리지 않았습니다"; break; }
                        bytes = rebuilt;
                    }
                    else if (orig != null && bytes.Length != orig.Length && !await page.DisplayAlertAsync("크기 다름", $"원본 {orig.Length:N0} 바이트, 새 세이브 {bytes.Length:N0} 바이트로 크기가 다릅니다. 다른 게임이나 파일일 수 있습니다. 그래도 올릴까요?", "올리기", "취소")) { status.Text = "취소했습니다"; break; }
                    status.Text = "올리는 중…";
                    var r = await Put(bytes, path);
                    if (r != FtpStatus.Success) { status.Text = "올리지 못했습니다 (" + r + ")"; Note.Show("업로드 실패: " + name); break; }
                    Note.Show($"업로드 완료: {name} ({bytes.Length:N0}바이트)");
                    status.Text = zip || path.StartsWith(DbiBackup, StringComparison.OrdinalIgnoreCase)
                        ? "백업에 덮어썼습니다. 이제 스위치에서 이 백업으로 복원(DBI: Browse saves → Backups → Restore / JKSV·Checkpoint: Restore)하세요."
                        : "덮어썼습니다. 게임을 실행해 확인하세요 (문제가 있으면 백업 목록의 '스위치FTP 원본'으로 되돌리세요).";
                    await Refresh();
                    break;
            }
        }
        catch (Exception ex) { status.Text = "오류: " + ex.Message; }
    }

    // ===== 탐색기 기능: 새 폴더, 복사·잘라내기·붙여넣기·이름 바꾸기·삭제 =====
    private static (string Path, string Name, bool Dir, bool Cut)? clip;
    private static readonly SemaphoreSlim opLock = new(1, 1);
    private static bool IsNet(Exception ex) => ex is TimeoutException or IOException or System.Net.Sockets.SocketException or ObjectDisposedException or FluentFTP.Exceptions.FtpException || ex.InnerException is { } inner && IsNet(inner);

    /// <summary>
    /// 스위치와 주고받는 작업은 모두 여기로: 한 번에 하나만(겹치면 무시), 30초 넘으면 끊긴 것으로 보고 정리.
    /// 연결이 끊긴 상태에서 폴더를 눌러도 화면이 멈추거나 앱이 꺼지지 않게 함.
    /// </summary>
    private async Task Safe(Func<Task> op)
    {
        if (!await opLock.WaitAsync(0)) { status.Text = "이전 작업이 끝나기를 기다리는 중입니다…"; return; }
        try
        {
            if (client == null || !client.IsConnected) { status.Text = "연결되어 있지 않습니다. '연결'을 눌러 주세요"; return; }
            await op().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex) when (IsNet(ex)) { Lost(ex is TimeoutException ? "스위치가 응답하지 않습니다 (연결이 끊긴 것 같습니다). 다시 연결해 주세요" : "연결이 끊겼습니다. 다시 연결해 주세요 (" + ex.Message + ")"); }
        catch (Exception ex) { status.Text = "오류: " + ex.Message; }
        finally { opLock.Release(); }
    }
    private void Lost(string msg)
    {
        var c = client; client = null;
        status.Text = msg; Note.Show("FTP 연결이 끊겼습니다");
        if (c != null) _ = Task.Run(() => { try { c.Dispose(); } catch { } });
    }
    private Button pasteBtn;
    private static string Join(string dir, string name) => dir.TrimEnd('/') + "/" + name;

    private async Task NewFolder()
    {
        var name = await Application.Current.Windows[0].Page.DisplayPromptAsync("새 폴더", $"{cwd} 안에 만들 폴더 이름", "만들기", "취소");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            bool ok = false;
            try { ok = await client.CreateDirectory(Join(cwd, name.Trim())); } catch { }
            if (!ok) { await client.SetWorkingDirectory(cwd); ok = await client.CreateDirectory(name.Trim()); }
            status.Text = ok ? "폴더를 만들었습니다" : "폴더를 만들지 못했습니다"; Note.Show(ok ? $"폴더 만들기 완료: {name.Trim()}" : "폴더를 만들지 못했습니다");
        }
        catch (Exception ex) when (!IsNet(ex)) { status.Text = "폴더 만들기 오류: " + ex.Message; }
        await Refresh();
    }

    private async Task ItemMenu(Item it)
    {
        var page = Application.Current.Windows[0].Page;
        var pick = await page.DisplayActionSheetAsync(it.Name, "취소", "삭제", "복사", "잘라내기", "이름 바꾸기");
        try
        {
            switch (pick)
            {
                case "복사": clip = (it.Full, it.Name, it.Dir, false); UpdatePaste(); status.Text = $"'{it.Name}' 복사됨 · 붙여넣을 폴더로 가서 '붙여넣기'"; break;
                case "잘라내기": clip = (it.Full, it.Name, it.Dir, true); UpdatePaste(); status.Text = $"'{it.Name}' 잘라냄 · 옮길 폴더로 가서 '붙여넣기'"; break;
                case "이름 바꾸기":
                    var nn = await page.DisplayPromptAsync("이름 바꾸기", it.Name, "바꾸기", "취소", initialValue: it.Name);
                    if (string.IsNullOrWhiteSpace(nn) || nn == it.Name) break;
                    await Rename(it.Full, Join(Split(it.Full).Dir, nn.Trim()));
                    status.Text = "이름을 바꿨습니다"; await Refresh(); break;
                case "삭제":
                    if (!await page.DisplayAlertAsync("삭제", $"'{it.Name}'{(it.Dir ? " 폴더와 그 안의 모든 파일" : "")}을(를) 스위치에서 지웁니다. 되돌릴 수 없습니다.", "삭제", "취소")) break;
                    if (it.Dir) await client.DeleteDirectory(it.Full); else await client.DeleteFile(it.Full);
                    status.Text = "지웠습니다"; await Refresh(); break;
            }
        }
        catch (Exception ex) { status.Text = "오류: " + ex.Message; }
    }

    private static async Task Rename(string from, string to)
    {
        try { await client.Rename(from, to); return; } catch { }
        var (d, n) = Split(from); await client.SetWorkingDirectory(d); await client.Rename(n, to);
    }

    private void UpdatePaste() { if (pasteBtn != null) { pasteBtn.IsVisible = clip != null; pasteBtn.Text = clip is { } c ? $"붙여넣기 ({c.Name})" : "붙여넣기"; } }

    private async Task Paste()
    {
        if (clip is not { } c) return;
        var dest = Join(cwd, c.Name);
        if (dest == c.Path) { status.Text = "같은 위치입니다"; return; }
        try
        {
            status.Text = "붙여넣는 중…";
            if (c.Cut) { await Rename(c.Path, dest); clip = null; }
            else if (c.Dir) await CopyDir(c.Path, dest);
            else { var b = await Get(c.Path); await Put(b, dest); }
            UpdatePaste(); status.Text = "붙여넣었습니다"; Note.Show($"붙여넣기 완료: {c.Name}");
        }
        catch (Exception ex) when (!IsNet(ex)) { status.Text = "붙여넣기 오류: " + ex.Message; }
        await Refresh();
    }

    private static async Task CopyDir(string from, string to)
    {
        try { await client.CreateDirectory(to); } catch { }
        foreach (var it in await ListDir(from))
        {
            if (it.Dir) await CopyDir(it.Full, Join(to, it.Name));
            else { var b = await Get(it.Full); await Put(b, Join(to, it.Name)); }
        }
    }

}
