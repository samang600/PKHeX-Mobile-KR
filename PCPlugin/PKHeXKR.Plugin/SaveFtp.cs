using System.Net.Sockets;
using System.Text;

namespace PKHeXKR.Plugin;

/// <summary>스위치에서 찾은 세이브 하나</summary>
public sealed record FtpSave(string User, string Game, string Path)
{
    public override string ToString() => $"{User}  ·  {Game}";
}

/// <summary>
/// ftpsrv(스위치 sysmodule)로 게임 세이브를 내려받고 올림. 게임을 켜지 않은 상태에서 씀.
/// ftpsrv 경로: /save:/&lt;사용자 이름&gt; [UID]/files/&lt;게임 이름&gt; [타이틀ID]/&lt;파일&gt; — 타이틀 ID로 찾음 (이름은 언어마다 다름).
/// 쓰기는 세이브 쓰기를 고친 ftpsrv + config.ini [Nx] mount_devices=1, save_writable=1 이 필요.
/// (HomeBot의 SaveFtp를 옮긴 것)
/// </summary>
public sealed class SaveFtp(string host, int port, string user, string pass)
{
    /// <summary>PKHeX가 여는 스위치 게임들</summary>
    public static readonly (string Title, string Name)[] Titles =
    [
        ("0100A3D008C5C000", "스칼렛"), ("01008F6008C5E000", "바이올렛"),
        ("0100F43008C44000", "레전즈 Z-A"), ("01001F5010DFA000", "레전즈 아르세우스"),
        ("0100ABF008968000", "소드"), ("01008DB008C2C000", "실드"),
        ("0100000011D90000", "브릴리언트 다이아몬드"), ("010018E011D92000", "샤이닝 펄"),
        ("010003F003A34000", "레츠고 피카츄"), ("0100187003A36000", "레츠고 이브이"),
    ];
    private static readonly string[] SaveNames = ["main", "savedata.bin", "SaveData.bin"];

    // ---------- 최소 FTP (PASV, 바이너리)
    private sealed class Session : IDisposable
    {
        private readonly TcpClient c; private readonly NetworkStream s; private readonly string host;
        public Session(string host, int port, string user, string pass)
        {
            this.host = host;
            c = new TcpClient { ReceiveTimeout = 20000, SendTimeout = 20000 };
            if (!c.ConnectAsync(host, port).Wait(6000)) throw new IOException($"FTP 연결 시간 초과 ({host}:{port})");
            s = c.GetStream();
            Expect(Reply(), 220);
            var (code, msg) = Cmd("USER " + (string.IsNullOrEmpty(user) ? "anonymous" : user));
            if (code == 331) (code, msg) = Cmd("PASS " + pass);
            if (code != 230) throw new IOException($"FTP 로그인 실패: {code} {msg} (ftpsrv config.ini의 user/pass 또는 anon=1 확인)");
            Cmd("OPTS UTF8 ON");
            Expect(Cmd("TYPE I"), 200);
        }
        private string Line()
        {
            var b = new List<byte>();
            while (true) { int x = s.ReadByte(); if (x < 0) throw new IOException("FTP 연결이 끊김"); if (x == '\n') break; if (x != '\r') b.Add((byte)x); }
            return Encoding.UTF8.GetString(b.ToArray());
        }
        public (int, string) Reply()
        {
            var l = Line();
            if (l.Length < 3 || !int.TryParse(l[..3], out var code)) throw new IOException("FTP 응답 이상: " + l);
            if (l.Length > 3 && l[3] == '-') { var end = l[..3] + " "; string m; do m = Line(); while (!m.StartsWith(end)); }
            return (code, l.Length > 4 ? l[4..] : "");
        }
        public (int, string) Cmd(string line) { var b = Encoding.UTF8.GetBytes(line + "\r\n"); s.Write(b); return Reply(); }
        public static void Expect((int code, string msg) r, int want) { if (r.code != want) throw new IOException($"FTP {r.code} {r.msg}"); }
        private TcpClient Pasv()
        {
            var (code, msg) = Cmd("PASV");
            if (code != 227) throw new IOException($"FTP PASV 실패: {code} {msg}");
            int a = msg.IndexOf('('), z = msg.IndexOf(')');
            var p = msg[(a + 1)..z].Split(',').Select(int.Parse).ToArray();
            var d = new TcpClient { ReceiveTimeout = 30000, SendTimeout = 30000 };
            if (!d.ConnectAsync(host, p[4] * 256 + p[5]).Wait(6000)) throw new IOException("FTP 데이터 연결 시간 초과");
            return d;
        }
        public byte[] Get(string cmd)
        {
            using var d = Pasv();
            var (code, msg) = Cmd(cmd);
            if (code is not (150 or 125)) throw new IOException($"{cmd.Split(' ')[0]} 실패: {code} {msg}");
            using var ms = new MemoryStream(); d.GetStream().CopyTo(ms); d.Close();
            var (c2, m2) = Reply(); if (c2 != 226) throw new IOException($"{cmd.Split(' ')[0]} 끝 응답 이상: {c2} {m2}");
            return ms.ToArray();
        }
        public void Put(string path, byte[] data)
        {
            using (var d = Pasv())
            {
                var (code, msg) = Cmd("STOR " + path);
                if (code is not (150 or 125)) throw new IOException($"STOR 실패: {code} {msg} (세이브 쓰기를 고친 ftpsrv와 config.ini [Nx] save_writable=1 이 필요)");
                var ns = d.GetStream(); ns.Write(data); ns.Flush(); d.Client.Shutdown(SocketShutdown.Send); d.Close();
            }
            var (c2, m2) = Reply(); if (c2 != 226) throw new IOException($"STOR 끝 응답 이상: {c2} {m2}");
        }
        public List<string> List(string path) => Encoding.UTF8.GetString(Get("NLST " + path)).Split('\n').Select(x => x.Trim('\r', ' ', '/')).Where(x => x.Length > 0).Select(x => x.Contains('/') ? x[(x.LastIndexOf('/') + 1)..] : x).ToList();
        public void Dispose() { try { Cmd("QUIT"); } catch { } try { c.Close(); } catch { } }
    }

    private int okPort;
    /// <summary>실제로 연결된 포트 (아직이면 0)</summary>
    public int Port => okPort;
    /// <summary>적은 포트로 안 되면 ftpsrv에서 흔한 값(5000·21)도 시도하고, 된 포트를 기억</summary>
    private Session Open()
    {
        if (okPort != 0) return new(host, okPort, user, pass);
        Exception first = null;
        foreach (var p in new[] { port, 5000, 21 }.Where(x => x > 0).Distinct())
        {
            try { var s = new Session(host, p, user, pass); okPort = p; return s; }
            catch (Exception ex) when (ex is SocketException or AggregateException || ex.Message.Contains("시간 초과")) { first ??= ex; }
        }
        throw new IOException($"ftpsrv에 연결하지 못했습니다 ({host}, 포트 {port}/5000/21): {first?.GetBaseException().Message}\n스위치에 ftpsrv가 켜져 있는지, PC와 같은 공유기인지, IP가 맞는지 확인해 주세요.");
    }

    public byte[] Retr(string path) { using var f = Open(); return f.Get("RETR " + path); }
    public void Stor(string path, byte[] data) { using var f = Open(); f.Put(path, data); }

    /// <summary>스위치의 모든 사용자에서 PKHeX가 여는 게임 세이브를 찾음</summary>
    public List<FtpSave> Scan()
    {
        using var f = Open();
        var top = f.List("/");
        var s = top.FirstOrDefault(x => x.TrimEnd(':').Equals("save", StringComparison.OrdinalIgnoreCase))
            ?? throw new IOException("FTP 맨 위에 save: 가 없습니다 (ftpsrv config.ini [Nx] mount_devices=1 확인)\n있는 것: " + string.Join(", ", top));
        var root = "/" + (s.EndsWith(':') ? s : s + ":");
        var list = new List<FtpSave>();
        foreach (var u in f.List(root).Where(x => x.Contains('[')))
        {
            List<string> apps;
            try { apps = f.List($"{root}/{u}/files"); } catch (IOException) { continue; }
            foreach (var (title, name) in Titles)
            {
                var app = apps.FirstOrDefault(x => x.Contains("[" + title + "]", StringComparison.OrdinalIgnoreCase));
                if (app == null) continue;
                var dir = $"{root}/{u}/files/{app}";
                List<string> names;
                try { names = f.List(dir); } catch (IOException) { continue; }
                var file = SaveNames.FirstOrDefault(names.Contains);
                if (file != null) list.Add(new FtpSave(u[..u.LastIndexOf('[')].Trim(), name, $"{dir}/{file}"));
            }
        }
        return list;
    }
}
