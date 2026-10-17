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
    private readonly Switch anyVer = new() { OnColor = T.Accent };
    private readonly Entry offsetE = T.Input(Keyboard.Text, "비우면 버전별 기본값 (4.1.0: 12F80 · 4.0.0: 4C0130)");
    private List<PKM> loaded = [];
    private int rangeIdx = -1;             // -1 = 한 박스
    private ConversionType conv = ConversionType.CompatibleData;
    private const int HomeBoxes = 300;

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
            tap.Tapped += (_, e) => { if (e.Parameter is Cell c && c.Pk != null) { Close(); if (AppState.Load(c.Pk)) Note.Show("HOME 개체를 불러왔습니다"); } };
            g.GestureRecognizers.Add(tap); return g;
        });

        // 범위: 한 박스 / 1–32 / 33–64 …
        var ranges = new List<(string, int)> { ("한 박스", -1) };
        for (int i = 0; i * 32 < HomeBoxes; i++) ranges.Add(($"{i * 32 + 1}–{Math.Min((i + 1) * 32, HomeBoxes)}", i));
        var rangeFlex = Chips(ranges.Select(r => r.Item1).ToArray(), 0, k => { rangeIdx = ranges[k].Item2; single.IsVisible = rangeIdx < 0; });
        var convFlex = Chips(["해당 게임만", "호환 변환", "모두 강제"], 1, k => conv = (ConversionType)(k + 1), cols: 3);
        var fixRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } }; fixRow.Add(T.L("변환 후 합법성 자동 보정", 14), 0); fixRow.Add(fix, 1);
        fix.Toggled += (_, e) => Preferences.Set("home_fix", e.Value);

        var hint = T.L("스위치에서 sys-botbase를 켜고 포켓몬 HOME을 실행한 뒤 연결합니다. 읽기 전용이며 HOME에 쓰지 않습니다. 변환은 비공식 시뮬레이션입니다.\n· 해당 게임만: 그 게임에 들어간 적이 있는 개체만\n· 호환 변환: 현재 게임으로 옮길 수 있는 개체는 변환\n· 모두 강제: 가능한 한 모두 변환", 12, sub: true);
        hint.LineBreakMode = LineBreakMode.WordWrap;
        // HOME 새 버전 대응 (테스트): 버전 확인 건너뛰기 + 데이터 위치 직접 입력·자동 찾기
        anyVer.IsToggled = Preferences.Get("home_anyver", false); anyVer.Toggled += (_, e) => Preferences.Set("home_anyver", e.Value);
        offsetE.Text = Preferences.Get("home_offset", "");
        var anyRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        var anyLab = T.L("지원하지 않는 HOME 버전도 연결", 14); anyLab.LineBreakMode = LineBreakMode.WordWrap; anyRow.Add(anyLab, 0); anyRow.Add(anyVer, 1);
        var scan = T.Pill("데이터 위치 자동 찾기"); scan.Clicked += async (_, _) => await Scan();
        var newHint = T.L("HOME이 업데이트되어 읽기가 안 될 때: 'HOME 1번 박스 1번 칸'에 포켓몬을 넣어 두고 '자동 찾기'를 누르세요. 기존 위치 앞뒤를 읽어 박스 데이터를 찾고, 찾은 위치로 1번 박스를 보여 줍니다(와이파이로 1~3분). 읽기만 하므로 HOME 데이터는 바뀌지 않습니다.", 12, sub: true);
        newHint.LineBreakMode = LineBreakMode.WordWrap;
        var newCard = T.Card(new VerticalStackLayout { Spacing = 8, Children = { T.L("HOME 새 버전 대응 (4.0.0·4.1.0 기본 지원)", 13, sub: true), anyRow, T.Field("박스 데이터 위치 (heap 기준 16진수)", offsetE), scan, newHint } });
        var conn = new Grid { ColumnDefinitions = { new(new GridLength(2, GridUnitType.Star)), new(GridLength.Star) }, ColumnSpacing = 8 };
        conn.Add(T.Field("IP 주소", ip), 0); conn.Add(T.Field("포트", port), 1);
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { hint, conn, T.Field("범위", rangeFlex), T.Field("HOME 박스 번호", single), T.Field("데이터 변환 방식", convFlex), fixRow,
            new HorizontalStackLayout { Spacing = 8, Children = { read, fill, busy } }, status, grid, newCard } } });
        status.Text = $"변환 대상: {AppState.GameName(AppState.Sav.Version)} 형식";
    }

    /// <summary>같은 너비 버튼을 칸에 맞춰 가지런히 (cols열)</summary>
    private static Grid Chips(string[] names, int initial, Action<int> on, int cols = 4)
    {
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int r = 0; r < (names.Length + cols - 1) / cols; r++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var btns = new List<Button>();
        void Style(int sel) { for (int j = 0; j < btns.Count; j++) { var b = btns[j]; if (j == sel) { b.BackgroundColor = T.Accent; b.TextColor = Colors.White; } else { b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); b.SetAppThemeColor(Button.TextColorProperty, T.Accent, Color.FromArgb("#A5B4FC")); } } }
        for (int i = 0; i < names.Length; i++)
        {
            int k = i; var b = T.Pill(names[i], size: 13); b.Padding = new Thickness(2, 0); b.HorizontalOptions = LayoutOptions.Fill;
            b.Clicked += (_, _) => { Style(k); on(k); }; btns.Add(b); grid.Add(b, i % cols, i / cols);
        }
        Style(initial);
        return grid;
    }

    public record Cell(PKM Pk, string Sprite);

    private async Task Read()
    {
        if (AppState.Sav is not (SAV9ZA or SAV9SV or SAV8LA or SAV8BS or SAV8SWSH or SAV7b))
        { status.Text = "HOME과 호환되는 게임(Z-A·SV·PLA·BDSP·소드실드·레츠고) 세이브에서만 쓸 수 있습니다. 하단 '게임 변경'으로 바꾸세요"; return; }
        int start, end;
        if (rangeIdx < 0) { if (!int.TryParse(single.Text, out var b) || b < 1) b = 1; start = end = Math.Min(b, HomeBoxes) - 1; }
        else { start = rangeIdx * 32; end = Math.Min(start + 31, HomeBoxes - 1); }
        if (AppState.Sav is not (SAV9ZA or SAV9SV or SAV8LA or SAV8BS or SAV8SWSH or SAV7b))
        { status.Text = "HOME과 호환되는 게임(Z-A·SV·PLA·BDSP·소드실드·레츠고) 세이브에서 찾으세요. 하단 '게임 변경'으로 바꾸세요"; return; }
        Preferences.Set("home_ip", ip.Text ?? ""); Preferences.Set("home_port", port.Text ?? "6000");
        busy.IsVisible = busy.IsRunning = true; status.Text = "HOME에 연결 중…";
        DeviceExecutor<DeviceState> ex = null;
        var destType = AppState.Sav.PKMType; var cv = conv; bool doFix = fix.IsToggled;
        try
        {
            ApplyNewVersionSettings();
            ex = NewExecutor();
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

    static HomeLiveSheet()
    {
        // HOME 칸 확인: 머리의 체크섬이 암호화 데이터와 맞는지 (메모리에 풀린 채 있으면 다시 암호화해서 비교)
        DeviceExecutor<DeviceState>.SlotValidator = b =>
        {
            try
            {
                var stored = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(0xA));
                if (HomeCrypto.GetChecksum1(b) == stored) return true;
                return HomeCrypto.GetChecksum1(HomeCrypto.Crypt(b, false)) == stored;
            }
            catch { return false; }
        };
    }

    private DeviceExecutor<DeviceState> NewExecutor()
    {
        var cfg = new SwitchConnectionConfig { IP = ip.Text?.Trim() ?? "", Port = int.TryParse(port.Text, out var p) ? p : 6000, Protocol = SwitchProtocol.WiFi };
        return new DeviceExecutor<DeviceState>(new DeviceState { Connection = cfg, InitialRoutine = RoutineType.ReadWrite });
    }

    private void ApplyNewVersionSettings()
    {
        var t = (offsetE.Text ?? "").Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        Preferences.Set("home_offset", t);
        DeviceExecutor<DeviceState>.AllowAnyVersion = anyVer.IsToggled;
        DeviceExecutor<DeviceState>.UseOffset(ulong.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0);
    }

    /// <summary>HOME 박스 데이터 위치 자동 찾기 → 찾은 위치로 1번 박스를 읽어 보여 주고 저장할지 확인.</summary>
    private async Task Scan()
    {
        if (AppState.Sav is not (SAV9ZA or SAV9SV or SAV8LA or SAV8BS or SAV8SWSH or SAV7b))
        { status.Text = "HOME과 호환되는 게임(Z-A·SV·PLA·BDSP·소드실드·레츠고) 세이브에서 찾으세요. 하단 '게임 변경'으로 바꾸세요"; return; }
        Preferences.Set("home_ip", ip.Text ?? ""); Preferences.Set("home_port", port.Text ?? "6000");
        busy.IsVisible = busy.IsRunning = true; status.Text = "HOME에 연결 중…";
        DeviceExecutor<DeviceState> ex = null;
        var destType = AppState.Sav.PKMType;
        try
        {
            DeviceExecutor<DeviceState>.AllowAnyVersion = true; DeviceExecutor<DeviceState>.UseOffset(0);
            ex = NewExecutor();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            await ex.RunAsync(cts.Token);
            await ex.Connect(cts.Token);
            var progress = new Progress<string>(m => status.Text = m);
            var rel = await ex.ScanBoxStart(64UL << 20, progress, cts.Token);
            if (rel == 0) { status.Text = $"HOME {ex.HomeVersion}: 기존 위치 앞뒤 64MB에서 박스 데이터를 찾지 못했습니다 (1번 박스 1번 칸에 포켓몬이 있는지 확인)"; return; }
            DeviceExecutor<DeviceState>.UseOffset(rel);
            status.Text = $"찾은 위치 heap+0x{rel:X} · 1번 박스 읽는 중…";
            var data = await ex.ReadBoxData(0, cts.Token);
            var list = PokeHandler.GenerateEntitiesFromBoxBin(data);
            var result = new List<PKM>();
            await Task.Run(() => { foreach (var w in list) { PKM pk = null; try { pk = w?.ConvertToType(destType, ConversionType.AnyData); } catch { } result.Add(pk); } });
            loaded = result;
            grid.ItemsSource = loaded.Select(x => new Cell(x, x == null || x.Species == 0 ? null : AppState.Sprite(x))).ToList();
            int n = loaded.Count(x => x != null && x.Species != 0);
            status.Text = $"HOME {ex.HomeVersion} · 찾은 위치 heap+0x{rel:X} · 1번 박스 {n}마리";
            var page = Application.Current.Windows[0].Page;
            if (await page.DisplayAlertAsync("데이터 위치", $"HOME {ex.HomeVersion}에서 찾은 위치: heap+0x{rel:X}\n\n아래에 보이는 포켓몬({n}마리)이 HOME 1번 박스와 같으면 이 위치를 저장하세요.\n(다르면 다른 칸에 포켓몬을 옮기고 다시 찾기)", "이 위치 저장", "취소"))
            {
                offsetE.Text = rel.ToString("X"); anyVer.IsToggled = true;
                Preferences.Set("home_offset", offsetE.Text); Preferences.Set("home_anyver", true);
                Note.Show($"저장했습니다 · 이제 '읽기'로 다른 박스도 읽을 수 있습니다 (위치 {offsetE.Text} 를 알려 주시면 앱 기본값에 넣겠습니다)");
            }
        }
        catch (Exception e)
        {
            var inner = e; while (inner.InnerException != null) inner = inner.InnerException;
            status.Text = $"실패: {e.GetType().Name} — {inner.Message}";
        }
        finally { try { ex?.Disconnect(); } catch { } busy.IsVisible = busy.IsRunning = false; ApplyNewVersionSettings(); }
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
