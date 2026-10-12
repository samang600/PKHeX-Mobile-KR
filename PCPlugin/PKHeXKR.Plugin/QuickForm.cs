using System.Drawing;
using PKHeX.Core;

namespace PKHeXKR.Plugin;

/// <summary>텍스트로 빠른 생성 창.</summary>
public sealed class QuickForm : Form
{
    private readonly KRToolsPlugin host;
    private readonly TextBox input = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Dock = DockStyle.Fill, Font = new Font("Malgun Gothic", 11f) };
    private readonly RichTextBox output = new() { ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Malgun Gothic", 10f), BorderStyle = BorderStyle.FixedSingle, BackColor = SystemColors.Window };
    private readonly Label status = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 6) };
    private readonly Button parseBtn = new() { Text = "해석해 보기", AutoSize = true }, makeBtn = new() { Text = "만들기", AutoSize = true }, stopBtn = new() { Text = "중지", AutoSize = true, Enabled = false },
        dictBtn = new() { Text = "내 사전…", AutoSize = true }, clearBtn = new() { Text = "지우기", AutoSize = true };
    private CancellationTokenSource cts;

    public QuickForm(KRToolsPlugin host)
    {
        this.host = host;
        Text = $"텍스트로 빠른 생성 · {GenHost.GameName(host.SaveFileEditor.SAV.Version)}";
        Size = new Size(820, 720); MinimumSize = new Size(560, 480); Font = new Font("Malgun Gothic", 9.5f);
        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(780, 0), Padding = new Padding(0, 0, 0, 6),
            Text = "한 줄에 한 마리(괄호 안에 '/'로 적어도 됨: 에써르 (수/ 작증/ 럽볼)). 포켓몬·성별(암/수)·이로치(이로치·우두로치·별·네모)·볼(럽볼·렙볼·프볼…)·크기(가장작게·L·가장크게)·증표(작증·큰증·카레)·성격·숨특·6V·A0·Lv50·x3 등을 순서 상관없이 적으세요.\n" +
                   "'어버이: 이름 TID' 줄은 아래 전체에 적용되고, 적지 않은 값은 게임에서 자연스럽게 나오는 값으로 채웁니다. 한 마리면 편집기로, 여러 마리면 현재 박스부터 빈 칸에 넣습니다.",
        };
        input.PlaceholderText = "예)\r\n어버이: 새아 468686\r\n빠르모트 암컷 이로치 러브볼 가장작게 / 찌리배리 레벨볼 큰증\r\n싸리용(뻗은,젖힌) 우두로치 다이브볼 가장크게\r\n망나뇽 암";
        input.Text = PluginSettings.Current.LastQuickText;
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.AddRange([parseBtn, makeBtn, stopBtn, dictBtn, clearBtn]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.Controls.Add(hint, 0, 0); layout.Controls.Add(input, 0, 1); layout.Controls.Add(buttons, 0, 2); layout.Controls.Add(status, 0, 3); layout.Controls.Add(output, 0, 4);
        Controls.Add(layout);

        parseBtn.Click += (_, _) => ShowParse();
        makeBtn.Click += async (_, _) => await Make();
        stopBtn.Click += (_, _) => cts?.Cancel();
        dictBtn.Click += (_, _) => { using var f = new DictForm(); f.ShowDialog(this); };
        clearBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text)) return;
            if (MessageBox.Show(this, "입력한 내용을 모두 지울까요?", "지우기", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            input.Clear(); output.Clear(); status.Text = "";
        };
        FormClosing += (_, e) =>
        {
            if (stopBtn.Enabled) { e.Cancel = true; MessageBox.Show(this, "만드는 중입니다. 먼저 '중지'를 누르세요.", Text); return; }
            PluginSettings.Current.LastQuickText = input.Text; PluginSettings.Current.Save();
        };
    }

    private SaveFile Sav => host.SaveFileEditor.SAV;
    private List<OrderSpec> Parse() => QuickGen.Parse(input.Text ?? "", Sav, PluginSettings.Current.Dict());

    private void Append(string head, string sub, bool? ok)
    {
        output.SelectionStart = output.TextLength;
        output.SelectionFont = new Font(output.Font, FontStyle.Bold);
        output.SelectionColor = ok == false ? Color.Firebrick : ok == true ? Color.SeaGreen : SystemColors.WindowText;
        output.AppendText((ok == null ? "" : ok.Value ? "✔ " : "✖ ") + head + "\n");
        output.SelectionFont = output.Font; output.SelectionColor = SystemColors.GrayText;
        output.AppendText(sub.Replace("\n", "\n   ") + "\n\n");
        output.ScrollToCaret();
    }
    private static string Head(OrderSpec o) => o.Species == 0 ? "(포켓몬 없음)" : o.Describe(GenHost.Strings);
    private static string Sub(OrderSpec o, string result) =>
        "   입력: " + o.Raw.Trim() + (o.Unknown.Count > 0 ? "   ? " + string.Join(", ", o.Unknown) : "") + (o.Notes.Count > 0 ? "   ※ " + string.Join("; ", o.Notes) : "") + (result != null ? "\n" + result : "");

    private void ShowParse()
    {
        var list = Parse(); output.Clear();
        foreach (var o in list) Append(Head(o), Sub(o, null), null);
        status.Text = $"{list.Count}마리로 해석했습니다" + (list.Any(x => x.Unknown.Count > 0) ? " · '?' 표시 단어는 알아보지 못해 무시합니다" : "");
    }

    private async Task Make()
    {
        var list = Parse();
        if (list.Count == 0) { status.Text = "입력한 내용이 없습니다"; return; }
        var noShiny = QuickGen.NoShinySpecified(list);
        if (noShiny.Count > 0)
        {
            var names = string.Join(", ", noShiny.Take(5).Select(x => GenHost.Strings.Species[x.Species])) + (noShiny.Count > 5 ? " 외" : "");
            var r = MessageBox.Show(this, $"이로치 여부를 적지 않은 포켓몬 {noShiny.Count}마리 ({names})\n\n예: 이로치로 만들기\n아니요: 일반 색으로 만들기", "이로치 여부", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            foreach (var x in noShiny) x.Shiny = r == DialogResult.Yes ? 1 : 2;
        }
        var sav = Sav;
        cts = new CancellationTokenSource(); var tok = cts.Token;
        parseBtn.Enabled = makeBtn.Enabled = dictBtn.Enabled = clearBtn.Enabled = false; stopBtn.Enabled = true; output.Clear();
        var made = new List<PKM>(); int total = list.Sum(x => x.Species == 0 ? 1 : x.Count), done = 0;
        using (new AlmScope(sav))
        {
            try
            {
                foreach (var o in list)
                {
                    if (tok.IsCancellationRequested) break;
                    if (o.Species == 0) { Append(Head(o), Sub(o, "   포켓몬 이름을 알아보지 못했습니다"), false); done++; continue; }
                    for (int k = 0; k < o.Count && !tok.IsCancellationRequested; k++)
                    {
                        status.Text = $"만드는 중… {done + 1}/{total} · {GenHost.Strings.Species[o.Species]}";
                        var (pk, how) = await Task.Run(() => QuickGen.Make(o, sav, tok));
                        done++;
                        bool ok = pk != null && GenHost.IsLegal(pk);
                        if (pk != null) made.Add(pk);
                        Append(Head(o), Sub(o, "   " + (pk == null ? how : $"{how} · {(ok ? "합법" : "불법 — 편집기에서 합법성 표시를 확인하세요")}{QuickGen.Stats(pk)}")), pk != null && ok);
                    }
                }
            }
            finally { parseBtn.Enabled = makeBtn.Enabled = dictBtn.Enabled = clearBtn.Enabled = true; stopBtn.Enabled = false; }
        }
        if (made.Count == 0) { status.Text = "만든 포켓몬이 없습니다"; return; }
        if (made.Count == 1)
        {
            host.PKMEditor.PopulateFields(made[0]);
            status.Text = "편집기로 불러왔습니다 (박스에 넣으려면 PKHeX에서 칸에 저장하세요)";
            return;
        }
        int put = QuickGen.PlaceInBoxes(sav, made, host.SaveFileEditor.CurrentBox);
        host.SaveFileEditor.ReloadSlots();
        status.Text = $"{made.Count}마리 중 {put}마리를 {host.SaveFileEditor.CurrentBox + 1}번 박스부터 빈 칸에 넣었습니다" + (put < made.Count ? " (박스가 가득 참)" : "");
    }
}

/// <summary>내 사전 편집 창.</summary>
public sealed class DictForm : Form
{
    public DictForm()
    {
        Text = "내 사전 (줄임말·별칭)"; Size = new Size(560, 520); Font = new Font("Malgun Gothic", 9.5f);
        var hint = new Label { AutoSize = true, MaximumSize = new Size(520, 0), Padding = new Padding(0, 0, 0, 6), Text = "'줄임말 = 바꿀 말' 형식으로 한 줄에 하나씩 적습니다. 바꿀 말에 '/'를 넣으면 여러 마리로 나뉩니다. 내 사전이 기본 사전보다 먼저 적용됩니다.\n기본 사전: " + string.Join(", ", QuickParse.BuiltInAlias.Select(kv => $"{kv.Key}→{kv.Value}")) };
        var ed = new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Malgun Gothic", 11f), Text = (PluginSettings.Current.UserDict ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"), PlaceholderText = "포니 = 포푸니라\r\n전설쌍 = 칠색조 / 루기아" };
        var save = new Button { Text = "저장", AutoSize = true, Anchor = AnchorStyles.Right };
        save.Click += (_, _) => { PluginSettings.Current.UserDict = ed.Text; PluginSettings.Current.Save(); MessageBox.Show(this, $"사전 {PluginSettings.Current.Dict().Count}개를 저장했습니다", Text); Close(); };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(hint, 0, 0); layout.Controls.Add(ed, 0, 1); layout.Controls.Add(save, 0, 2);
        Controls.Add(layout);
    }
}
