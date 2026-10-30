using System.IO.Compression;
using PKHeX.Core;
namespace PKHeXKR;

/// <summary>레이드 편집 (SV 테라 레이드 · 소드실드 맥스 레이드 굴). 메뉴 → 도구, 해당 게임 세이브일 때만.</summary>
public class RaidSheet : Sheet
{
    public static bool Supported(SaveFile s) => s is SAV9SV || s is SAV8SWSH;

    public record Row(int Index, string Text, string Sub, string Extra, ImageSource Sprite, bool Shiny, string Search);

    internal readonly SaveFile sav = AppState.Sav;
    internal readonly SAV9SV sv; internal readonly SAV8SWSH swsh;
    internal readonly (string Name, TeraRaidMapParent Map, RaidSpawnList9 List)[] lists9 = [];
    internal readonly (string Name, RaidSpawnList8 List)[] lists8 = [];
    internal int region, progress;
    internal RaidData9.SaveEvent ev;
    private readonly Dictionary<(int, int), object> undo = new();   // (지역, 번호) → 처음 값
    private readonly CollectionView list = new() { SelectionMode = SelectionMode.None };
    private readonly Label status = T.L("", 12, sub: true);
    private readonly List<Button> regions = [];
    private Button progBtn;
    private string filter = "";
    internal CancellationTokenSource search;

    public RaidSheet() : base(AppState.Sav is SAV9SV ? "테라 레이드 편집" : "맥스 레이드 굴 편집", 0.92)
    {
        sv = sav as SAV9SV; swsh = sav as SAV8SWSH;
        if (sv != null) { lists9 = RaidTools.Lists(sv); progress = RaidTools.Progress(sv); ev = RaidData9.ReadSave(sv); }
        if (swsh != null) lists8 = RaidTools.Lists(swsh);
        Closed += () => search?.Cancel();
        status.LineBreakMode = LineBreakMode.WordWrap;

        list.ItemTemplate = new DataTemplate(() =>
        {
            var g = new Grid { ColumnDefinitions = { new(new GridLength(48)), new(GridLength.Star) }, ColumnSpacing = 8, Padding = new Thickness(4, 3) };
            var img = new Image { WidthRequest = 44, HeightRequest = 38 }; img.SetBinding(Image.SourceProperty, "Sprite");
            var star = new Image { Source = "rare_icon.png", WidthRequest = 12, HeightRequest = 12, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start };
            star.SetBinding(IsVisibleProperty, "Shiny");
            var cell = new Grid(); cell.Add(img); cell.Add(star);
            var t = T.L("", 14); t.SetBinding(Label.TextProperty, new Binding("Text", converter: Loc.Conv));
            var s = T.L("", 11, sub: true); s.SetBinding(Label.TextProperty, new Binding("Sub", converter: Loc.Conv));
            var x = T.L("", 10, sub: true); x.SetBinding(Label.TextProperty, new Binding("Extra", converter: Loc.Conv)); x.SetBinding(IsVisibleProperty, "Extra", converter: new NotEmpty());
            g.Add(cell, 0); g.Add(new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center, Children = { t, s, x } }, 1);
            var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => { if (g.BindingContext is Row r) Open(r.Index); }; g.GestureRecognizers.Add(tap);
            return g;
        });

        var bulk = new BtnGrid(2);
        void B(string text, Action a) => bulk.Add(text, a);
        if (sv != null)
        {
            B("모두 켜기", () => All9(r => { if (r.AreaID != 0) r.IsEnabled = true; }, "모든 레이드를 켰습니다"));
            B("모두 이로치 (종 유지)", () => _ = AllShiny9());
            progBtn = bulk.Add($"진행도: {ProgressName(progress)}", PickProgress);
            B("이벤트 레이드 가져오기", () => _ = ImportEvent());
        }
        else
        {
            B("모든 굴 켜기", () => All8(d => { if (!d.IsActive) d.Activate(d.Stars, d.RandRoll); }, "모든 굴을 켰습니다"));
            B("모두 희귀", () => All8(d => { if (!d.IsActive) d.Activate(d.Stars, d.RandRoll); d.IsRare = true; }, "모든 굴을 희귀로 바꿨습니다"));
            B("모두 이로치 시드", () => _ = AllShiny8());
            B("와트 다시 받기", () => All8(d => d.WattsHarvested = false, "모든 굴의 와트를 다시 받을 수 있습니다"));
            B("이벤트 굴 가져오기", () => _ = ImportEvent());
        }
        B("처음 값으로", Revert);
        var top = new VerticalStackLayout { Spacing = 8 };
        top.Children.Add(new Label { Text = sv != null ? "레이드를 누르면 자세히 보고 바꿀 수 있어요. 미리보기는 지금 세이브 트레이너 기준이고, 바꾼 뒤 세이브 내보내기로 저장하세요." : "굴을 누르면 자세히 보고 바꿀 수 있어요. 종은 굴·별·뽑기 값으로, 이로치·개체값은 시드로 정해져요. 바꾼 뒤 세이브 내보내기로 저장하세요.", FontSize = 12, LineBreakMode = LineBreakMode.WordWrap }.Also(T.Sub));

        int n = sv != null ? lists9.Length : lists8.Length;
        var regionGrid = T.Cols(Math.Max(1, n), 8);
        for (int i = 0; i < n; i++)
        {
            int k = i; var p = T.Pill(sv != null ? lists9[i].Name : lists8[i].Name, size: 12);
            p.HorizontalOptions = LayoutOptions.Fill; p.Padding = new Thickness(4, 4);
            p.Clicked += (_, _) => { region = k; Fill(); };
            regions.Add(p); regionGrid.Add(p, i, 0);
        }
        top.Children.Add(regionGrid); top.Children.Add(bulk.View);
        if (AppState.LiveConnected)   // 라이브헥스 연결 중에만 (없으면 자리도 차지하지 않음)
        {
            var live = new BtnGrid(2);
            live.Add("🎮 게임에서 읽기", LiveRead);
            live.Add("🎮 게임에 쓰기", LiveWrite, true);
            top.Children.Add(live.View);
        }
        var find = T.Input(placeholder: sv != null ? "목록에서 찾기: 종·테라·이로치·보상 (예: 특성패치, 이로치)" : "목록에서 찾기: 별·희귀·이로치");
        find.TextChanged += (_, e) => { filter = (e.NewTextValue ?? "").Trim(); Fill(); };
        if (sv != null)
        {
            var seek = T.Pill("🔍 조건으로 시드 찾기 (종·성격·개체값·크기·테라·보상)", size: 12);
            seek.HorizontalOptions = LayoutOptions.Fill;
            seek.Clicked += (_, _) => { var l = lists9[region].List; int first = Enumerable.Range(0, Math.Min(l.CountUsed, l.CountAll)).FirstOrDefault(i => l.GetRaid(i).AreaID != 0); SheetHost.Show(new RaidSearchSheet(this, region, first)); };
            top.Children.Add(seek);
        }
        top.Children.Add(find); top.Children.Add(status);
        Body.RowDefinitions.Add(new(GridLength.Auto)); Body.RowDefinitions.Add(new(GridLength.Star));
        Body.Add(top, 0, 0); Body.Add(list, 0, 1);
        if (n == 0) { status.Text = "이 세이브에는 레이드 정보가 없습니다"; return; }
        Fill();
    }

    private sealed class NotEmpty : IValueConverter
    {
        public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c) => v is string s && s.Length > 0;
        public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotSupportedException();
    }

    internal static string ProgressName(int p) => p switch { 0 => "☆1~2", 1 => "☆3 해금", 2 => "☆4 해금", 3 => "☆5 해금", _ => "☆6 해금" };

    private void PickProgress()
    {
        var items = Enumerable.Range(0, 5).Select(i => new ComboItem(ProgressName(i) + (i == RaidTools.Progress(sv) ? "  · 세이브 진행도" : ""), i)).ToList();
        SheetHost.Show(new PickerSheet("미리보기 진행도 (별 개수 뽑기 표)", items, progress, c => { progress = c.Value; if (progBtn != null) progBtn.Text = $"진행도: {ProgressName(progress)}"; Fill(); Note.Show("미리보기 진행도: " + ProgressName(progress)); }));
    }

    private void Highlight()
    {
        for (int i = 0; i < regions.Count; i++)
        {
            var b = regions[i]; bool on = i == region;
            b.FontAttributes = on ? FontAttributes.Bold : FontAttributes.None;
            if (on) { b.BackgroundColor = T.Accent; b.TextColor = Colors.White; }
            else { b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); b.SetAppThemeColor(Button.TextColorProperty, T.Accent, Color.FromArgb("#A5B4FC")); }
        }
    }

    /// <summary>같은 너비로 가지런히 놓는 버튼 칸 (열 수 고정).</summary>
    internal sealed class BtnGrid(int cols)
    {
        public readonly Grid View = T.Cols(cols, 8);
        private int n;
        public Button Add(string text, Action a, bool primary = false)
        {
            var b = T.Pill(text, primary, 12);
            b.HorizontalOptions = LayoutOptions.Fill; b.Padding = new Thickness(6, 4);
            b.Clicked += (_, _) => a();
            if (n % cols == 0) View.RowDefinitions.Add(new(GridLength.Auto));
            View.Add(b, n % cols, n / cols); n++;
            return b;
        }
    }

    // ───── 미리보기 ─────
    /// <summary>SV 레이드 한 칸의 미리보기 (일반·검은 결정은 PKHeX 조우, 배포·최강은 세이브의 이벤트).</summary>
    internal RaidTools.Tera Preview9(int rg, int i, uint? seed = null, TeraRaidContentType? content = null, bool rewards = true)
    {
        var (_, map, l) = lists9[rg]; var r = l.GetRaid(i);
        var ct = content ?? r.Content; var sd = seed ?? r.Seed;
        if (ct is TeraRaidContentType.Base05 or TeraRaidContentType.Black6) return RaidTools.Preview(sv, map, sd, ct, progress, rewards);
        if (ev is not { Any: true }) return null;
        return RaidTools.PreviewEvent(sv, ev, RaidData9.GroupOf(ev, l, i, sav.Version, progress), sd, ct, progress, rewards);
    }

    internal static string TeraName(int t) => t == 99 ? "스텔라" : t >= 0 && t < GameInfo.Strings.types.Length ? GameInfo.Strings.types[t] : "?";

    // ───── 목록 ─────
    internal void Fill()
    {
        Highlight();
        var rows = new List<Row>();
        string q = filter.ToLowerInvariant();
        if (sv != null)
        {
            var (name, map, l) = lists9[region]; int on = 0, shiny = 0, total = 0;
            for (int i = 0; i < l.CountUsed && i < l.CountAll; i++)
            {
                var r = l.GetRaid(i);
                if (r.AreaID == 0 && !r.IsEnabled) continue;
                total++;
                var t = Preview9(region, i);
                if (r.IsEnabled) on++; if (t?.Shiny == true && r.IsEnabled) shiny++;
                var text = Text9(i, r, t); var sub = Sub9(r, t); var rw = t == null ? "" : RaidData9.Text(t.Rewards, 6);
                var hay = $"{text} {sub} {rw}".ToLowerInvariant();
                if (q.Length > 0 && !q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => hay.Contains(w) || Korean.Match(hay, w))) continue;
                rows.Add(new Row(i, text, sub, rw.Length > 0 ? "보상: " + rw : "", t != null ? AppState.Sprite(t.Pk) : null, t?.Shiny == true, hay));
            }
            status.Text = $"{name} · 레이드 {total}개 · 켜짐 {on} · 이로치 {shiny} · 진행도 {ProgressName(progress)}"
                + (ev is { Any: true } ? $" · 이벤트 {ev.Identifier}" : "") + (q.Length > 0 ? $" · 찾은 {rows.Count}" : "");
        }
        else
        {
            var l = lists8[region].List; int on = 0, shiny = 0;
            for (int i = 0; i < l.CountUsed && i < l.CountAll; i++)
            {
                var d = l.GetRaid(i); var s = RaidTools.Shiny8(d.Seed);
                if (d.IsActive) on++; if (d.IsActive && s > 0) shiny++;
                var text = Text8(i, d); var sub = Sub8(d);
                var hay = $"{text} {sub}".ToLowerInvariant();
                if (q.Length > 0 && !q.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(w => hay.Contains(w))) continue;
                rows.Add(new Row(i, text, sub, "", null, d.IsActive && s > 0, hay));
            }
            status.Text = $"{lists8[region].Name} · 굴 {l.CountUsed}개 · 켜짐 {on} · 이로치 시드 {shiny}" + (q.Length > 0 ? $" · 찾은 {rows.Count}" : "");
        }
        list.ItemsSource = rows;
    }

    internal string Text9(int i, TeraRaidDetail r, RaidTools.Tera t)
    {
        var name = t == null ? (r.Content is TeraRaidContentType.Distribution or TeraRaidContentType.Might7 ? RaidTools.ContentName(r.Content) + (ev is { Any: true } ? "" : " (이벤트 정보 없음)") : "?") : $"{t.Stars}★ {AppState.SpeciesName(t.Pk)}";
        return $"#{i + 1} {name}{(r.IsEnabled ? "" : " · 꺼짐")}";
    }
    internal static string Sub9(TeraRaidDetail r, RaidTools.Tera t)
    {
        var tera = t == null ? "" : $"테라 {TeraName(t.TeraType)} · ";
        return $"{tera}{(t?.Shiny == true ? (t.Pk.ShinyXor == 0 ? "네모 이로치 · " : "별 이로치 · ") : "")}시드 {r.Seed:X8} · {RaidTools.ContentName(r.Content)}{(t?.Group > 0 ? $" · 그룹 {t.Group}" : "")}";
    }
    internal static string Text8(int i, RaidSpawnDetail d) => $"#{i + 1} 굴 {(d.IsActive ? $"{d.Stars + 1}★ · {RaidTools.DenName(d.DenType)}" : "· 비활성")}";
    internal static string Sub8(RaidSpawnDetail d)
    {
        var s = RaidTools.Shiny8(d.Seed);
        return $"{(s == 2 ? "네모 이로치 · " : s == 1 ? "별 이로치 · " : "")}시드 {d.Seed:X16} · 뽑기 {d.RandRoll}{(d.IsActive && d.WattsHarvested ? " · 와트 받음" : "")}";
    }

    private void Open(int i)
    {
        if (sv != null) SheetHost.Show(new RaidDetailSheet(this, region, i));
        else SheetHost.Show(new DenDetailSheet(this, region, i));
    }

    internal void Remember(int rg, int i)
    {
        if (undo.ContainsKey((rg, i))) return;
        if (sv != null) { var r = lists9[rg].List.GetRaid(i); undo[(rg, i)] = (r.IsEnabled, r.Seed, r.Content, r.IsClaimedLeaguePoints); }
        else { var d = lists8[rg].List.GetRaid(i); undo[(rg, i)] = (d.Seed, d.Stars, d.RandRoll, d.DenType, d.Flags); }
    }

    private void Revert()
    {
        if (undo.Count == 0) { Note.Show("바꾼 레이드가 없습니다"); return; }
        foreach (var ((rg, i), v) in undo)
        {
            if (sv != null && v is ValueTuple<bool, uint, TeraRaidContentType, bool> a) { var r = lists9[rg].List.GetRaid(i); (r.IsEnabled, r.Seed, r.Content, r.IsClaimedLeaguePoints) = a; }
            else if (v is ValueTuple<ulong, byte, byte, RaidType, byte> b) { var d = lists8[rg].List.GetRaid(i); d.Seed = b.Item1; d.Stars = b.Item2; d.RandRoll = b.Item3; d.DenType = b.Item4; d.Flags = b.Item5; }
        }
        undo.Clear(); Fill(); Note.Show("처음 값으로 되돌렸습니다");
    }

    internal void Changed(string msg) { Fill(); if (msg != null) Note.Show(msg + " · 세이브 내보내기로 저장하세요"); }

    // ───── 일괄 (SV) ─────
    private void All9(Action<TeraRaidDetail> a, string msg)
    {
        var l = lists9[region].List;
        for (int i = 0; i < l.CountUsed && i < l.CountAll; i++) { Remember(region, i); a(l.GetRaid(i)); }
        Changed(msg);
    }

    private async Task AllShiny9()
    {
        var (_, map, l) = lists9[region];
        if (!await Application.Current.Windows[0].Page.DisplayAlertTr("모두 이로치", "켜진 레이드를 같은 포켓몬의 이로치 시드로 바꿉니다 (일반·검은 결정만). 계속할까요?", "바꾸기", "취소")) return;
        search?.Cancel(); search = new CancellationTokenSource(TimeSpan.FromSeconds(180)); var tok = search.Token;
        var ver = sav.Version; int prog = progress, done = 0, fail = 0, all = l.CountUsed;
        using var bg = BgTask.Begin("레이드 이로치 시드 찾기", search);
        for (int i = 0; i < l.CountUsed && i < l.CountAll && !tok.IsCancellationRequested; i++)
        {
            var r = l.GetRaid(i);
            if (!r.IsEnabled || r.AreaID == 0 || r.Content is not (TeraRaidContentType.Base05 or TeraRaidContentType.Black6)) continue;
            var t = RaidTools.Preview(sv, map, r.Seed, r.Content, prog, false);
            if (t == null || t.Shiny || t.Enc.Shiny == Shiny.Never) continue;
            status.Text = $"이로치 시드 찾는 중… #{i + 1}"; bg.Report($"#{i + 1} {AppState.SpeciesName(t.Pk)}", i, all);
            var content = r.Content; var e = t.Enc;
            var f = await Task.Run(() => RaidTools.Find9(ver, map, content, prog, e, true, tok, 5_000_000));
            if (f == null) { fail++; continue; }
            Remember(region, i); r.Seed = f.Value; done++;
        }
        var msg = $"레이드 {done}개를 이로치 시드로 바꿨습니다{(fail > 0 ? $" (못 찾음 {fail})" : "")}";
        bg.Done(msg); Changed(msg);
    }

    internal void CopyToAll9(int rg, int index, bool seedToo)
    {
        var l = lists9[rg].List;
        for (int i = 0; i < l.CountUsed && i < l.CountAll; i++) Remember(rg, i);
        l.Propagate(index, seedToo);
        Changed("모든 레이드에 복사했습니다");
    }

    // ───── 일괄 (소드실드) ─────
    private void All8(Action<RaidSpawnDetail> a, string msg)
    {
        var l = lists8[region].List;
        for (int i = 0; i < l.CountUsed && i < l.CountAll; i++) { Remember(region, i); a(l.GetRaid(i)); }
        Changed(msg);
    }

    private async Task AllShiny8()
    {
        var l = lists8[region].List;
        search?.Cancel(); search = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var tok = search.Token;
        int n = 0;
        for (int i = 0; i < l.CountUsed && i < l.CountAll && !tok.IsCancellationRequested; i++)
        {
            var d = l.GetRaid(i);
            if (!d.IsActive || RaidTools.Shiny8(d.Seed) > 0) continue;
            var f = await Task.Run(() => RaidTools.Find8(false, tok));
            if (f == null) continue;
            Remember(region, i); d.Seed = f.Value; n++;
        }
        Changed($"켜진 굴 {n}개를 이로치 시드로 바꿨습니다");
    }

    // ───── 이벤트 레이드 파일 ─────
    private async Task ImportEvent()
    {
        var page = Application.Current.Windows[0].Page;
        var how = sv != null
            ? "Project Pokemon EventsGallery의 SV 'Raid Events/이벤트/Files' 파일들(raid_enemy_array·fixed_reward_item_array·lottery_reward_item_array·raid_priority_array·event_raid_identifier, _3_0_0 등 패치 이름 그대로)을 모두 고르거나, 그 폴더를 압축한 .zip을 고르세요."
            : "Project Pokemon EventsGallery의 소드실드 'Wild Area Events/Index NNN' 파일들(normal_encount·drop_rewards·bonus_rewards 등)을 모두 고르거나, 그 폴더를 압축한 .zip을 고르세요.";
        if (!await page.DisplayAlertTr("이벤트 레이드 가져오기", how + "\n\n세이브에 받아 둔 이벤트가 이것으로 바뀝니다. 게임에서 포켓포털 뉴스·Y통신으로 새 이벤트를 받으면 다시 바뀝니다.", "파일 고르기", "취소")) return;
        IEnumerable<FileResult> picked;
        try { picked = await FilePicker.PickMultipleAsync(); } catch { return; }
        if (picked == null) return;
        var files = new Dictionary<string, byte[]>();
        foreach (var f in picked)
        {
            if (f == null) continue;
            using var st = await f.OpenReadAsync(); using var ms = new MemoryStream();
            if (st.CanSeek && st.Length > 32 << 20) { Note.Show($"{f.FileName}: 파일이 너무 큽니다"); continue; }
            await st.CopyToAsync(ms);
            var data = ms.ToArray();
            if (f.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var z = new ZipArchive(new MemoryStream(data));
                    foreach (var e in z.Entries)
                    {
                        if (e.Length == 0 || e.Length > 1 << 20) continue;
                        using var es = e.Open(); using var em = new MemoryStream();
                        var buf = new byte[65536]; int r;   // 선언된 크기는 믿지 않고 실제로 풀리는 양을 1MB로 제한
                        while ((r = es.Read(buf, 0, buf.Length)) > 0) { em.Write(buf, 0, r); if (em.Length > 1 << 20) break; }
                        if (em.Length > 1 << 20) continue;
                        files[e.Name] = em.ToArray();
                    }
                }
                catch { Note.Show("압축 파일을 읽지 못했습니다"); return; }
            }
            else files[f.FileName] = data;
        }
        if (sv != null)
        {
            int n = RaidData9.Import(sv, files, out var err);
            if (n == 0) { Note.Show("가져오지 못했습니다: " + (err ?? "파일 이름을 확인하세요")); return; }
            ev = RaidData9.ReadSave(sv);
            Changed($"이벤트 {(ev?.Identifier ?? 0)}를 세이브에 넣었습니다 ({n}개 파일){(err != null ? " · " + err : "")} — 배포·최강 레이드 미리보기에 쓰입니다");
        }
        else
        {
            var errs = new List<string>(); var done = RaidTools.Import8(swsh, files, errs);
            if (done.Count == 0) { Note.Show("가져오지 못했습니다: " + (errs.Count > 0 ? string.Join(", ", errs) : "normal_encount 등의 파일 이름을 확인하세요")); return; }
            Changed($"이벤트 굴 정보를 세이브에 넣었습니다 ({string.Join(", ", done)}){(errs.Count > 0 ? " · " + string.Join(", ", errs) : "")} — 굴 종류를 '이벤트'로 바꾸면 그 이벤트가 나옵니다");
        }
    }

    // ───── 라이브헥스 ─────
    /// <summary>PKHeX.Core.Injection 블록 이름 (SV: Raid·RaidKitakami·RaidBlueberry, 소드실드: Raid·RaidArmor·RaidCrown).</summary>
    private string LiveBlock(int rg) => sv != null
        ? lists9[rg].Map switch { TeraRaidMapParent.Kitakami => "RaidKitakami", TeraRaidMapParent.Blueberry => "RaidBlueberry", _ => "Raid" }
        : lists8[rg].Name switch { "갑옷의 외딴섬" => "RaidArmor", "왕관의 설원" => "RaidCrown", _ => "Raid" };

    private int RegionCount => sv != null ? lists9.Length : lists8.Length;

    private async void LiveRead()
    {
        if (!AppState.LiveConnected) { Note.Show("라이브헥스가 연결되어 있지 않습니다"); return; }
        if (undo.Count > 0 && !await Application.Current.Windows[0].Page.DisplayAlertTr("게임에서 읽기", "바꾼 내용을 버리고 게임의 레이드로 바꿀까요?", "읽기", "취소")) return;
        status.Text = "게임에서 읽는 중…";
        var names = Enumerable.Range(0, RegionCount).Select(LiveBlock).Distinct().ToList();
        var r = await Task.Run(() =>
        {
            int ok = 0; string err = null;
            foreach (var name in names)
            {
                try
                {
                    bool got; List<byte[]> read;
                    lock (AppState.LiveLock) got = AppState.Remote.Injector.ReadBlockFromString(AppState.Remote, sav, name, out read);
                    if (got && read is { Count: > 0 }) ok++;
                }
                catch (Exception ex) { err = ex.Message; }
            }
            return (ok, err);
        });
        undo.Clear(); Fill();
        Note.Show(r.ok > 0 ? $"게임에서 레이드를 읽었습니다 ({r.ok}/{names.Count}) · 세이브에도 반영됨" : r.err != null ? "읽기 실패: " + r.err : "이 게임 버전은 레이드 읽기를 지원하지 않습니다");
    }

    private async void LiveWrite()
    {
        if (!AppState.LiveConnected) { Note.Show("라이브헥스가 연결되어 있지 않습니다"); return; }
        if (!await Application.Current.Windows[0].Page.DisplayAlertTr("게임에 쓰기", "지금 목록의 레이드를 게임 메모리에 씁니다. 게임에서 레이드가 바로 안 바뀌면 화면을 한 번 바꾸거나(맵 이동·메뉴), 저장 후 다시 켜서 확인하세요.", "쓰기", "취소")) return;
        status.Text = "게임에 쓰는 중…";
        var names = Enumerable.Range(0, RegionCount).Select(LiveBlock).Distinct().ToList();
        var err = await Task.Run(() =>
        {
            try { lock (AppState.LiveLock) foreach (var name in names) AppState.Remote.Injector.WriteBlocksFromSAV(AppState.Remote, name, sav); return null; }
            catch (Exception ex) { return ex.Message; }
        });
        Fill();
        Note.Show(err == null ? "게임에 레이드를 썼습니다" : "쓰기 실패: " + err);
    }
}

internal static class ViewExt
{
    public static T Also<T>(this T v, Action<T> a) { a(v); return v; }
}

/// <summary>SV 레이드 한 칸: 미리보기·보상·편집 (스크롤).</summary>
public class RaidDetailSheet : Sheet
{
    private readonly RaidSheet o; private readonly int rg, i;
    private readonly VerticalStackLayout body = new() { Spacing = 10 };

    public RaidDetailSheet(RaidSheet owner, int region, int index) : base($"#{index + 1} · {owner.lists9[region].Name}", 0.88)
    {
        o = owner; rg = region; i = index;
        Body.Add(new ScrollView { Content = body });
        Render();
    }

    private void Changed(string msg) { o.Changed(msg); Render(); }

    private void Render()
    {
        body.Children.Clear();
        var (_, map, l) = o.lists9[rg]; var r = l.GetRaid(i);
        var t = o.Preview9(rg, i);
        var head = new Grid { ColumnDefinitions = { new(new GridLength(72)), new(GridLength.Star) }, ColumnSpacing = 10 };
        var img = new Image { WidthRequest = 68, HeightRequest = 58, Source = t != null ? AppState.Sprite(t.Pk) : null };
        var info = T.L("", 13); info.LineBreakMode = LineBreakMode.WordWrap;
        if (t == null)
            info.Text = r.Content is TeraRaidContentType.Distribution or TeraRaidContentType.Might7
                ? (o.ev is { Any: true } ? "이 진행도·버전에서 이 굴에 나오는 이벤트 조우를 찾지 못했습니다" : "배포·최강 레이드는 이벤트 정보가 필요합니다. 목록 화면의 '이벤트 레이드 가져오기'로 이벤트 파일을 넣으세요.")
                : "이 시드로 나오는 조우를 찾지 못했습니다";
        else
        {
            var p = t.Pk; var s = GameInfo.Strings;
            info.Text = $"{t.Stars}★ {AppState.SpeciesName(p)}{(t.Shiny ? (p.ShinyXor == 0 ? " · 네모 이로치" : " · 별 이로치") : "")} · 테라 {RaidSheet.TeraName(t.TeraType)}\n"
                + $"{s.natures[(int)p.Nature]} · {s.abilitylist[p.Ability]} · {(p.Gender == 0 ? "♂" : p.Gender == 1 ? "♀" : "성별 없음")} · Lv{p.CurrentLevel}\n"
                + $"개체값 {p.IV_HP}/{p.IV_ATK}/{p.IV_DEF}/{p.IV_SPA}/{p.IV_SPD}/{p.IV_SPE} · 크기 {p.Scale} (키 {p.HeightScalar}, 몸무게 {p.WeightScalar})"
                + (t.Ev != null ? $"\n이벤트 {o.ev.Identifier} · 그룹 {t.Group}{(t.Ev.CaptureRate == 0 ? " · 잡을 수 없음" : t.Ev.CaptureRate == 2 ? " · 세이브당 한 번" : "")}" : "")
                + $"\n장소 {r.AreaID}-{r.LotteryGroup}-{r.SpawnPointID} · 미리보기 진행도 {RaidSheet.ProgressName(o.progress)}";
        }
        head.Add(img, 0); head.Add(info, 1);
        body.Children.Add(T.Card(head));

        if (t != null && t.Rewards.Count > 0)   // 보상
        {
            var items = GameInfo.Strings.itemlist;
            string N(int id) => id < items.Length ? items[id] : id.ToString();
            var main = RaidData9.HostSummary(t.Rewards);
            var power = t.Rewards.Where(x => x.Subject == 4).GroupBy(x => x.Id).Select(g => $"{N(g.Key)}×{g.Sum(x => x.Amount)}").ToList();
            var charm = t.Rewards.Where(x => x.Subject == 5).Select(x => $"{N(x.Id)}×{x.Amount}").ToList();
            var guest = t.Rewards.Where(x => x.Subject == 2).Select(x => $"{N(x.Id)}×{x.Amount}").ToList();
            var rl = T.L("", 13); rl.LineBreakMode = LineBreakMode.WordWrap;
            rl.Text = "보상 (호스트): " + string.Join(", ", main.Select(x => $"{N(x.Id)}×{x.Amount}"))
                + (power.Count > 0 ? "\n레이드 파워(보상 늘리기) 추가분: " + string.Join(", ", power) : "")
                + (charm.Count > 0 ? "\n빛나는 부적이 있으면: " + string.Join(", ", charm) : "")
                + (guest.Count > 0 ? "\n손님만: " + string.Join(", ", guest) : "");
            body.Children.Add(T.Card(rl));
        }

        var en = new Switch { IsToggled = r.IsEnabled }; en.Toggled += (_, e) => { o.Remember(rg, i); r.IsEnabled = e.Value; Changed(null); };
        var lp = new Switch { IsToggled = r.IsClaimedLeaguePoints }; lp.Toggled += (_, e) => { o.Remember(rg, i); r.IsClaimedLeaguePoints = e.Value; };
        var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 6 };
        row.Add(T.L("켜기", 13), 0); row.Add(en, 1); row.Add(T.L("LP 받음", 13), 3); row.Add(lp, 4);
        body.Children.Add(row);

        var seed = T.Input(placeholder: "시드 (16진수 8자리)"); seed.Text = r.Seed.ToString("X8");
        var (cv, ct) = T.Chooser(() => SheetHost.Show(new PickerSheet("레이드 종류",
            Enum.GetValues<TeraRaidContentType>().Select(c => new ComboItem(RaidTools.ContentName(c), (int)c)).ToList(), (int)r.Content,
            c => { o.Remember(rg, i); r.Content = (TeraRaidContentType)c.Value; Changed("레이드 종류를 바꿨습니다"); })));
        ct.Text = RaidTools.ContentName(r.Content);
        var g = T.Cols(2); g.Add(T.Field("시드", seed), 0); g.Add(T.Field("종류", cv), 1);
        body.Children.Add(g);

        var btns = new RaidSheet.BtnGrid(2);
        btns.Add("시드 적용", () =>
        {
            if (!uint.TryParse((seed.Text ?? "").Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase), System.Globalization.NumberStyles.HexNumber, null, out var v)) { Note.Show("16진수 시드를 넣으세요"); return; }
            o.Remember(rg, i); r.Seed = v; Changed("시드를 바꿨습니다");
        }, true);
        btns.Add("무작위 시드", () => { o.Remember(rg, i); r.Seed = (uint)Random.Shared.NextInt64(0, 1L << 32); Changed("시드를 바꿨습니다"); });
        btns.Add("이로치로 (종 유지)", () => _ = Shiny(t));
        btns.Add("🔍 조건으로 시드 찾기", () => SheetHost.Show(new RaidSearchSheet(o, rg, i, this)));
        btns.Add("이 레이드를 모두에 복사", async () =>
        {
            var pick = await Application.Current.Windows[0].Page.DisplayActionSheetTr($"#{i + 1} 레이드를 이 지역 모든 레이드에 복사", "취소", null, "켜기·종류만", "시드까지 (모두 같은 포켓몬)");
            if (pick is null or "취소") return;
            o.CopyToAll9(rg, i, pick.StartsWith("시드")); Render();
        });
        if (t != null)
        {
            bool legal = new LegalityAnalysis(t.Pk).Valid;
            btns.Add(legal ? "편집기로 불러오기" : "편집기로 불러오기 (합법 아님)", () => { if (AppState.Load(t.Pk.Clone())) { Note.Show(legal ? "레이드 개체를 편집기로 불러왔습니다" : "불러왔습니다 · PKHeX 판정은 합법이 아닙니다 (최신 이벤트일 수 있음)"); Close(); o.Close(); } });
        }
        body.Children.Add(btns.View);
    }

    public void Apply(uint seed, TeraRaidContentType content)
    {
        var r = o.lists9[rg].List.GetRaid(i);
        o.Remember(rg, i); r.Seed = seed; r.Content = content; r.IsEnabled = true;
        Changed("시드를 바꿨습니다");
    }

    private async Task Shiny(RaidTools.Tera t)
    {
        var r = o.lists9[rg].List.GetRaid(i);
        if (t == null) { Note.Show("미리보기가 안 되는 레이드입니다"); return; }
        if (t.Shiny) { Note.Show("이미 이로치입니다"); return; }
        var crit = new RaidTools.Criteria { Species = t.Species, Form = t.Form, Stars = t.Stars, Shiny = 1 };
        o.search?.Cancel(); o.search = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var tok = o.search.Token;
        Note.Show("이로치 시드 찾는 중…");
        var content = r.Content;
        var (q, make) = RaidSearchSheet.Funcs(o, rg, i, content);
        var res = await Task.Run(() => RaidTools.Search(q, make, crit, 1, TimeSpan.FromSeconds(60), tok, null, t.Ev == null || t.Ev.Shiny == 0));
        if (res.Count == 0) { Note.Show(t.Ev?.Shiny == 1 || t.Enc?.Shiny == PKHeX.Core.Shiny.Never ? "이 레이드 포켓몬은 이로치가 나오지 않습니다" : "시드를 찾지 못했습니다"); return; }
        Apply(res[0].Seed, content);
    }
}

/// <summary>SV 레이드 조건 검색: 종(폼)·별·이로치·테라 타입·성격·성별·특성·개체값·크기·보상으로 시드 찾기.</summary>
public class RaidSearchSheet : Sheet
{
    private readonly RaidSheet o; private readonly int rg, i; private readonly RaidDetailSheet detail;
    private readonly RaidTools.Criteria c = new();
    private TeraRaidContentType content;
    private readonly Label status = T.L("", 12, sub: true);
    private readonly VerticalStackLayout results = new() { Spacing = 6 };
    private readonly Label speciesL, starL, shinyL, teraL, natureL, genderL, abilityL, rewardL;
    private readonly Entry[] ivMin = new Entry[6], ivMax = new Entry[6];
    private readonly Entry scaleMin = T.Input(Keyboard.Numeric, "0"), scaleMax = T.Input(Keyboard.Numeric, "255"), rewardMin = T.Input(Keyboard.Numeric, "1");
    private Button go;

    public RaidSearchSheet(RaidSheet owner, int region, int index, RaidDetailSheet from = null) : base("레이드 조건 검색", 0.9)
    {
        o = owner; rg = region; i = index; detail = from;
        var r = o.lists9[rg].List.GetRaid(i);
        content = r.Content is TeraRaidContentType.Distribution or TeraRaidContentType.Might7 && o.ev is not { Any: true } ? TeraRaidContentType.Base05 : r.Content;
        var s = GameInfo.Strings;
        Label ctL = null;
        (var cv, ctL) = T.Chooser(() => SheetHost.Show(new PickerSheet("레이드 종류", Enum.GetValues<TeraRaidContentType>().Select(x => new ComboItem(RaidTools.ContentName(x), (int)x)).ToList(), (int)content,
            x => { content = (TeraRaidContentType)x.Value; ctL.Text = RaidTools.ContentName(content); c.Species = 0; c.Form = -1; speciesL.Text = "상관없음"; })));
        ctL.Text = RaidTools.ContentName(content);
        (var sv1, speciesL) = T.Chooser(PickSpecies); speciesL.Text = "상관없음";
        (var stv, starL) = T.Chooser(() => Pick("별", ["상관없음", "1★", "2★", "3★", "4★", "5★", "6★", "7★"], c.Stars, v => { c.Stars = v; starL.Text = v == 0 ? "상관없음" : $"{v}★"; })); starL.Text = "상관없음";
        (var shv, shinyL) = T.Chooser(() => Pick("이로치", ["상관없음", "이로치", "이로치 아님"], c.Shiny, v => { c.Shiny = v; shinyL.Text = new[] { "상관없음", "이로치", "이로치 아님" }[v]; })); shinyL.Text = "상관없음";
        (var tv, teraL) = T.Chooser(() => Pick("테라 타입", ["상관없음", .. s.types.Take(18)], c.TeraType + 1, v => { c.TeraType = v - 1; teraL.Text = v == 0 ? "상관없음" : s.types[v - 1]; })); teraL.Text = "상관없음";
        (var nv, natureL) = T.Chooser(() => Pick("성격", ["상관없음", .. s.natures.Take(25)], c.Nature + 1, v => { c.Nature = v - 1; natureL.Text = v == 0 ? "상관없음" : s.natures[v - 1]; })); natureL.Text = "상관없음";
        (var gv, genderL) = T.Chooser(() => Pick("성별", ["상관없음", "♂", "♀"], c.Gender + 1, v => { c.Gender = v - 1; genderL.Text = new[] { "상관없음", "♂", "♀" }[v]; })); genderL.Text = "상관없음";
        (var av, abilityL) = T.Chooser(() => Pick("특성", ["상관없음", "특성 1", "특성 2", "숨겨진 특성"], c.Ability + 1, v => { c.Ability = v - 1; abilityL.Text = new[] { "상관없음", "특성 1", "특성 2", "숨겨진 특성" }[v]; })); abilityL.Text = "상관없음";
        (var rv, rewardL) = T.Chooser(PickReward); rewardL.Text = "상관없음";

        var g1 = T.Cols(2, 8); g1.RowDefinitions.Add(new(GridLength.Auto)); g1.RowDefinitions.Add(new(GridLength.Auto)); g1.RowDefinitions.Add(new(GridLength.Auto)); g1.RowDefinitions.Add(new(GridLength.Auto)); g1.RowDefinitions.Add(new(GridLength.Auto));
        g1.Add(T.Field("레이드 종류", cv), 0, 0); g1.Add(T.Field("포켓몬 (폼 포함)", sv1), 1, 0);
        g1.Add(T.Field("별", stv), 0, 1); g1.Add(T.Field("이로치", shv), 1, 1);
        g1.Add(T.Field("테라 타입", tv), 0, 2); g1.Add(T.Field("성격", nv), 1, 2);
        g1.Add(T.Field("성별", gv), 0, 3); g1.Add(T.Field("특성", av), 1, 3);
        g1.Add(T.Field("보상 아이템", rv), 0, 4); g1.Add(T.Field("보상 최소 개수", rewardMin), 1, 4);

        var ivGrid = new Grid { ColumnSpacing = 4, RowSpacing = 2 };
        for (int k = 0; k < 6; k++) ivGrid.ColumnDefinitions.Add(new(GridLength.Star));
        ivGrid.RowDefinitions.Add(new(GridLength.Auto)); ivGrid.RowDefinitions.Add(new(GridLength.Auto)); ivGrid.RowDefinitions.Add(new(GridLength.Auto));
        string[] stat = ["H", "A", "B", "C", "D", "S"];
        for (int k = 0; k < 6; k++)
        {
            var l = T.L(stat[k], 12, sub: true); l.HorizontalOptions = LayoutOptions.Center; ivGrid.Add(l, k, 0);
            ivMin[k] = T.Input(Keyboard.Numeric, "0"); ivMax[k] = T.Input(Keyboard.Numeric, "31");
            ivMin[k].HorizontalTextAlignment = ivMax[k].HorizontalTextAlignment = TextAlignment.Center;
            ivGrid.Add(ivMin[k], k, 1); ivGrid.Add(ivMax[k], k, 2);
        }
        var quick = new RaidSheet.BtnGrid(3);
        quick.Add("6V", () => { foreach (var e in ivMin) e.Text = "31"; foreach (var e in ivMax) e.Text = ""; });
        quick.Add("A0 (특수형)", () => { ivMin[1].Text = ""; ivMax[1].Text = "0"; });
        quick.Add("S0 (트릭룸)", () => { ivMin[5].Text = ""; ivMax[5].Text = "0"; });
        quick.Add("가장 작게 (0)", () => { scaleMin.Text = "0"; scaleMax.Text = "0"; });
        quick.Add("가장 크게 (255)", () => { scaleMin.Text = "255"; scaleMax.Text = "255"; });
        quick.Add("지우기", Clear);
        var sg = T.Cols(2, 8); sg.Add(T.Field("크기 최소 (0~255)", scaleMin), 0); sg.Add(T.Field("크기 최대", scaleMax), 1);

        go = T.Pill("찾기 (최대 20개 · 60초)", primary: true); go.HorizontalOptions = LayoutOptions.Fill;
        go.Clicked += async (_, _) => await Run();
        var stop = T.Pill("멈추기"); stop.HorizontalOptions = LayoutOptions.Fill; stop.Clicked += (_, _) => o.search?.Cancel();
        var runRow = T.Cols(2, 8); runRow.Add(go, 0); runRow.Add(stop, 1);
        status.LineBreakMode = LineBreakMode.WordWrap;
        Body.Add(new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    T.L($"#{i + 1} 레이드({o.lists9[rg].Name})에 쓸 시드를 찾아요. 결과를 누르면 이 레이드에 넣어요. 진행도 {RaidSheet.ProgressName(o.progress)} 기준.", 12, sub: true).Also(x => x.LineBreakMode = LineBreakMode.WordWrap),
                    g1, T.L("개체값 (위: 최소, 아래: 최대 · 비우면 0~31)", 12, sub: true), ivGrid, sg, quick.View, runRow, status, results,
                }
            }
        });
        Closed += () => o.search?.Cancel();
    }

    private void Clear()
    {
        foreach (var e in ivMin) e.Text = ""; foreach (var e in ivMax) e.Text = ""; scaleMin.Text = scaleMax.Text = ""; rewardMin.Text = "";
        c.Species = 0; c.Form = -1; c.Stars = 0; c.Shiny = 0; c.TeraType = c.Nature = c.Gender = c.Ability = -1; c.RewardItem = 0;
        foreach (var l in new[] { speciesL, starL, shinyL, teraL, natureL, genderL, abilityL, rewardL }) l.Text = "상관없음";
    }

    private static void Pick(string title, IList<string> items, int cur, Action<int> on)
        => SheetHost.Show(new PickerSheet(title, items.Select((x, k) => new ComboItem(x, k)).ToList(), cur, x => on(x.Value)));

    /// <summary>이 지역·종류에서 나올 수 있는 포켓몬 (폼마다 따로).</summary>
    private void PickSpecies()
    {
        var map = o.lists9[rg].Map; var list = new List<(ushort Sp, byte Form, int Stars)>();
        if (content is TeraRaidContentType.Base05 or TeraRaidContentType.Black6)
            foreach (var st in content == TeraRaidContentType.Black6 ? new[] { 6 } : [1, 2, 3, 4, 5])
                list.AddRange(RaidTools.Possible(o.sav.Version, map, st).Select(e => (e.Species, e.Form, (int)e.Stars)));
        else if (o.ev != null)
            list.AddRange(o.ev.Rows.Where(r => r.Rate > 0 && (r.Stars == 7) == (content == TeraRaidContentType.Might7)).Select(r => (r.Species, r.Form, r.Stars)));
        var forms = list.GroupBy(x => (x.Sp, x.Form)).Select(gr => (gr.Key.Sp, gr.Key.Form, Stars: string.Join("·", gr.Select(x => x.Stars).Distinct().OrderBy(x => x)))).OrderBy(x => x.Sp).ThenBy(x => x.Form).ToList();
        var items = new List<ComboItem> { new("상관없음", -1) };
        for (int k = 0; k < forms.Count; k++)
        {
            var (sp, f, st) = forms[k];
            items.Add(new ComboItem($"{GameInfo.Strings.Species[sp]}{FormText(sp, f)}  ({st}★)", k));
        }
        SheetHost.Show(new PickerSheet("포켓몬 (폼 포함, 이 레이드 종류에서 나오는 것)", items, -1, x =>
        {
            if (x.Value < 0) { c.Species = 0; c.Form = -1; speciesL.Text = "상관없음"; return; }
            var (sp, f, _) = forms[x.Value]; c.Species = sp; c.Form = f; speciesL.Text = GameInfo.Strings.Species[sp] + FormText(sp, f);
        }, x => x.Value < 0 ? null : AppState.Sprite(forms[x.Value].Sp, forms[x.Value].Form, false)));
    }

    internal static string FormText(ushort sp, byte f)
    {
        try
        {
            var names = FormConverter.GetFormList(sp, GameInfo.Strings.types, GameInfo.Strings.forms, GameInfo.GenderSymbolUnicode, EntityContext.Gen9);
            if (names.Length <= 1) return "";
            return f < names.Length && !string.IsNullOrWhiteSpace(names[f]) ? $" ({names[f]})" : f > 0 ? $" (폼 {f})" : "";
        }
        catch { return f > 0 ? $" (폼 {f})" : ""; }
    }

    private void PickReward()
    {
        var names = GameInfo.Strings.itemlist;
        var ids = RaidData9.AllItems(o.ev).Where(x => x > 0 && x < names.Length && !string.IsNullOrWhiteSpace(names[x])).OrderBy(x => names[x]).ToList();
        var items = new List<ComboItem> { new("상관없음", 0) };
        items.AddRange(ids.Select(x => new ComboItem(names[x], x)));
        SheetHost.Show(new PickerSheet("보상 아이템 (호스트가 받는 보상)", items, c.RewardItem, x => { c.RewardItem = x.Value; rewardL.Text = x.Value == 0 ? "상관없음" : names[x.Value]; }, x => x.Value > 0 ? AppState.ItemSprite(x.Value) : null));
    }

    /// <summary>시드 → (종, 폼, 별) 빠른 거르기와 시드 → 미리보기 (일반·검은 결정은 PKHeX 조우, 배포·최강은 이벤트).</summary>
    internal static (Func<uint, (ushort, byte, int)?> Quick, Func<uint, RaidTools.Tera> Make) Funcs(RaidSheet o, int rg, int i, TeraRaidContentType content)
    {
        var (_, map, l) = o.lists9[rg]; var sav = o.sv; var ver = sav.Version; int prog = o.progress;
        if (content is TeraRaidContentType.Base05 or TeraRaidContentType.Black6)
            return (seed => { var (st, e) = RaidTools.Select(ver, map, seed, content, prog); return e == null ? null : (e.Species, e.Form, st); },
                    seed => RaidTools.Preview(sav, map, seed, content, prog));
        var ev = o.ev; int group = ev == null ? 0 : RaidData9.GroupOf(ev, l, i, ver, prog);
        bool might = content == TeraRaidContentType.Might7;
        return (seed => { var r = ev == null ? null : RaidData9.Select(ev, seed, ver, prog, group, might); return r == null ? null : (r.Species, r.Form, r.Stars); },
                seed => RaidTools.PreviewEvent(sav, ev, group, seed, content, prog));
    }

    private static int Num(Entry e, int def, int min, int max) => int.TryParse(e.Text, out var v) ? Math.Clamp(v, min, max) : def;

    private async Task Run()
    {
        for (int k = 0; k < 6; k++) { c.IvMin[k] = Num(ivMin[k], 0, 0, 31); c.IvMax[k] = Num(ivMax[k], 31, 0, 31); }
        c.ScaleMin = Num(scaleMin, 0, 0, 255); c.ScaleMax = Num(scaleMax, 255, 0, 255); c.RewardMin = Num(rewardMin, 1, 1, 999);
        if (content is TeraRaidContentType.Distribution or TeraRaidContentType.Might7 && o.ev is not { Any: true }) { Note.Show("배포·최강 레이드는 이벤트 정보가 있어야 찾을 수 있어요"); return; }
        o.search?.Cancel(); var cts = o.search = new CancellationTokenSource(); var tok = cts.Token;
        go.IsEnabled = false; results.Children.Clear(); status.Text = "찾는 중…";
        var (q, make) = Funcs(o, rg, i, content);
        bool seedShiny = content is TeraRaidContentType.Base05 or TeraRaidContentType.Black6;
        using var bg = BgTask.Begin("레이드 시드 찾기", cts);
        var last = DateTime.MinValue;
        var res = await Task.Run(() => RaidTools.Search(q, make, c, 20, TimeSpan.FromSeconds(60), tok, (tried, found) =>
        {
            if ((DateTime.Now - last).TotalMilliseconds < 400) return; last = DateTime.Now;
            MainThread.BeginInvokeOnMainThread(() => status.Text = $"찾는 중… 시드 {tried:N0}개 확인 · 찾은 {found}");
            bg.Report($"시드 {tried:N0}개 확인 · 찾은 {found}", found, 20);
        }, seedShiny));
        go.IsEnabled = true;
        status.Text = res.Count == 0 ? "조건에 맞는 시드를 찾지 못했어요 (조건을 줄이거나 다시 찾아 보세요)" : $"찾은 시드 {res.Count}개 · 누르면 #{i + 1} 레이드에 넣어요";
        bg.Done(res.Count == 0 ? "레이드 시드를 찾지 못했어요" : $"레이드 시드 {res.Count}개를 찾았어요");
        var s = GameInfo.Strings;
        foreach (var (seed, t) in res.OrderByDescending(x => x.T.Shiny))
        {
            var p = t.Pk;
            var l1 = T.L($"{t.Stars}★ {AppState.SpeciesName(p)}{(t.Shiny ? " ✨" : "")} · 테라 {RaidSheet.TeraName(t.TeraType)} · {s.natures[(int)p.Nature]} · 시드 {seed:X8}", 13);
            var l2 = T.L($"개체값 {p.IV_HP}/{p.IV_ATK}/{p.IV_DEF}/{p.IV_SPA}/{p.IV_SPD}/{p.IV_SPE} · 크기 {p.Scale} · {s.abilitylist[p.Ability]}", 11, sub: true);
            var l3 = T.L("보상: " + RaidData9.Text(t.Rewards, 8), 10, sub: true); l3.LineBreakMode = LineBreakMode.WordWrap;
            var g = new Grid { ColumnDefinitions = { new(new GridLength(48)), new(GridLength.Star) }, ColumnSpacing = 8 };
            g.Add(new Image { WidthRequest = 44, HeightRequest = 38, Source = AppState.Sprite(p) }, 0);
            g.Add(new VerticalStackLayout { Spacing = 1, Children = { l1, l2, l3 } }, 1);
            var card = T.Card(g, 8);
            var tap = new TapGestureRecognizer();
            uint sd = seed; var ctt = content;
            tap.Tapped += (_, _) =>
            {
                if (detail != null) detail.Apply(sd, ctt);
                else { var r = o.lists9[rg].List.GetRaid(i); o.Remember(rg, i); r.Seed = sd; r.Content = ctt; r.IsEnabled = true; o.Changed("시드를 바꿨습니다"); }
                Close();
            };
            card.GestureRecognizers.Add(tap);
            results.Children.Add(card);
        }
    }
}

/// <summary>소드실드 굴 한 칸 (스크롤).</summary>
public class DenDetailSheet : Sheet
{
    private readonly RaidSheet o; private readonly int rg, i;
    private readonly VerticalStackLayout body = new() { Spacing = 10 };
    public DenDetailSheet(RaidSheet owner, int region, int index) : base($"#{index + 1} 굴 · {owner.lists8[region].Name}", 0.8)
    {
        o = owner; rg = region; i = index;
        Body.Add(new ScrollView { Content = body });
        Render();
    }
    private void Changed(string msg) { o.Changed(msg); Render(); }

    private void Render()
    {
        body.Children.Clear();
        var d = o.lists8[rg].List.GetRaid(i);
        var s = RaidTools.Shiny8(d.Seed);
        var info = T.L($"{(d.IsActive ? $"{d.Stars + 1}★ · {RaidTools.DenName(d.DenType)}" : "비활성")} · {(s == 2 ? "네모 이로치 시드" : s == 1 ? "별 이로치 시드" : "이로치 아님")}\n"
            + "보상은 굴 종류·별에 따라 게임 표로 정해져 세이브에 들어 있지 않아요 (이벤트 굴은 '이벤트 굴 가져오기'로 넣은 이벤트 표).", 13);
        info.LineBreakMode = LineBreakMode.WordWrap;
        body.Children.Add(T.Card(info));

        var act = new Switch { IsToggled = d.IsActive };
        act.Toggled += (_, e) => { o.Remember(rg, i); if (e.Value) d.Activate(d.Stars, d.RandRoll); else d.Deactivate(); Changed(null); };
        var watt = new Switch { IsToggled = d.WattsHarvested };
        watt.Toggled += (_, e) => { o.Remember(rg, i); d.WattsHarvested = e.Value; Changed(null); };
        var row = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, ColumnSpacing = 6 };
        row.Add(T.L("켜기", 13), 0); row.Add(act, 1); row.Add(T.L("와트 받음", 13), 3); row.Add(watt, 4);
        body.Children.Add(row);

        var seed = T.Input(placeholder: "시드 (16진수 16자리)"); seed.Text = d.Seed.ToString("X16");
        var roll = T.Input(Keyboard.Numeric, "0~99"); roll.Text = d.RandRoll.ToString();
        var (sv1, st) = T.Chooser(() => SheetHost.Show(new PickerSheet("별", Enumerable.Range(0, 5).Select(k => new ComboItem($"{k + 1}★", k)).ToList(), d.Stars,
            c => { o.Remember(rg, i); d.Stars = (byte)c.Value; Changed("별을 바꿨습니다"); })));
        st.Text = $"{d.Stars + 1}★";
        var (tv, tt) = T.Chooser(() => SheetHost.Show(new PickerSheet("굴 종류", Enum.GetValues<RaidType>().Select(k => new ComboItem(RaidTools.DenName(k), (int)k)).ToList(), (int)d.DenType,
            c => { o.Remember(rg, i); d.DenType = (RaidType)c.Value; Changed("굴 종류를 바꿨습니다"); })));
        tt.Text = RaidTools.DenName(d.DenType);
        var g = T.Cols(2); g.Add(T.Field("별", sv1), 0); g.Add(T.Field("종류", tv), 1);
        var g2 = T.Cols(2); g2.Add(T.Field("시드", seed), 0); g2.Add(T.Field("뽑기 값 (종 결정)", roll), 1);
        body.Children.Add(g); body.Children.Add(g2);

        var btns = new RaidSheet.BtnGrid(2);
        btns.Add("적용", () =>
        {
            if (!ulong.TryParse((seed.Text ?? "").Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase), System.Globalization.NumberStyles.HexNumber, null, out var v)) { Note.Show("16진수 시드를 넣으세요"); return; }
            if (!byte.TryParse(roll.Text, out var rr) || rr > 99) { Note.Show("뽑기 값은 0~99"); return; }
            o.Remember(rg, i); d.Seed = v; d.RandRoll = rr; Changed("굴을 바꿨습니다");
        }, true);
        btns.Add("무작위 시드", () => { o.Remember(rg, i); d.Seed = (ulong)Random.Shared.NextInt64() ^ ((ulong)Random.Shared.Next() << 63); Changed("시드를 바꿨습니다"); });
        btns.Add("별 이로치 시드", () => _ = Find8(false));
        btns.Add("네모 이로치 시드", () => _ = Find8(true));
        body.Children.Add(btns.View);
    }

    private async Task Find8(bool square)
    {
        o.search?.Cancel(); o.search = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var tok = o.search.Token;
        var f = await Task.Run(() => RaidTools.Find8(square, tok));
        if (f == null) { Note.Show("시드를 찾지 못했습니다"); return; }
        var d = o.lists8[rg].List.GetRaid(i); o.Remember(rg, i); d.Seed = f.Value;
        Changed(square ? "네모 이로치 시드로 바꿨습니다" : "이로치 시드로 바꿨습니다");
    }
}
