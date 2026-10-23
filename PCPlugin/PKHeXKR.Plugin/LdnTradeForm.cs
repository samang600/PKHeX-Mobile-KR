using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PKHeX.Core;

namespace PKHeXKR.Plugin;

/// <summary>
/// PokeLDN 교환: PC에 꽂은 ESP32 보드로 스위치 포켓몬 게임과 로컬 통신 교환.
/// 실제 통신은 PokeLDN(github.com/Decryptu/pokeldn)이 하고, 이 창은 데스크톱 앱과 같은 프리셋으로 PokeLDN을 실행
/// (pokeldn.exe --module pokeldn 도구 -- 포켓몬 파일)한 뒤 받은 포켓몬을 PKHeX로 가져옴.
/// prod.keys·보드 펌웨어·받은 폴더는 PokeLDN 데스크톱 앱 설정(%AppData%\pokeldn\settings.json)을 그대로 씀.
/// </summary>
public sealed class LdnTradeForm : Form
{
    // 게임 키, 이름, 확장자, 호스트 도구, 참가 도구, 포켓몬 인자(호스트), 포켓몬 인자(참가), 다음 포켓몬 인자(호스트)
    private sealed record Game(string Key, string Name, string Ext, string Host, string Join, string HostFlag, string JoinFlag, string HostMore,
                               string HostGuide, string JoinGuide);
    private static readonly Game[] Games =
    [
        new("frlg", "파이어레드·리프그린", "pk3", "frlg-trade-host", "frlg-trade-join", "", "", "",
            "시작 → 게임에서 무선 통신 → 다이렉트 코너 → 그룹에 참가 → PokeLDN 트레이너 선택", "게임에서 다이렉트 코너 → 그룹 만들기를 연 뒤 시작"),
        new("lgpe", "레츠고 피카츄·이브이", "pb7", "lgpe-host", "lgpe-join", "--offer", "--offer", "--next-offer",
            "시작 → 통신 → 근처의 사람과 교환 → 링크 코드 피카츄×3 → 검색 화면에서 대기", "링크 코드 피카츄×3으로 검색 화면을 연 뒤 시작"),
        new("swsh", "소드·실드", "pk8", "swsh-host", "swsh-join", "--offer-file", "--offer-file", "--offer-file",
            "시작 → Y통신 → 링크 교환 → 로컬 통신 → 두 메시지 모두 A → 검색 화면에서 대기", "Y통신 → 링크 교환 → 로컬 통신으로 검색을 연 뒤 시작"),
        new("bdsp", "브릴리언트다이아몬드·샤이닝펄", "pb8", "bdsp-host", "bdsp-join", "--offer", "--trade-template", "--offer",
            "시작 → 포켓몬센터 2층 왼쪽 접수원 → '예' → 유니언룸에서 Y → 통신 메뉴 → 포켓몬 교환", "유니언룸 벽에서 떨어져 기다린 뒤 시작, 캐릭터가 나타나면 Y → 포켓몬 교환"),
        new("pla", "레전드 아르세우스", "pa8", "pla-host", "pla-join", "--trade-box-record", "--offer", "--trade-box-record",
            "시작 → 축복마을의 사문 → 교환 → 근처의 사람과 → 코드 00000000", "근처 교환 검색을 연 뒤 시작"),
        new("sv", "스칼렛·바이올렛", "pk9", "sv-host", "sv-join", "--trade-offer", "--trade-offer", "--trade-offer",
            "시작 → X → 포케포털 → 링크 교환 → 오프라인, 코드 없이 → 검색", "링크 교환 검색(오프라인, 코드 없음)을 연 뒤 시작"),
        new("za", "레전드 Z-A", "pa9", "za-host", "za-join", "--trade-offer", "--trade-offer", "--trade-offer",
            "시작 → 링크 교환 → 로컬 통신 → 코드 00000000으로 검색", "링크 교환 → 로컬 통신 → 코드 00000000으로 검색을 연 뒤 시작"),
    ];

    private readonly KRToolsPlugin plugin;
    private readonly TextBox path = new() { Dock = DockStyle.Fill }, port = new() { Width = 90, PlaceholderText = "자동" };
    private readonly ComboBox game = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox lang = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly CheckBox skipLegal = new() { Text = "합법성 확인 건너뛰기 (PKHeX 판정이 불법이어도 그대로 보냄)", AutoSize = true };
    private readonly RadioButton host = new() { Text = "기다리기 (호스트)", AutoSize = true }, join = new() { Text = "찾아 들어가기 (참가)", AutoSize = true };
    private readonly Label guide = new() { AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = SystemColors.GrayText };
    private readonly ListBox offers = new() { Height = 96, Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly ListBox received = new() { Height = 80, Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };
    private readonly Button start = new() { Text = "교환 시작", AutoSize = true }, stop = new() { Text = "중지", AutoSize = true, Enabled = false };
    private readonly List<string> offerFiles = [];
    private readonly string work = Path.Combine(Path.GetTempPath(), "pkhexkr_ldn");
    private Process proc;
    private DateTime startedAt;
    private readonly System.Windows.Forms.Timer watch = new() { Interval = 2000 };

    public LdnTradeForm(KRToolsPlugin plugin)
    {
        this.plugin = plugin;
        Text = "PokeLDN 교환 (ESP32 보드)"; Size = new Size(720, 760); Font = new Font("Malgun Gothic", 9.5f); MinimumSize = new Size(600, 600);
        Directory.CreateDirectory(work);
        var st = PluginSettings.Current;
        path.Text = st.LdnPath; port.Text = st.LdnPort;
        (st.LdnHost ? host : join).Checked = true;
        skipLegal.Checked = st.LdnSkipLegality;
        foreach (var g in Games) game.Items.Add(g.Name);
        foreach (var (_, name) in LdnLanguage.Languages) lang.Items.Add(name);
        lang.SelectedIndex = Math.Max(0, Array.FindIndex(LdnLanguage.Languages, l => l.Id == st.LdnLanguage));
        game.SelectedIndex = Math.Max(0, Array.FindIndex(Games, g => g.Key == KeyOf(plugin.SaveFileEditor.SAV)));
        game.SelectedIndexChanged += (_, _) => UpdateGuide();
        host.CheckedChanged += (_, _) => UpdateGuide();

        var browse = new Button { Text = "찾기…", AutoSize = true };
        browse.Click += (_, _) => Browse();
        var pathRow = Row(new Label { Text = "PokeLDN 위치", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, path, browse);
        pathRow.ColumnStyles.Clear();
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var help = new Label { AutoSize = true, MaximumSize = new Size(660, 0), ForeColor = SystemColors.GrayText,
            Text = "PokeLDN 릴리스(pokeldn-windows-x64.zip)를 풀고 pokeldn.exe를 고르세요. prod.keys, 받은 폴더, 보드 펌웨어는 PokeLDN 앱에서 한 번 설정하면 여기서도 그대로 씁니다. 소스로 쓰는 경우 저장소 폴더를 고르면 됩니다(.venv 파이썬 사용)." };

        var gameRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        gameRow.Controls.AddRange([new Label { Text = "게임", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, game, host, join,
            new Label { Text = "보드 포트", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, port,
            new Label { Text = "트레이너 국적", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, lang]);
        var langHelp = new Label { AutoSize = true, MaximumSize = new Size(660, 0), ForeColor = SystemColors.GrayText,
            Text = "트레이너 이름은 PokeLDN 앱 설정의 이름을 씁니다. 한글 이름이면 국적을 한국어로 두세요 (SV·Z-A·BDSP에 적용, 소드실드·레츠고는 콘솔 언어를 따름, 아르세우스는 미적용, 파이어레드·리프그린은 한글 불가)." };

        var addCur = new Button { Text = "편집 중인 포켓몬 추가", AutoSize = true };
        addCur.Click += (_, _) => AddPkm(plugin.PKMEditor.PreparePKM());
        var addFile = new Button { Text = "파일 추가…", AutoSize = true };
        addFile.Click += (_, _) => AddFiles();
        var clear = new Button { Text = "비우기", AutoSize = true };
        clear.Click += (_, _) => { offerFiles.Clear(); RefreshOffers(); };
        var offerBtns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        offerBtns.Controls.AddRange([addCur, addFile, clear]);

        var toEditor = new Button { Text = "편집기로 열기", AutoSize = true };
        toEditor.Click += (_, _) => { if (Selected() is { } pk) plugin.PKMEditor.PopulateFields(pk); };
        var toBox = new Button { Text = "박스 빈 칸에 넣기", AutoSize = true };
        toBox.Click += (_, _) => PutInBox();
        var recBtns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        recBtns.Controls.AddRange([toEditor, toBox]);

        start.Click += (_, _) => Start();
        stop.Click += (_, _) => Stop();
        var runRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        runRow.Controls.AddRange([start, stop]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        void Add(Control c, SizeType t = SizeType.AutoSize, float h = 0) { layout.RowStyles.Add(new RowStyle(t, h)); layout.Controls.Add(c); }
        Add(pathRow); Add(help); Add(gameRow); Add(langHelp); Add(guide);
        Add(new Label { Text = "보낼 포켓몬 (최대 6마리, 순서대로 교환)", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 8, 0, 2) });
        Add(offers, SizeType.Absolute, 100); Add(offerBtns); Add(skipLegal); Add(runRow);
        Add(new Label { Text = "받은 포켓몬", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 8, 0, 2) });
        Add(received, SizeType.Absolute, 84); Add(recBtns);
        Add(new Label { Text = "진행 상황", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(0, 8, 0, 2) });
        Add(log, SizeType.Percent, 100);
        Controls.Add(layout);

        watch.Tick += (_, _) => RefreshReceived();
        FormClosing += (_, _) => { Stop(); SaveSettings(); };
        UpdateGuide(); RefreshOffers();
    }

    private static TableLayoutPanel Row(params Control[] cs)
    {
        var t = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = cs.Length, RowCount = 1 };
        foreach (var c in cs) t.Controls.Add(c);
        return t;
    }

    private static string KeyOf(SaveFile sav) => sav switch
    {
        SAV3FRLG => "frlg", SAV7b => "lgpe", SAV8BS => "bdsp", SAV8SWSH => "swsh", SAV8LA => "pla", SAV9SV => "sv", SAV9ZA => "za", _ => "",
    };

    private Game Cur => Games[game.SelectedIndex];

    private void UpdateGuide() => guide.Text = (host.Checked ? Cur.HostGuide : Cur.JoinGuide)
        + (KeyOf(plugin.SaveFileEditor.SAV) is var k && k != "" && k != Cur.Key ? "\n※ 지금 열린 세이브와 다른 게임입니다." : "");

    private void SaveSettings()
    {
        var st = PluginSettings.Current;
        st.LdnPath = path.Text.Trim(); st.LdnPort = port.Text.Trim(); st.LdnHost = host.Checked;
        st.LdnLanguage = LdnLanguage.Languages[Math.Max(0, lang.SelectedIndex)].Id;
        st.LdnSkipLegality = skipLegal.Checked;
        st.Save();
    }

    private void Browse()
    {
        using var d = new OpenFileDialog { Title = "pokeldn.exe 고르기 (소스 폴더라면 그 안의 아무 파일이나)", Filter = "PokeLDN|pokeldn.exe|모든 파일|*.*" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var f = d.FileName;
        path.Text = Path.GetFileName(f).Equals("pokeldn.exe", StringComparison.OrdinalIgnoreCase) ? f : FindSourceRoot(Path.GetDirectoryName(f)) ?? f;
        SaveSettings();
    }

    private static string FindSourceRoot(string dir)
    {
        for (var d = dir; d != null; d = Path.GetDirectoryName(d))
            if (File.Exists(Path.Combine(d, "pokeldn", "__main__.py"))) return d;
        return null;
    }

    // ===== 보낼 포켓몬 =====
    private void AddPkm(PKM pk)
    {
        if (pk == null || pk.Species == 0) { MessageBox.Show("편집 중인 포켓몬이 없습니다.", Text); return; }
        if (offerFiles.Count >= 6) { MessageBox.Show("최대 6마리까지 넣을 수 있습니다.", Text); return; }
        if (!skipLegal.Checked && !new LegalityAnalysis(pk).Valid)
            Append("※ 합법이 아닌 포켓몬입니다. '합법성 확인 건너뛰기'를 켜지 않으면 PokeLDN이 교환 전에 거절합니다.");
        var data = new byte[pk.SIZE_STORED]; pk.WriteDecryptedDataStored(data);
        var file = Path.Combine(work, $"{DateTime.Now:HHmmssfff}_{pk.Species:0000}.{pk.Extension}");
        File.WriteAllBytes(file, data);
        offerFiles.Add(file);
        var idx = Array.FindIndex(Games, g => g.Ext == pk.Extension);
        if (idx >= 0) game.SelectedIndex = idx;
        RefreshOffers();
    }

    private void AddFiles()
    {
        using var d = new OpenFileDialog { Multiselect = true, Title = "보낼 포켓몬 파일", Filter = "포켓몬|*.pk3;*.pb7;*.pb8;*.pk8;*.pa8;*.pk9;*.pa9|모든 파일|*.*" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        foreach (var f in d.FileNames)
        {
            if (offerFiles.Count >= 6) break;
            offerFiles.Add(f);
        }
        var idx = Array.FindIndex(Games, g => offerFiles.Count > 0 && offerFiles[^1].EndsWith("." + g.Ext, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) game.SelectedIndex = idx;
        RefreshOffers();
    }

    private void RefreshOffers()
    {
        offers.Items.Clear();
        for (int i = 0; i < offerFiles.Count; i++)
        {
            var f = offerFiles[i];
            string name = Path.GetFileName(f);
            try
            {
                var pk = EntityFormat.GetFromBytes(File.ReadAllBytes(f), EntityFileExtension.GetContextFromExtension(f));
                if (pk != null) name = $"{GameInfo.Strings.Species[pk.Species]} Lv.{pk.CurrentLevel}{(pk.IsShiny ? " ★" : "")}  ({Path.GetFileName(f)})";
            }
            catch { }
            if (!f.EndsWith("." + Cur.Ext, StringComparison.OrdinalIgnoreCase)) name += $"  ⚠ {Cur.Name} 형식(.{Cur.Ext})이 아님";
            offers.Items.Add($"{i + 1}. {name}");
        }
    }

    // ===== 실행 =====
    private (string exe, string args, string cwd)? Command(string tool, IEnumerable<string> extra)
    {
        var p = path.Text.Trim().Trim('"');
        var rest = string.Join(" ", extra.Select(Quote));
        var run = skipLegal.Checked ? RunScript() : null;   // 합법성 확인 건너뛰기: PokeLDN 안에서 확인 부분만 바꿔 실행
        if (File.Exists(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return (p, run != null ? $"--run {Quote(run)} {tool} -- {rest}" : $"--module pokeldn {tool} -- {rest}", Path.GetDirectoryName(p));
        var root = Directory.Exists(p) ? FindSourceRoot(p) ?? p : null;
        if (root != null && File.Exists(Path.Combine(root, "pokeldn", "__main__.py")))
        {
            var py = new[] { Path.Combine(root, ".venv", "Scripts", "python.exe"), Path.Combine(root, ".venv", "bin", "python") }.FirstOrDefault(File.Exists) ?? "python";
            return (py, run != null ? $"-u {Quote(Path.Combine(root, "pokeldn", "app", "entry.py"))} --run {Quote(run)} {tool} -- {rest}" : $"-u -m pokeldn {tool} -- {rest}", root);
        }
        return null;
    }

    private string RunScript()
    {
        try
        {
            using var src = typeof(LdnTradeForm).Assembly.GetManifestResourceStream("pkhexkr_ldn_run.py");
            if (src == null) return null;
            var file = Path.Combine(work, "pkhexkr_ldn_run.py");
            using (var dst = File.Create(file)) src.CopyTo(dst);
            return file;
        }
        catch { return null; }
    }

    private static string Quote(string s) => s.Length > 0 && !s.Contains(' ') && !s.Contains('"') ? s : "\"" + s.Replace("\"", "\\\"") + "\"";

    private void Start()
    {
        if (proc != null) return;
        if (offerFiles.Count == 0) { MessageBox.Show("보낼 포켓몬을 추가하세요.", Text); return; }
        var g = Cur; bool isHost = host.Checked;
        var flag = isHost ? g.HostFlag : g.JoinFlag;
        var extra = new List<string>();
        for (int i = 0; i < offerFiles.Count; i++)
        {
            var f = isHost && i > 0 && g.HostMore != "" ? g.HostMore : flag;
            if (f != "") extra.Add(f);
            extra.Add(offerFiles[i]);
        }
        if (g.Key == "frlg") { extra.Add("--trades"); extra.Add(offerFiles.Count.ToString()); }
        string langNote;
        try { extra.AddRange(LdnLanguage.Args(g.Key, LdnLanguage.Languages[Math.Max(0, lang.SelectedIndex)].Id, path.Text, work, out langNote)); }
        catch (Exception ex) { langNote = "국적 설정 실패: " + ex.Message; }
        var cmd = Command(isHost ? g.Host : g.Join, extra);
        if (cmd == null) { MessageBox.Show("PokeLDN 위치를 먼저 지정하세요 (pokeldn.exe 또는 소스 폴더).", Text); return; }
        SaveSettings();
        var psi = new ProcessStartInfo(cmd.Value.exe, cmd.Value.args)
        {
            WorkingDirectory = cmd.Value.cwd, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        psi.Environment["POKELDN_RADIO"] = "esp32:" + (port.Text.Trim() is { Length: > 0 } pt ? pt : "auto");
        psi.Environment["POKELDN_MANAGED_RUN"] = "1";   // 중지 = 입력 닫기 → PokeLDN이 정리하고 끝냄
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        log.Clear();
        if (langNote != "") Append("※ " + langNote);
        Append($"> {Path.GetFileName(cmd.Value.exe)} {cmd.Value.args}");
        try
        {
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
            proc.Exited += (_, _) => { try { if (!IsDisposed && IsHandleCreated) BeginInvoke(() => { if (!IsDisposed) Finished(); }); } catch { } };   // 창을 닫은 뒤 pokeldn이 끝나도 PKHeX가 죽지 않게
            startedAt = DateTime.Now;
            proc.Start(); proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
            start.Enabled = false; stop.Enabled = true; start.Text = "교환 중…";
            watch.Start();
        }
        catch (Exception ex) { Append("실행 실패: " + ex.Message); proc = null; }
    }

    private void Stop()
    {
        if (proc == null) return;
        try { proc.StandardInput.Close(); } catch { }
        var p = proc;
        Task.Delay(8000).ContinueWith(_ => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        Append("중지하는 중…");
    }

    private void Finished()
    {
        Append($"끝났습니다 (코드 {proc?.ExitCode})");
        proc?.Dispose(); proc = null;
        start.Enabled = true; stop.Enabled = false; start.Text = "교환 시작";
        watch.Stop(); RefreshReceived();
    }

    private void Append(string line)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { try { BeginInvoke(() => Append(line)); } catch { } return; }
        if (log.TextLength > 200_000) log.Text = log.Text[^100_000..];
        log.AppendText(line + Environment.NewLine);
    }

    // ===== 받은 포켓몬 =====
    private static string ReceivedFolder()
    {
        try
        {
            var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pokeldn", "settings.json");
            if (File.Exists(settings) && JsonDocument.Parse(File.ReadAllText(settings)).RootElement.TryGetProperty("received", out var r) && r.GetString() is { Length: > 0 } s)
                return Environment.ExpandEnvironmentVariables(s.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        }
        catch { }
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(Directory.Exists(docs) ? docs : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "pokeldn", "Received");
    }

    private readonly List<string> receivedFiles = [];

    private void RefreshReceived()
    {
        var dir = ReceivedFolder();
        if (!Directory.Exists(dir)) return;
        var found = Directory.GetFiles(dir).Where(f => File.GetLastWriteTime(f) >= startedAt.AddSeconds(-2) && startedAt != default)
            .OrderBy(File.GetLastWriteTime).ToList();
        foreach (var f in found.Where(f => !receivedFiles.Contains(f)))
        {
            receivedFiles.Add(f);
            string name = Path.GetFileName(f);
            var pk = Load(f);
            if (pk != null) name = $"{GameInfo.Strings.Species[pk.Species]} Lv.{pk.CurrentLevel}{(pk.IsShiny ? " ★" : "")}  ({name})";
            received.Items.Add(name);
        }
    }

    private static PKM Load(string f)
    {
        try
        {
            var data = File.ReadAllBytes(f);
            var pk = EntityFormat.GetFromBytes(data, EntityFileExtension.GetContextFromExtension(f));
            return pk is { Species: > 0 } ? pk : null;
        }
        catch { return null; }
    }

    private PKM Selected()
    {
        var i = received.SelectedIndex;
        if (i < 0 || i >= receivedFiles.Count) { MessageBox.Show("받은 포켓몬을 고르세요.", Text); return null; }
        var pk = Load(receivedFiles[i]);
        if (pk == null) { MessageBox.Show("파일을 읽지 못했습니다.", Text); return null; }
        var sav = plugin.SaveFileEditor.SAV;
        var conv = EntityConverter.ConvertToType(pk, sav.PKMType, out var result);
        if (conv == null) { MessageBox.Show($"지금 세이브 형식으로 바꿀 수 없습니다 ({result}).", Text); return null; }
        return conv;
    }

    private void PutInBox()
    {
        if (Selected() is not { } pk) return;
        var sav = plugin.SaveFileEditor.SAV;
        var slot = sav.NextOpenBoxSlot();
        if (slot < 0) { MessageBox.Show("박스에 빈 칸이 없습니다.", Text); return; }
        sav.SetBoxSlotAtIndex(pk, slot);
        plugin.SaveFileEditor.ReloadSlots();
        MessageBox.Show($"박스 {slot / sav.BoxSlotCount + 1} - {slot % sav.BoxSlotCount + 1}번 칸에 넣었습니다. (세이브 내보내기로 저장하세요)", Text);
    }
}
