using System.Drawing;
using PKHeX.Core;

namespace PKHeXKR.Plugin;

/// <summary>리빙덱스 채우기 창.</summary>
public sealed class LivingDexForm : Form
{
    private readonly KRToolsPlugin host;
    private readonly CheckBox shiny = new() { Text = "이로치로 만들기 (불가능하면 일반 색)", AutoSize = true };
    private readonly CheckBox forms = new() { Text = "모든 폼 포함", AutoSize = true };
    private readonly CheckBox skip = new() { Text = "이미 박스에 있는 포켓몬 건너뛰기", AutoSize = true, Checked = true };
    private readonly CheckBox alpha = new() { Text = "우두머리로 만들기 (우두머리 조우가 없으면 일반)", AutoSize = true };
    private readonly CheckBox transfer = new() { Text = "다른 게임에서 옮겨 온 개체 포함 (HOME·GO 등)", AutoSize = true };
    private readonly TextBox presets = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Height = 80, Dock = DockStyle.Fill, PlaceholderText = "포켓몬별 성별·볼 미리 정하기 (텍스트 생성과 같은 문법, 한 줄에 하나)\r\n예) 피카츄 ♀ 몬스터볼\r\n라이츄(알로라) ♂ 럭셔리볼" };
    private readonly TextBox dFrom = new() { Width = 130, PlaceholderText = "예: 2025-10-16" }, dTo = new() { Width = 130, PlaceholderText = "예: 2026-09-30" };
    private readonly TextBox ots = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Height = 90, Dock = DockStyle.Fill, PlaceholderText = "어버이 여러 명 (한 줄에 '이름 TID'), 비우면 세이브 트레이너\r\n예) 새아 468686\r\n하늘 123456" };
    private readonly Button go = new() { Text = "채우기 시작", AutoSize = true }, stop = new() { Text = "중지", AutoSize = true, Enabled = false };
    private readonly ProgressBar bar = new() { Dock = DockStyle.Fill, Height = 18 };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(560, 0) };
    private CancellationTokenSource cts;
    private bool running;

    public LivingDexForm(KRToolsPlugin host)
    {
        this.host = host;
        var sav = host.SaveFileEditor.SAV;
        Text = $"리빙덱스 채우기 · {GenHost.GameName(sav.Version)}";
        Size = new Size(620, 560); MinimumSize = new Size(520, 480); Font = new Font("Malgun Gothic", 9.5f);
        alpha.Visible = LivingDexGen.IsAlphaGame(sav);
        ots.Text = PluginSettings.Current.LastOTs;
        presets.Text = PluginSettings.Current.LivingDexPresets;
        transfer.Checked = LivingDexOptions.DefaultTransfer(sav);
        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(570, 0), Padding = new Padding(0, 0, 0, 6),
            Text = $"{GenHost.GameName(sav.Version)}에 나오는 포켓몬을 도감 순서대로 자동 합법화(ALM)·조우로 만들어 {host.SaveFileEditor.CurrentBox + 1}번 박스부터 빈 칸에 채웁니다. 합법으로 만들 수 있는 개체만 넣고, 도구를 지니면 바뀌는 폼(아르세우스·실버디·게노세크트 등)은 기본 폼만 만듭니다. 어버이를 비우면 세이브 트레이너, 출신 게임이 다르면 '트레이너·내 사전 설정'의 트레이너를 씁니다. 게임과 포켓몬 수에 따라 몇 분 걸릴 수 있습니다.",
        };
        var dates = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        dates.Controls.AddRange([new Label { Text = "만난 날짜", AutoSize = true, Padding = new Padding(0, 6, 4, 0) }, dFrom, new Label { Text = "~", AutoSize = true, Padding = new Padding(2, 6, 2, 0) }, dTo, new Label { Text = "(비우면 그대로)", AutoSize = true, Padding = new Padding(4, 6, 0, 0), ForeColor = SystemColors.GrayText }]);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; buttons.Controls.AddRange([go, stop]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10), AutoScroll = true };
        foreach (var c in new Control[] { hint, shiny, forms, skip, alpha, transfer, dates, new Label { Text = "어버이 (여러 명이면 돌아가며)", AutoSize = true, Padding = new Padding(0, 6, 0, 2) }, ots, new Label { Text = "성별·볼 미리 정하기", AutoSize = true, Padding = new Padding(0, 6, 0, 2) }, presets, buttons, bar, status })
        { layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(c); }
        Controls.Add(layout);

        stop.Click += (_, _) => cts?.Cancel();
        go.Click += async (_, _) => await Run();
        FormClosing += (_, e) =>
        {
            if (running) { e.Cancel = true; MessageBox.Show(this, "채우는 중입니다. 먼저 '중지'를 누르세요.", Text); return; }
            PluginSettings.Current.LastOTs = ots.Text; PluginSettings.Current.LivingDexPresets = presets.Text; PluginSettings.Current.Save();
        };
    }

    private async Task Run()
    {
        var sav = host.SaveFileEditor.SAV;
        var opt = new LivingDexOptions
        {
            Shiny = shiny.Checked, Forms = forms.Checked, SkipExisting = skip.Checked, Alpha = alpha.Visible && alpha.Checked, StartBox = host.SaveFileEditor.CurrentBox,
            From = DateOnly.TryParse(dFrom.Text, out var df) ? df : null, To = DateOnly.TryParse(dTo.Text, out var dt) ? dt : null,
            OTs = LivingDexOptions.ParseOTs(ots.Text),
            Transfer = transfer.Checked,
            Presets = LivingDexOptions.ParsePresets(presets.Text, sav, PluginSettings.Current.Dict()),
        };
        cts = new CancellationTokenSource(); var tok = cts.Token;
        running = true; go.Enabled = false; stop.Enabled = true; bar.Value = 0;
        LivingDexResult r;
        using (new AlmScope(sav))
        {
            try
            {
                r = await Task.Run(() => LivingDexGen.Run(sav, opt, tok, (done, total, s) =>
                    BeginInvoke(() => { bar.Maximum = Math.Max(1, total); bar.Value = Math.Min(done, bar.Maximum); status.Text = $"{done}/{total} · {GenHost.Strings.Species[s]}"; })));
            }
            finally { running = false; go.Enabled = true; stop.Enabled = false; }
        }
        host.SaveFileEditor.ReloadSlots();
        status.Text = $"넣음 {r.Made}마리 (모두 합법{(opt.Alpha ? $", 우두머리 {r.Alpha}" : "")}) · 만들 수 없음 {r.Fail} · 대상 {r.Targets}{(r.PresetMissed > 0 ? $" · 미리 정한 성별·볼이 안 돼 기본으로 만든 수 {r.PresetMissed}" : "")}{(r.BoxFull ? " · 박스가 가득 차서 멈춤" : "")}{(r.Canceled ? " · 중지함" : "")}";
    }
}
