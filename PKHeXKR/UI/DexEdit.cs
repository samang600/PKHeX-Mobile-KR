using System.ComponentModel;
using PKHeX.Core;
namespace PKHeXKR;

/// <summary>도감 한 종씩: 본 것 / 잡은 것 읽고 쓰기 (게임마다 쓰는 방법이 다름).</summary>
public static class DexEdit
{
    private static object Zukan(SaveFile sav)
    {
        if (sav is SAV4 s4) return s4.Dex;   // 4세대는 'Dex'
        return DexFill.FindZukan(sav);
    }
    private static System.Reflection.MethodInfo M(object o, string name, params Type[] args)
        => o?.GetType().GetMethods().FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length && x.GetParameters().Select(p => p.ParameterType).SequenceEqual(args));
    private static bool SavOverrides(SaveFile sav, string name)
    {
        var mi = sav.GetType().GetMethod(name, [typeof(ushort), typeof(bool)]);
        return mi != null && mi.DeclaringType != typeof(SaveFile);
    }

    /// <summary>한 종씩 편집할 수 있는 게임인지 (레전드 아르세우스는 연구 과제 방식이라 불가).</summary>
    public static bool Supported(SaveFile sav)
    {
        var z = Zukan(sav);
        return z is Zukan5 || M(z, "SetDexEntryAll", typeof(ushort), typeof(bool)) != null || M(z, "SetCaught", typeof(ushort), typeof(bool)) != null || SavOverrides(sav, "SetCaught");
    }

    /// <summary>'본 것'만 따로 켤 수 있는 게임인지 (최신 게임은 종 단위로 '등록/해제'만 가능).</summary>
    public static bool SeenSeparate(SaveFile sav)
    {
        var z = Zukan(sav);
        return z is Zukan5 || M(z, "SetSeen", typeof(ushort), typeof(bool)) != null || SavOverrides(sav, "SetSeen");
    }

    public static (bool Seen, bool Caught) Get(SaveFile sav, ushort sp)
    {
        try { return (sav.GetSeen(sp), sav.GetCaught(sp)); } catch { return (false, false); }
    }

    private static void RawSeen(SaveFile sav, ushort sp, bool v)
    {
        var z = Zukan(sav);
        if (z is Zukan5 z5) { if (v) z5.SetSeen(sp, 0, true); else z5.ClearSeen(sp); return; }
        var m = M(z, "SetSeen", typeof(ushort), typeof(bool));
        if (m != null) m.Invoke(z, [sp, v]); else sav.SetSeen(sp, v);
    }
    private static void RawCaught(SaveFile sav, ushort sp, bool v)
    {
        var z = Zukan(sav); var m = M(z, "SetCaught", typeof(ushort), typeof(bool));
        if (m != null) m.Invoke(z, [sp, v]); else sav.SetCaught(sp, v);
    }

    /// <summary>잡은 것으로(폼·성별 기록까지 채움) 또는 해제.</summary>
    public static void SetCaught(SaveFile sav, ushort sp, bool on)
    {
        var z = Zukan(sav);
        var all = M(z, "SetDexEntryAll", typeof(ushort), typeof(bool));
        var clr = M(z, "ClearDexEntryAll", typeof(ushort));
        if (on)
        {
            if (z is Zukan5 z5) { try { z5.GiveAll(sp, true, false, (LanguageID)sav.Language, false); } catch { } RawCaught(sav, sp, true); if (!sav.GetSeen(sp)) RawSeen(sav, sp, true); return; }
            if (all != null) { all.Invoke(z, [sp, false]); if (Get(sav, sp).Caught) return; }
            RawSeen(sav, sp, true); RawCaught(sav, sp, true);
        }
        else
        {
            if (!SeenSeparate(sav) && clr != null) { clr.Invoke(z, [sp]); return; }   // 최신 게임: 종 기록 통째로 해제
            RawCaught(sav, sp, false);
        }
    }

    /// <summary>본 것 켜기/끄기 (끄면 잡은 기록·폼 기록도 지움).</summary>
    public static void SetSeen(SaveFile sav, ushort sp, bool on)
    {
        if (on) { RawSeen(sav, sp, true); return; }
        var z = Zukan(sav); var clr = M(z, "ClearDexEntryAll", typeof(ushort));
        if (clr != null) { clr.Invoke(z, [sp]); if (!Get(sav, sp).Seen) return; }
        RawCaught(sav, sp, false); RawSeen(sav, sp, false);
    }
}

public sealed class DexRow : INotifyPropertyChanged
{
    public ushort Species { get; init; }
    public string Name { get; init; } = "";
    public string No => $"No.{Species:000}";
    public string Sprite { get; init; } = "";
    private bool seen, caught;
    public event PropertyChangedEventHandler PropertyChanged;
    public Action<DexRow, int, bool> Write;   // (행, 0 본 것 / 1 잡은 것, 값)
    public bool Seen { get => seen; set { if (seen == value) return; Write?.Invoke(this, 0, value); } }
    public bool Caught { get => caught; set { if (caught == value) return; Write?.Invoke(this, 1, value); } }
    public void Refresh(bool s, bool c)
    {
        bool ch = s != seen || c != caught; seen = s; caught = c;
        if (ch) { PropertyChanged?.Invoke(this, new(nameof(Seen))); PropertyChanged?.Invoke(this, new(nameof(Caught))); }
    }
}

/// <summary>도감 한 마리씩 편집: 검색 · 못 잡은 것만 · 본 것/잡은 것 체크.</summary>
public class DexEditSheet : Sheet
{
    private readonly List<DexRow> rows = [];
    private readonly CollectionView list = new() { SelectionMode = SelectionMode.None };
    private readonly Label count = T.L("", 12, sub: true);
    private int mode;   // 0 전체, 1 못 잡은 것, 2 잡은 것
    private string query = "";

    public DexEditSheet() : base("도감 편집", 0.9)
    {
        var sav = AppState.Sav; var s = GameInfo.Strings;
        if (!DexEdit.Supported(sav))
        {
            var no = T.L($"{AppState.GameName(sav.Version)}은(는) 도감을 한 마리씩 편집할 수 없습니다 (레전드 아르세우스는 연구 과제 방식). 일괄 등록을 쓰세요.", 14); no.LineBreakMode = LineBreakMode.WordWrap;
            Body.Add(no); return;
        }
        bool sep = DexEdit.SeenSeparate(sav);
        for (ushort sp = 1; sp <= sav.MaxSpeciesID; sp++)
        {
            if (!sav.Personal.IsSpeciesInGame(sp)) continue;
            var r = new DexRow { Species = sp, Name = s.Species[sp], Sprite = AppState.Sprite(sp, 0, false) };
            var (se, ca) = DexEdit.Get(sav, sp); r.Refresh(se, ca);
            r.Write = (row, what, v) =>
            {
                try { if (what == 0) DexEdit.SetSeen(sav, row.Species, v); else DexEdit.SetCaught(sav, row.Species, v); }
                catch (Exception ex) { Note.Show("도감 기록 실패: " + ex.Message); }
                var (a, b) = DexEdit.Get(sav, row.Species); row.Refresh(a, b);
                if (what == 0 && v && !a) Note.Show("이 게임은 '본 것'만 따로 켤 수 없습니다 · '잡음'을 쓰세요");
                UpdateCount();
            };
            rows.Add(r);
        }
        var q = T.Input(placeholder: "이름·초성·번호");
        q.TextChanged += (_, e) => { query = (e.NewTextValue ?? "").Trim(); Filter(); };
        var bAll = T.Pill("전체", primary: true); var bMiss = T.Pill("못 잡은 것"); var bGot = T.Pill("잡은 것");
        void Mode(int m) { mode = m; foreach (var (b, i) in new[] { (bAll, 0), (bMiss, 1), (bGot, 2) }) Tint(b, i == m); Filter(); }
        bAll.Clicked += (_, _) => Mode(0); bMiss.Clicked += (_, _) => Mode(1); bGot.Clicked += (_, _) => Mode(2);
        list.ItemTemplate = new DataTemplate(() =>
        {
            var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 6, Padding = new Thickness(2, 2), HeightRequest = 46 };
            var img = new Image { WidthRequest = 44, HeightRequest = 38 }; img.SetBinding(Image.SourceProperty, nameof(DexRow.Sprite));
            var n = T.L("", 14, bold: true); n.SetBinding(Label.TextProperty, nameof(DexRow.Name));
            var no = T.L("", 11, sub: true); no.SetBinding(Label.TextProperty, nameof(DexRow.No));
            View Check(string label, string prop)
            {
                var c = new CheckBox { Color = T.Accent }; c.SetBinding(CheckBox.IsCheckedProperty, new Binding(prop, BindingMode.TwoWay));
                return new HorizontalStackLayout { Spacing = 0, Children = { c, T.L(label, 12) } };
            }
            g.Add(img, 0); g.Add(new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { n, no } }, 1);
            if (sep) g.Add(Check("본 것", nameof(DexRow.Seen)), 2);
            g.Add(Check(sep ? "잡음" : "등록", nameof(DexRow.Caught)), 3);
            return g;
        });
        var hint = T.L(sep ? "체크하면 바로 세이브 도감에 기록 · 잡음은 본 것도 함께" : "체크하면 바로 세이브 도감에 등록 (폼·성별 기록 포함) · 해제하면 그 종 기록을 지움", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new Grid
        {
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star) }, RowSpacing = 8,
            Children = { hint.Also(v => Grid.SetRow(v, 0)), q.Also(v => Grid.SetRow(v, 1)), T.Btns(3, bAll, bMiss, bGot).Also(v => Grid.SetRow(v, 2)), count.Also(v => Grid.SetRow(v, 3)), list.Also(v => Grid.SetRow(v, 4)) },
        });
        Mode(0);
    }

    private static void Tint(Button b, bool on)
    {
        if (on) { b.BackgroundColor = T.Accent; b.TextColor = Colors.White; b.BorderWidth = 0; }
        else
        {
            b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24"));
            b.SetAppThemeColor(Button.TextColorProperty, T.Accent, Color.FromArgb("#A5B4FC")); b.BorderWidth = 1;
        }
    }

    private void Filter()
    {
        IEnumerable<DexRow> r = rows;
        if (mode == 1) r = r.Where(x => !x.Caught); else if (mode == 2) r = r.Where(x => x.Caught);
        if (query.Length > 0)
            r = int.TryParse(query.TrimStart('0'), out var no) ? r.Where(x => x.Species == no || x.Species.ToString().StartsWith(query.TrimStart('0'))) : r.Where(x => Korean.Match(x.Name, query));
        list.ItemsSource = r.ToList();
        UpdateCount();
    }

    private void UpdateCount() => count.Text = $"잡은 것 {rows.Count(x => x.Caught)} · 본 것 {rows.Count(x => x.Seen || x.Caught)} / {rows.Count}종";
}
