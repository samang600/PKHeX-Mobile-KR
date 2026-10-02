using HomeLive.Core;
using HomeLive.DeviceExecutor;
using PKHeX.Core;
using SysBot.Base;
namespace PKHeXKR;

/// <summary>HOME Live (Manu098vm/HOME-Live-Plugin 기반): 스위치의 포켓몬 HOME 박스를 읽어 게임 형식으로 변환. 읽기 전용.</summary>
public class HomeLiveSheet : Sheet
{
    private readonly Entry ip = T.Input(Keyboard.Url, "192.168.0.10"), port = T.Input(Keyboard.Numeric), single = T.Input(Keyboard.Numeric);
    private readonly Label status = T.L("", 13, sub: true);
    private readonly CollectionView grid = new() { SelectionMode = SelectionMode.None, HeightRequest = 330 };
    private readonly ActivityIndicator busy = new() { Color = T.Accent, IsVisible = false };
    private readonly Switch fix = new() { OnColor = T.Accent };
    private List<PKM> loaded = [];
    private int rangeIdx = -1;             // -1 = 한 박스
    private ConversionType conv = ConversionType.CompatibleData;
    private const int HomeBoxes = 200;

    public HomeLiveSheet() : base("HOME Live (HOME 박스 보기)")
    {
        ip.Text = Preferences.Get("home_ip", Preferences.Get("live_ip", "")); port.Text = Preferences.Get("home_port", "6000"); single.Text = "1";
        fix.IsToggled = Preferences.Get("home_fix", false);
        var read = T.Pill("읽기", primary: true); read.Clicked += async (_, _) => await Read();
        var fill = T.Pill("세이브 박스에 채우기"); fill.Clicked += async (_, _) => await Fill();
        grid.ItemsLayout = new GridItemsLayout(6, ItemsLayoutOrientation.Vertical) { VerticalItemSpacing = 4, HorizontalItemSpacing = 4 };
        grid.ItemTemplate = new DataTemplate(() =>
        {
            var img = new Image { WidthRequest = 52, HeightRequest = 44 }; img.SetBinding(Image.SourceProperty, "Sprite");
            var g = new Grid { HeightRequest = 54 }; g.Add(img);
            var tap = new TapGestureRecognizer(); tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, ".");
            tap.Tapped += (_, e) => { if (e.Parameter is Cell c && c.Pk != null) { Close(); AppState.Load(c.Pk); Note.Show("HOME 개체를 불러왔습니다"); } };
            g.GestureRecognizers.Add(tap); return g;
        });

        // 범위: 한 박스 / 1–32 / 33–64 …
        var ranges = new List<(string, int)> { ("한 박스", -1) };
        for (int i = 0; i * 32 < HomeBoxes; i++) ranges.Add(($"{i * 32 + 1}–{Math.Min((i + 1) * 32, HomeBoxes)}", i));
        var rangeFlex = Chips(ranges.Select(r => r.Item1).ToArray(), 0, k => { rangeIdx = ranges[k].Item2; single.IsVisible = rangeIdx < 0; });
        var convFlex = Chips(["해당 게임 데이터만", "호환 변환", "모두 강제 변환"], 1, k => conv = (ConversionType)(k + 1));
        var fixRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } }; fixRow.Add(T.L("변환 후 합법성 자동 보정", 14), 0); fixRow.Add(fix, 1);
        fix.Toggled += (_, e) => Preferences.Set("home_fix", e.Value);

        var hint = T.L("스위치에서 sys-botbase를 켜고 포켓몬 HOME을 실행한 뒤 연결합니다. 읽기 전용이며 HOME에 쓰지 않습니다. 변환은 비공식 시뮬레이션입니다.\n· 해당 게임 데이터만: 그 게임에 들어간 적이 있는 개체만\n· 호환 변환: 현재 게임으로 옮길 수 있는 개체는 변환\n· 모두 강제 변환: 가능한 한 모두 변환", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        var conn = new Grid { ColumnDefinitions = { new(new GridLength(2, GridUnitType.Star)), new(GridLength.Star) }, ColumnSpacing = 8 };
        conn.Add(T.Field("IP 주소", ip), 0); conn.Add(T.Field("포트", port), 1);
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, conn, T.Field("범위", rangeFlex), T.Field("HOME 박스 번호", single), T.Field("데이터 변환 방식", convFlex), fixRow,
            new HorizontalStackLayout { Spacing = 8, Children = { read, fill, busy } }, status, grid } } });
        status.Text = $"변환 대상: {AppState.GameName(AppState.Sav.Version)} 형식";
    }

    private static FlexLayout Chips(string[] names, int initial, Action<int> on)
    {
        var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        var btns = new List<Button>();
        void Style(int sel) { for (int j = 0; j < btns.Count; j++) { var b = btns[j]; if (j == sel) { b.BackgroundColor = T.Accent; b.TextColor = Colors.White; } else { b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); b.SetAppThemeColor(Button.TextColorProperty, T.Accent, Color.FromArgb("#A5B4FC")); } } }
        for (int i = 0; i < names.Length; i++) { int k = i; var b = T.Pill(names[i]); b.Margin = new Thickness(0, 0, 6, 6); b.Clicked += (_, _) => { Style(k); on(k); }; btns.Add(b); flex.Children.Add(b); }
        Style(initial);
        return flex;
    }

    public record Cell(PKM Pk, string Sprite);

    private async Task Read()
    {
        if (AppState.Sav is not (SAV9ZA or SAV9SV or SAV8LA or SAV8BS or SAV8SWSH or SAV7b))
        { status.Text = "HOME과 호환되는 게임(Z-A·SV·PLA·BDSP·소드실드·레츠고) 세이브에서만 쓸 수 있습니다. 하단 '게임 변경'으로 바꾸세요"; return; }
        int start, end;
        if (rangeIdx < 0) { if (!int.TryParse(single.Text, out var b) || b < 1) b = 1; start = end = Math.Min(b, HomeBoxes) - 1; }
        else { start = rangeIdx * 32; end = Math.Min(start + 31, HomeBoxes - 1); }
        Preferences.Set("home_ip", ip.Text ?? ""); Preferences.Set("home_port", port.Text ?? "6000");
        busy.IsVisible = busy.IsRunning = true; status.Text = "HOME에 연결 중…";
        DeviceExecutor<DeviceState> ex = null;
        var destType = AppState.Sav.PKMType; var cv = conv; bool doFix = fix.IsToggled;
        try
        {
            var cfg = new SwitchConnectionConfig { IP = ip.Text?.Trim() ?? "", Port = int.TryParse(port.Text, out var p) ? p : 6000, Protocol = SwitchProtocol.WiFi };
            ex = new DeviceExecutor<DeviceState>(new DeviceState { Connection = cfg, InitialRoutine = RoutineType.ReadWrite });
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await ex.RunAsync(cts.Token);
            await ex.Connect(cts.Token);
            var result = new List<PKM>();
            for (int bx = start; bx <= end; bx++)
            {
                status.Text = $"HOME {bx + 1}번 박스 읽는 중… ({bx - start + 1}/{end - start + 1})";
                var data = await ex.ReadBoxData(bx, cts.Token);
                var list = PokeHandler.GenerateEntitiesFromBoxBin(data);
                await Task.Run(() =>
                {
                    foreach (var w in list)
                    {
                        PKM pk = null;
                        try { pk = w?.ConvertToType(destType, cv); if (pk != null && doFix) pk = pk.FixLegality(); } catch { }
                        result.Add(pk);
                    }
                });
            }
            loaded = result;
            grid.ItemsSource = loaded.Select(x => new Cell(x, x == null || x.Species == 0 ? null : AppState.Sprite(x))).ToList();
            int n = loaded.Count(x => x != null && x.Species != 0);
            status.Text = $"HOME {start + 1}–{end + 1}번 박스: 변환된 포켓몬 {n}마리 · 누르면 편집기로 불러옵니다";
        }
        catch (Exception e)
        {
            var inner = e; while (inner.InnerException != null) inner = inner.InnerException;
            status.Text = $"실패: {e.GetType().Name} — {inner.Message}";
        }
        finally { try { ex?.Disconnect(); } catch { } busy.IsVisible = busy.IsRunning = false; }
    }

    private async Task Fill()
    {
        if (loaded.Count == 0) { status.Text = "먼저 읽어 오세요"; return; }
        var sav = AppState.Sav;
        var page = Application.Current.Windows[0].Page;
        if (!await page.DisplayAlertAsync("세이브 박스에 채우기", $"{AppState.BoxName(AppState.Box)}부터 HOME 순서 그대로 채웁니다. 해당 칸의 기존 포켓몬은 덮어씁니다.", "채우기", "취소")) return;
        int n = 0;
        for (int i = 0; i < loaded.Count; i++)
        {
            int b = AppState.Box + i / sav.BoxSlotCount; if (b >= sav.BoxCount) break;
            var p = loaded[i];
            sav.SetBoxSlotAtIndex(p ?? sav.BlankPKM, b, i % sav.BoxSlotCount);
            if (p != null && p.Species != 0) n++;
        }
        AppState.NotifyBox(); Note.Show($"{n}마리를 넣었습니다");
    }
}
