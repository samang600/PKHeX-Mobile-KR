using PKHeX.Core;

namespace PKHeXKR.Plugin;

/// <summary>
/// PKHeX 플러그인 진입점. 도구 메뉴에 "PKHeX 모바일 도구" 묶음을 추가.
/// </summary>
public sealed class KRToolsPlugin : IPlugin
{
    public string Name => "PKHeX 모바일 도구 (KR)";
    public int Priority => 50;   // ALM(0) 등 다른 플러그인 다음
    public ISaveFileProvider SaveFileEditor { get; private set; }
    public IPKMView PKMEditor { get; private set; }
    private ToolStripMenuItem root;
    private static bool initDone;

    public void Initialize(params object[] args)
    {
        SaveFileEditor = (ISaveFileProvider)Array.Find(args, z => z is ISaveFileProvider);
        PKMEditor = (IPKMView)Array.Find(args, z => z is IPKMView);
        var menu = (ToolStrip)Array.Find(args, z => z is ToolStrip);
        InitEngine();
        if (menu == null) return;
        if (menu.Items.Find("Menu_Tools", false).FirstOrDefault() is not ToolStripDropDownItem tools) return;

        root = new ToolStripMenuItem("PKHeX 모바일 도구 (KR)") { Name = "Menu_PKHeXKR" };
        var quick = new ToolStripMenuItem("텍스트로 빠른 생성…");
        quick.Click += (_, _) => Open(() => new QuickForm(this));
        var dex = new ToolStripMenuItem("리빙덱스 채우기…");
        dex.Click += (_, _) => Open(() => new LivingDexForm(this));
        var settings = new ToolStripMenuItem("트레이너·내 사전 설정…");
        settings.Click += (_, _) => Open(() => new SettingsForm(this));
        var ldn = new ToolStripMenuItem("PokeLDN 교환 (ESP32 보드)…");
        ldn.Click += (_, _) => Open(() => new LdnTradeForm(this));
        root.DropDownItems.AddRange([quick, dex, ldn, new ToolStripSeparator(), settings]);
        tools.DropDownItems.Add(root);
    }

    /// <summary>공통 생성 엔진 준비 (한 번만): 이름 데이터는 PKHeX 표시 언어와 상관없이 한국어, 5~7세대 배포 기간 데이터.</summary>
    private static void InitEngine()
    {
        if (initDone) return; initDone = true;
        GameStrings ko = null;
        GenHost.StringsSource = () => ko ??= GameInfo.GetStrings("ko");
        // PKHeX의 빈 세이브(파일을 열지 않은 상태, "PKHeX"·123456·영어)이거나 이 게임을 따로 지정했으면 설정 트레이너로 만듦
        GenHost.TrainerOverride = sav =>
        {
            var st = PluginSettings.Current; var ex = st.Explicit(sav.Version);
            if (ex == null && sav.State.Exportable) return null;   // 실제 세이브: 세이브 트레이너
            return GenTrainers.FromPref(sav.Version, (ex ?? st.Default).ToPref(), GenTrainers.SafeLang);
        };
        try
        {
            using var st = typeof(KRToolsPlugin).Assembly.GetManifestResourceStream("card_windows.json");
            if (st != null) GenHost.LoadCardWindows(st);
        }
        catch { }
    }

    // 열린 플러그인 창 (종류마다 하나). 모달(ShowDialog)이 아니라서 창을 연 채로 PKHeX 본 창도 조작할 수 있음
    private readonly Dictionary<Type, Form> open = new();

    private void Open<T>(Func<T> make) where T : Form
    {
        if (SaveFileEditor?.SAV == null) { MessageBox.Show("세이브가 열려 있지 않습니다.", Name); return; }
        if (open.TryGetValue(typeof(T), out var cur) && !cur.IsDisposed)
        {
            if (cur.WindowState == FormWindowState.Minimized) cur.WindowState = FormWindowState.Normal;
            cur.Activate(); return;
        }
        var f = make();
        var owner = (SaveFileEditor as Control)?.FindForm();
        f.StartPosition = FormStartPosition.CenterScreen;
        f.FormClosed += (_, _) => { open.Remove(typeof(T)); f.Dispose(); };
        open[typeof(T)] = f;
        f.Show(owner);   // owner를 주면 본 창 위에 떠 있지만 본 창도 누를 수 있음
    }

    /// <summary>다른 세이브를 열면: 그 세이브 기준으로 만든 창(빠른 생성·리빙덱스·설정)은 닫음. PokeLDN 창은 교환 중일 수 있어 그대로 둠 (세이브는 쓸 때마다 새로 읽음)</summary>
    public void NotifySaveLoaded()
    {
        foreach (var (t, f) in open.ToList())
            if (t != typeof(LdnTradeForm) && !f.IsDisposed) f.Close();
        if (open.TryGetValue(typeof(LdnTradeForm), out var l) && l is LdnTradeForm ldn && !ldn.IsDisposed) ldn.SaveChanged();
    }
    public bool TryLoadFile(string filePath) => false;
}
