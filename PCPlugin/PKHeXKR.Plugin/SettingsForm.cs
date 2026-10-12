using System.Drawing;
using PKHeX.Core;

namespace PKHeXKR.Plugin;

/// <summary>
/// 트레이너 설정: 기본 트레이너 + 게임별 트레이너.
/// 자동 합법화·리빙덱스가 다른 게임 출신 개체를 만들 때 "ALM"·영어 대신 이 트레이너를 씀.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly KRToolsPlugin host;
    private readonly ComboBox game = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox ot = new() { Width = 160 }, tid = new() { Width = 90 }, sid = new() { Width = 70 };
    private readonly ComboBox gender = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 }, lang = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly CheckBox useOwn = new() { Text = "이 게임만 따로 지정", AutoSize = true };
    private readonly Label info = new() { AutoSize = true, MaximumSize = new Size(520, 0), ForeColor = SystemColors.GrayText };
    private const string DefaultKey = "(기본 트레이너)";
    private static readonly (int Id, string Name)[] Langs = [((int)LanguageID.Korean, "한국어"), ((int)LanguageID.Japanese, "일본어"), ((int)LanguageID.English, "영어"), ((int)LanguageID.French, "프랑스어"), ((int)LanguageID.German, "독일어"), ((int)LanguageID.Italian, "이탈리아어"), ((int)LanguageID.Spanish, "스페인어"), ((int)LanguageID.ChineseS, "중국어(간체)"), ((int)LanguageID.ChineseT, "중국어(번체)")];
    private string current = DefaultKey;
    private bool loading;

    public SettingsForm(KRToolsPlugin host)
    {
        this.host = host;
        Text = "트레이너·내 사전 설정"; Size = new Size(580, 460); Font = new Font("Malgun Gothic", 9.5f);
        gender.Items.AddRange(["♂ 남", "♀ 여"]);
        foreach (var l in Langs) lang.Items.Add(l.Name);
        game.Items.Add(DefaultKey);
        foreach (var v in GenTrainers.Games) game.Items.Add($"{GenHost.GameName(v)} ({v})");
        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(540, 0), Padding = new Padding(0, 0, 0, 8),
            Text = "텍스트로 빠른 생성·리빙덱스가 지금 세이브와 다른 게임 출신 포켓몬을 만들 때 쓸 어버이입니다.\n순서: 지금 세이브(같은 게임) → 게임별 지정 → 같은 세대 세이브 트레이너 → 기본 트레이너.\n7세대 이후는 TID 6자리·SID 4자리, 그 이전은 TID·SID 각 5자리(최대 65535)로 들어갑니다.",
        };
        var fromSave = new Button { Text = "지금 세이브 트레이너 가져오기", AutoSize = true };
        fromSave.Click += (_, _) =>
        {
            var s = host.SaveFileEditor.SAV; bool six = s.Generation >= 7;
            ot.Text = s.OT; gender.SelectedIndex = s.Gender & 1;
            tid.Text = (six ? s.ID32 % 1_000_000 : s.TID16).ToString(); sid.Text = (six ? s.ID32 / 1_000_000 : s.SID16).ToString();
            var li = Array.FindIndex(Langs, x => x.Id == GenTrainers.SaveLanguage(s, (int)LanguageID.Korean)); lang.SelectedIndex = Math.Max(0, li);
        };
        var save = new Button { Text = "저장", AutoSize = true };
        save.Click += (_, _) => { Store(); var err = PluginSettings.Current.Save(); MessageBox.Show(this, err == null ? $"저장했습니다\n{PluginSettings.SavedPath}" : "설정 파일을 저장하지 못했습니다 (이번 실행 동안은 적용됨)\n" + err, Text); };
        var dict = new Button { Text = "내 사전…", AutoSize = true };
        dict.Click += (_, _) => { using var f = new DictForm(); f.ShowDialog(this); };

        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        void Row(string label, Control c) { grid.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 8, 0) }); grid.Controls.Add(c); }
        Row("게임", game); Row("", useOwn); Row("이름", ot); Row("성별", gender); Row("TID", tid); Row("SID", sid); Row("언어", lang);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill }; buttons.Controls.AddRange([fromSave, save, dict]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        foreach (var c in new Control[] { hint, grid, buttons, info }) { layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.Controls.Add(c); }
        Controls.Add(layout);

        game.SelectedIndexChanged += (_, _) => { if (loading) return; Store(); current = KeyOf(game.SelectedIndex); ShowEntry(current); };
        useOwn.CheckedChanged += (_, _) => { if (!loading) EnableFields(); };
        FormClosing += (_, _) => { Store(); PluginSettings.Current.Save(); };
        loading = true; game.SelectedIndex = 0; loading = false; ShowEntry(DefaultKey);
    }

    private static string KeyOf(int idx) => idx <= 0 ? DefaultKey : GenTrainers.Games[idx - 1].ToString();
    private void EnableFields() { bool on = current == DefaultKey || useOwn.Checked; ot.Enabled = tid.Enabled = sid.Enabled = gender.Enabled = lang.Enabled = on; }

    private void ShowEntry(string key)
    {
        loading = true;
        var st = PluginSettings.Current;
        TrainerEntry t;
        if (key == DefaultKey) { t = st.Default; useOwn.Visible = false; info.Text = ""; }
        else
        {
            st.PerGame.TryGetValue(key, out var own); t = own ?? st.Default;
            useOwn.Visible = true; useOwn.Checked = own != null;
            info.Text = own == null ? "따로 지정하지 않으면 같은 세대 세이브 트레이너, 없으면 기본 트레이너를 씁니다." : "";
        }
        bool old = key != DefaultKey && Enum.TryParse<GameVersion>(key, out var gv) && new SimpleTrainerInfo(gv).Generation <= 3;
        if (old) info.Text = (info.Text + "\n1~3세대 게임은 한글 이름을 쓸 수 없습니다 (영문 또는 일본어 이름, 일본어 이름은 언어도 일본어로).").Trim();
        else if (key == DefaultKey) info.Text = "기본 트레이너 이름이 한글이면 1~3세대 게임에는 이름이 들어가지 않습니다. 그 게임들은 게임별로 영문·일본어 이름을 지정하세요.";
        ot.Text = t.OT; gender.SelectedIndex = t.Gender & 1; tid.Text = t.TID.ToString(); sid.Text = t.SID.ToString();
        lang.SelectedIndex = Math.Max(0, Array.FindIndex(Langs, x => x.Id == t.Lang));
        loading = false; EnableFields();
    }

    private void Store()
    {
        var st = PluginSettings.Current;
        if (current != DefaultKey && !useOwn.Checked) { st.PerGame.Remove(current); return; }
        var t = new TrainerEntry
        {
            OT = string.IsNullOrWhiteSpace(ot.Text) ? "PKHeX" : ot.Text.Trim(), Gender = Math.Max(0, gender.SelectedIndex),
            TID = int.TryParse(tid.Text, out var a) ? Math.Clamp(a, 0, 999999) : 0, SID = int.TryParse(sid.Text, out var b) ? Math.Clamp(b, 0, 65535) : 0,
            Lang = Langs[Math.Max(0, lang.SelectedIndex)].Id,
        };
        if (current == DefaultKey) st.Default = t; else st.PerGame[current] = t;
    }
}
