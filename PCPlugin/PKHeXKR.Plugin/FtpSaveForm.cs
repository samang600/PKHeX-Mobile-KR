using System.Drawing;
using System.Reflection;
using PKHeX.Core;

namespace PKHeXKR.Plugin;

/// <summary>
/// 스위치 세이브 바로 고치기 (ftpsrv): 세이브를 꺼내 PKHeX로 열고, 고친 뒤 스위치에 다시 넣음. 카드·JKSV로 옮길 필요 없음.
/// 안전장치: 받을 때·넣기 직전 세이브를 PC에 남김, 넣은 뒤 다시 받아 비교, 다르면 넣기 전 세이브로 되돌림.
/// </summary>
public sealed class FtpSaveForm : Form
{
    private readonly KRToolsPlugin host;
    private readonly TextBox ip = new() { Width = 130 }, port = new() { Width = 56 }, user = new() { Width = 90 }, pass = new() { Width = 90, UseSystemPasswordChar = true };
    private readonly Button find = new() { Text = "연결 · 세이브 찾기", AutoSize = true };
    private readonly Button load = new() { Text = "PKHeX로 불러오기", AutoSize = true, Enabled = false };
    private readonly Button store = new() { Text = "스위치에 저장", AutoSize = true, Enabled = false };
    private readonly Button folder = new() { Text = "백업 폴더", AutoSize = true };
    private readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = SystemColors.Window };
    private SaveFtp ftp;
    private bool busy;
    /// <summary>불러온 세이브: 어디서 받았는지, 받은 그대로의 내용, 세이브 종류</summary>
    private (FtpSave from, byte[] raw, Type type)? loaded;

    public FtpSaveForm(KRToolsPlugin host)
    {
        this.host = host;
        Text = "스위치 세이브 바로 고치기 (ftpsrv)"; Size = new Size(640, 520); MinimumSize = new Size(560, 420); Font = new Font("Malgun Gothic", 9.5f);
        var st = PluginSettings.Current;
        ip.Text = st.FtpHost; port.Text = st.FtpPort.ToString(); user.Text = st.FtpUser; pass.Text = st.FtpPass;

        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(600, 0), Padding = new Padding(0, 0, 0, 6),
            Text = "스위치(커스텀 펌웨어)의 ftpsrv에 연결해 세이브를 꺼내 고치고 다시 넣습니다.\n" +
                   "· 그 게임은 꺼 둔 상태여야 합니다 (켜 둔 채 넣으면 게임이 다시 덮어씁니다).\n" +
                   "· ftpsrv config.ini [Nx] 에 mount_devices=1, 넣으려면 save_writable=1 과 세이브 쓰기를 고친 ftpsrv가 필요합니다.\n" +
                   "· 받을 때와 넣기 직전 세이브는 백업 폴더에 남습니다.",
        };
        var conn = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        void Add(string label, Control c) { conn.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }); conn.Controls.Add(c); }
        Add("스위치 IP", ip); Add("포트", port); Add("사용자", user); Add("비밀번호", pass); conn.Controls.Add(find);
        var btns = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        btns.Controls.AddRange([load, store, folder]);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); grid.RowStyles.Add(new RowStyle(SizeType.AutoSize)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        grid.Controls.Add(hint); grid.Controls.Add(conn); grid.Controls.Add(list); grid.Controls.Add(btns); grid.Controls.Add(log);
        Controls.Add(grid);

        find.Click += (_, _) => Find();
        load.Click += (_, _) => LoadSelected();
        store.Click += (_, _) => StoreCurrent();
        folder.Click += (_, _) => { try { Directory.CreateDirectory(BackupDir); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(BackupDir) { UseShellExecute = true }); } catch (Exception ex) { Say("폴더를 열지 못했습니다: " + ex.Message); } };
        list.SelectedIndexChanged += (_, _) => Buttons();
        list.DoubleClick += (_, _) => { if (load.Enabled) LoadSelected(); };
        Say("스위치 IP를 넣고 「연결 · 세이브 찾기」를 누르세요. (IP는 스위치 설정 › 인터넷 › 연결 상태에서 확인)");
    }

    private static string BackupDir => Path.Combine(Path.GetDirectoryName(PluginSettings.SavedPath ?? Environment.ProcessPath) ?? ".", "ftpsrv_backup");
    private void Say(string s) { if (IsDisposed) return; log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s.Replace("\n", "\r\n          ")}\r\n"); }
    private void Buttons()
    {
        find.Enabled = !busy;
        load.Enabled = !busy && list.SelectedItem is FtpSave;
        store.Enabled = !busy && loaded != null;
        store.Text = loaded is { } l ? $"스위치에 저장 ({l.from.Game})" : "스위치에 저장";
    }

    /// <summary>FTP는 창이 멈추지 않게 다른 스레드에서. 끝나면 화면 스레드에서 after</summary>
    private async void Run<T>(string what, Func<T> work, Action<T> after)
    {
        if (busy) return;
        busy = true; Buttons(); Say(what + "…");
        try { var r = await Task.Run(work); if (!IsDisposed) after(r); }
        catch (Exception ex) { Say("✕ " + (ex.GetBaseException().Message)); }
        finally { busy = false; if (!IsDisposed) Buttons(); }
    }

    private void Find()
    {
        var h = ip.Text.Trim();
        if (h.Length == 0) { Say("스위치 IP를 넣어 주세요."); return; }
        int.TryParse(port.Text.Trim(), out var p); if (p <= 0) p = 5000;
        var st = PluginSettings.Current; st.FtpHost = h; st.FtpPort = p; st.FtpUser = user.Text.Trim(); st.FtpPass = pass.Text; st.Save();
        var f = new SaveFtp(h, p, st.FtpUser, st.FtpPass);
        Run($"{h} 에 연결해 세이브를 찾는 중", f.Scan, found =>
        {
            ftp = f; list.Items.Clear();
            foreach (var x in found) list.Items.Add(x);
            if (f.Port != p) { port.Text = f.Port.ToString(); st.FtpPort = f.Port; st.Save(); }
            Say(found.Count == 0 ? "연결은 됐지만 PKHeX가 여는 포켓몬 게임 세이브가 없습니다." : $"세이브 {found.Count}개를 찾았습니다 (포트 {f.Port}). 고를 세이브를 누르고 「PKHeX로 불러오기」.");
            if (found.Count == 1) list.SelectedIndex = 0;
        });
    }

    private void LoadSelected()
    {
        if (list.SelectedItem is not FtpSave sel || ftp == null) return;
        Run($"{sel} 세이브를 받는 중", () =>
        {
            var raw = ftp.Retr(sel.Path);
            // 주의: TryGetSaveFile은 넘긴 배열을 그 자리에서 복호화함 → 사본으로 확인
            if (!SaveUtil.TryGetSaveFile(raw.ToArray(), out var s, sel.Path)) throw new InvalidDataException($"받은 파일을 세이브로 읽지 못했습니다 ({raw.Length:N0}바이트). 게임이 켜져 있지 않은지, PKHeX가 이 게임 버전을 지원하는지 확인해 주세요.");
            var file = Backup(sel, raw, "받음");
            return (raw, s.GetType(), file);
        }, r =>
        {
            if (!OpenInPKHeX(r.file)) { Say($"받은 세이브가 PKHeX에 열리지 않았습니다 (열기를 취소했거나 자동 열기 실패). 다시 불러오거나 이 파일을 PKHeX 창에 끌어다 놓으세요:\n{r.file}"); return; }
            loaded = (sel, r.raw, r.Item2);
            Say($"✓ 불러왔습니다 ({r.raw.Length:N0}바이트). PKHeX에서 고친 뒤 「스위치에 저장」을 누르세요.\n받은 그대로의 사본: {r.file}");
        });
    }

    /// <summary>PKHeX 본 창의 파일 열기를 그대로 부름 (끌어다 놓은 것과 같음)</summary>
    private bool OpenInPKHeX(string path)
    {
        try
        {
            var main = (host.SaveFileEditor as Control)?.FindForm();
            var m = main?.GetType().GetMethod("OpenFromPath", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, [typeof(string)]);
            if (m == null) return false;
            var before = host.SaveFileEditor.SAV;
            m.Invoke(main, [path]);
            return !ReferenceEquals(before, host.SaveFileEditor.SAV);
        }
        catch { return false; }
    }

    private void StoreCurrent()
    {
        if (loaded is not { } l || ftp == null) return;
        var sav = host.SaveFileEditor.SAV;
        if (sav == null || sav.GetType() != l.type)
        {
            Say($"✕ 지금 PKHeX에 열린 세이브가 불러온 것({l.from})과 다른 게임입니다. 스위치에서 다시 불러와 주세요."); return;
        }
        if (MessageBox.Show(this, $"PKHeX에 열린 세이브를 스위치에 넣습니다.\n\n  {l.from}\n  트레이너: {sav.OT} ({sav.DisplayTID:D6})\n\n스위치에서 이 게임이 꺼져 있어야 합니다. 계속할까요?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        byte[] data;
        try
        {
            data = sav.Write().ToArray();
            if (!SaveUtil.TryGetSaveFile(data.ToArray(), out var chk, l.from.Path) || chk.GetType() != l.type) throw new InvalidDataException("내보낸 세이브를 다시 읽지 못했습니다");
        }
        catch (Exception ex) { Say("✕ 세이브를 내보내지 못했습니다: " + ex.Message); return; }

        Run("스위치의 지금 세이브를 확인하는 중", () => ftp.Retr(l.from.Path), now =>
        {
            Backup(l.from, now, "넣기전");
            if (!now.AsSpan().SequenceEqual(l.raw) &&
                MessageBox.Show(this, "스위치의 세이브가 불러온 뒤로 바뀌었습니다 (그 사이 게임을 했거나 다른 도구가 고침).\n지금 넣으면 그 변경은 사라집니다. 그래도 넣을까요?\n\n(바뀐 세이브는 백업 폴더에 남겨 두었습니다)", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            { Say("넣지 않았습니다. 스위치에서 다시 불러와 고쳐 주세요."); return; }
            BeginInvoke(() => Run($"세이브를 넣는 중 ({data.Length:N0}바이트)", () =>
            {
                ftp.Stor(l.from.Path, data);
                var back = ftp.Retr(l.from.Path);
                if (back.AsSpan().SequenceEqual(data)) return "";
                // 넣은 것과 다시 받은 것이 다름 → 넣기 전 세이브로 되돌림
                try { ftp.Stor(l.from.Path, now); var again = ftp.Retr(l.from.Path); return again.AsSpan().SequenceEqual(now) ? "restored" : "broken"; }
                catch (Exception ex) { return "broken: " + ex.Message; }
            }, r =>
            {
                if (r == "")
                {
                    loaded = (l.from, data, l.type); Backup(l.from, data, "넣음");
                    Say("✓ 스위치에 넣고 다시 받아 같은지 확인했습니다. 이제 게임을 켜도 됩니다.");
                }
                else if (r == "restored") Say("✕ 넣은 세이브가 제대로 들어가지 않아 넣기 전 세이브로 되돌렸습니다. (게임이 켜져 있지 않은지, save_writable=1 과 세이브 쓰기를 고친 ftpsrv인지 확인)");
                else Say($"✕✕ 세이브가 제대로 들어가지 않았고 되돌리기도 확인하지 못했습니다 ({r}).\n게임을 켜지 말고, 백업 폴더의 「넣기전」 파일을 JKSV 등으로 복원해 주세요: {BackupDir}");
            }));
        });
    }

    /// <summary>세이브 사본을 백업 폴더에 남기고 그 경로를 돌려줌 (PKHeX가 알아보도록 파일 이름은 원래 이름 그대로)</summary>
    private static string Backup(FtpSave from, byte[] raw, string tag)
    {
        var safe = string.Concat($"{from.Game}_{from.User}".Select(ch => Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch));
        var dir = Path.Combine(BackupDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{safe}_{tag}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, from.Path[(from.Path.LastIndexOf('/') + 1)..]);
        File.WriteAllBytes(file, raw);
        return file;
    }
}
