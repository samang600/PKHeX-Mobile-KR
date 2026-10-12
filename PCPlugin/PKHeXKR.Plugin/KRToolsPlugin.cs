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
        root.DropDownItems.AddRange([quick, dex, new ToolStripSeparator(), settings]);
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

    private void Open(Func<Form> make)
    {
        if (SaveFileEditor?.SAV == null) { MessageBox.Show("세이브가 열려 있지 않습니다.", Name); return; }
        using var f = make();
        var owner = (SaveFileEditor as Control)?.FindForm();
        f.StartPosition = FormStartPosition.CenterParent;
        f.ShowDialog(owner);
    }

    public void NotifySaveLoaded() { }
    public bool TryLoadFile(string filePath) => false;
}
