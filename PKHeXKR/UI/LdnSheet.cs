using System.Text.Json;
using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// PokeLDN 교환 화면 (LDN판): ESP32 보드로 스위치 게임과 로컬 통신. 교환·교환 진화 도우미·이상한 소포·FRLG 세이브 백업/복원.
/// 보낼 포켓몬은 박스·편집기에서 바로, 받은 포켓몬은 끝나면 박스로. 통신은 LdnTrade(파이썬 PokeLDN)가 하고 이 시트는 화면만.
/// </summary>
public class LdnSheet : Sheet
{
    private readonly Label board = T.L("", 13), keys = T.L("", 13), guide = T.L("", 12, sub: true), summary = T.L("", 13), log = T.L("", 10, sub: true);
    private readonly VerticalStackLayout offersBox = new() { Spacing = 4 }, tradeCard = new() { Spacing = 8 }, giftCard = new() { Spacing = 8 }, restoreCard = new() { Spacing = 8 }, roleCard = new() { Spacing = 6 };
    private readonly Label gameL, modeL, giftGroupL, giftItemL, restoreL;
    private readonly Label giftNote = T.L("", 12, sub: true);
    private readonly Button usbB = T.Pill("USB 케이블", size: 12), bleB = T.Pill("블루투스", size: 12), hostB = T.Pill("기다리기 (호스트)", size: 12), joinB = T.Pill("찾아 들어가기 (참가)", size: 12),
        frB = T.Pill("파이어레드", size: 12), lgB = T.Pill("리프그린", size: 12), start = T.Pill("교환 시작", primary: true), stop = T.Pill("중지"), logB = T.Pill("전체 로그 보기", size: 12);
    private readonly Switch manual = new() { OnColor = T.Accent }, lowPower = new() { OnColor = T.Accent }, autoOt = new() { OnColor = T.Accent };
    private View autoOtRow;
    private readonly Label dexStatus = T.L("", 12, sub: true);
    private readonly Entry code = T.Input(Keyboard.Numeric);
    private readonly View versionRow, codeRow;
    private string game, mode;
    private string giftKey = "", giftFile, restoreFile;
    private bool restoreCurrent;
    private readonly IDispatcherTimer boardTimer;

    private static bool Ble
    {
        get => OperatingSystem.IsIOS() || Preferences.Get("ldn_ble", false);
        set { if (!OperatingSystem.IsIOS()) Preferences.Set("ldn_ble", value); }
    }
    private bool IsHost => manual.IsToggled ? Preferences.Get("ldn_host", false) : game == "frlg";

    public LdnSheet() : base("PokeLDN 교환 (ESP32 보드)", 0.9)
    {
        game = LdnTrade.KeyOf(AppState.Sav) ?? Preferences.Get("ldn_game", "swsh");
        if (LdnTrade.Offers.Count > 0 && LdnExport.GameOf(LdnTrade.Offers[0]) is { } og) game = og.Game;
        mode = Preferences.Get("ldn_mode_" + game, "trade");
        foreach (var l in new[] { board, keys, guide, summary, log, giftNote }) l.LineBreakMode = LineBreakMode.WordWrap;
        log.FontFamily = "monospace"; log.IsVisible = false;

        // 준비: 보드 연결 방식·prod.keys·펌웨어
        usbB.Clicked += (_, _) => { Ble = false; Styles(); UpdateBoard(); };
        bleB.Clicked += (_, _) => { Ble = true; Styles(); UpdateBoard(); };
        usbB.IsVisible = !OperatingSystem.IsIOS();
        var linkRow = T.Cols(2, 6); linkRow.Add(usbB, 0); linkRow.Add(bleB, 1);
        var check = T.Pill("보드 확인 · USB 허용", size: 12); check.Clicked += (_, _) => { LdnTrade.Bridge.RequestUsb(); UpdateBoard(); };
        check.IsVisible = !OperatingSystem.IsIOS();
        var keysB = T.Pill("prod.keys 가져오기", size: 12); keysB.Clicked += async (_, _) => await PickKeys();
        var fwB = T.Pill($"보드 펌웨어 설치 (v{LdnTrade.FwVersion})", size: 12); fwB.Clicked += async (_, _) => await Flash();
        fwB.IsVisible = !OperatingSystem.IsIOS();
        var prepRow = T.Cols(2, 6); prepRow.Add(check, 0); prepRow.Add(keysB, 1);
        lowPower.IsToggled = Preferences.Get("ldn_low_power", false); lowPower.Toggled += (_, e) => Preferences.Set("ldn_low_power", e.Value);
        var prep = new VerticalStackLayout { Spacing = 8, Children = { linkRow, board, keys, prepRow, fwB, Row("보드 송신 출력 낮추기 (8.5 dBm) — C3 Super Mini 등에서 호스트를 게임이 못 찾을 때", lowPower) } };

        // 게임·할 일
        (var gv, gameL) = T.Chooser(async () => await PickGame());
        (var mv, modeL) = T.Chooser(async () => await PickMode());
        var gm = T.Cols(2, 6); gm.Add(T.Field("게임", gv), 0); gm.Add(T.Field("할 일", mv), 1);

        // 방식(호스트·참가)·교환 코드
        manual.IsToggled = Preferences.Get("ldn_manual_role", false);
        manual.Toggled += (_, e) => { Preferences.Set("ldn_manual_role", e.Value); Refresh(); };
        hostB.Clicked += (_, _) => { Preferences.Set("ldn_host", true); Refresh(); };
        joinB.Clicked += (_, _) => { Preferences.Set("ldn_host", false); Refresh(); };
        var roleRow = T.Cols(2, 6); roleRow.Add(hostB, 0); roleRow.Add(joinB, 1);
        code.Text = Preferences.Get("ldn_code", ""); code.TextChanged += (_, e) => Preferences.Set("ldn_code", new string((e.NewTextValue ?? "").Where(char.IsDigit).ToArray()));
        codeRow = T.Field("교환 코드 (선택)", code);
        roleCard.Children.Add(Row("방식 직접 고르기 (고급)", manual)); roleCard.Children.Add(roleRow); roleCard.Children.Add(codeRow);

        // 교환: 보낼 포켓몬
        var addCur = T.Pill("편집 중인 포켓몬 추가", size: 12); addCur.Clicked += (_, _) => AddCurrent();
        var clear = T.Pill("비우기", size: 12); clear.Clicked += (_, _) => { LdnTrade.Offers.Clear(); if (LdnTrade.QueueGame is { } qg) LdnTrade.DropQueue(qg); Refresh(); };
        var all = T.Pill("전체 보기", size: 12); all.Clicked += (_, _) => SheetHost.Show(new LdnOffersSheet(true));
        var offerBtns = T.Cols(3, 6); offerBtns.Add(addCur, 0); offerBtns.Add(all, 1); offerBtns.Add(clear, 2);
        tradeCard.Children.Add(T.L("보낼 포켓몬 (제한 없음, 순서대로 교환) · 박스에서 여러 칸 선택 → 'PokeLDN 교환으로 보내기'", 12, sub: true));
        tradeCard.Children.Add(offersBox); tradeCard.Children.Add(offerBtns);
        autoOt.IsToggled = Preferences.Get("ldn_auto_ot", false); autoOt.Toggled += (_, e) => Preferences.Set("ldn_auto_ot", e.Value);
        autoOtRow = new VerticalStackLayout { Spacing = 2, Children = { Row("자동 어버이작 (SV): 콘솔 트레이너의 이름·ID로 바꿔 보내기", autoOt),
            T.L("호스트일 때만 · 콘솔 트레이너로 어버이·ID·성별·언어를 바꿔 보냄 (이로치 모양 유지, 바꾸면 불법인 개체는 그대로)", 11, sub: true).Also(x => x.LineBreakMode = LineBreakMode.WordWrap) } };
        tradeCard.Children.Add(autoOtRow);
        var dexB = T.Pill("📚 리빙덱스로 채우기", size: 12); dexB.Clicked += async (_, _) => await LivingDex();
        dexStatus.LineBreakMode = LineBreakMode.WordWrap;
        tradeCard.Children.Add(dexB); tradeCard.Children.Add(dexStatus);

        // 이상한 소포
        (var ggv, giftGroupL) = T.Chooser(async () => await PickGiftGroup());
        (var giv, giftItemL) = T.Chooser(() => PickGiftItem());
        var giftFileB = T.Pill("파일에서 고르기 (.wc8 · .wc3 · .pokegift)", size: 12); giftFileB.Clicked += async (_, _) => await PickGiftFile();
        giftCard.Children.Add(T.Field("분류", ggv)); giftCard.Children.Add(T.Field("보낼 소포", giv)); giftCard.Children.Add(giftNote); giftCard.Children.Add(giftFileB);

        // FRLG 카트리지 · 세이브 복원
        frB.Clicked += (_, _) => { Preferences.Set("ldn_leafgreen", false); Styles(); };
        lgB.Clicked += (_, _) => { Preferences.Set("ldn_leafgreen", true); Styles(); };
        var vr = T.Cols(2, 6); vr.Add(frB, 0); vr.Add(lgB, 1); versionRow = T.Field("카트리지", vr);
        (var rv, restoreL) = T.Chooser(async () => await PickRestore());
        restoreCard.Children.Add(T.Field("콘솔에 쓸 세이브", rv));
        restoreCard.Children.Add(T.L("모두 쓰고 다시 읽어 확인한 뒤에만 게임이 불러와 저장해요. 먼저 지금 콘솔 세이브를 백업해 두세요.", 12, sub: true));

        start.Clicked += async (_, _) => await Start();
        stop.Clicked += async (_, _) => { stop.IsEnabled = false; await LdnTrade.StopAsync(); };
        logB.Clicked += (_, _) => { log.IsVisible = !log.IsVisible; logB.Text = log.IsVisible ? "전체 로그 접기" : "전체 로그 보기"; Render(); };
        var runRow = T.Cols(2, 6); runRow.Add(start, 0); runRow.Add(stop, 1);

        Body.Add(new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    T.L("준비", 13, sub: true), T.Card(prep), gm, versionRow, roleCard, tradeCard, giftCard, restoreCard, guide, runRow,
                    T.L("진행 상황", 13, sub: true), T.Card(new VerticalStackLayout { Spacing = 6, Children = { summary, logB, log } }),
                    T.L(OperatingSystem.IsIOS() ? "PokeLDN(AGPLv3)으로 통신 · BLE 펌웨어 보드에 전원만 · 보드 연결은 화면을 켠 채로, 연결된 뒤에는 화면을 꺼도 교환이 이어짐" : "PokeLDN(AGPLv3)으로 통신 · USB: ESP32-C3·C6·S3, 클래식 ESP32(CP2102·CH340·CH9102) · 블루투스: BLE 펌웨어 보드에 전원만", 11, sub: true),
                }
            }
        });

        LdnTrade.Changed += OnChanged;
        Closed += () => { LdnTrade.Changed -= OnChanged; boardTimer?.Stop(); };
        boardTimer = Dispatcher.CreateTimer(); boardTimer.Interval = TimeSpan.FromSeconds(1.5); boardTimer.Tick += (_, _) => { if (!LdnTrade.Running) UpdateBoard(); }; boardTimer.Start();
        if (LdnTrade.Gifts(game).Count > 0) giftKey = Preferences.Get("ldn_gift_" + game, "");
        Styles(); UpdateBoard(); UpdateKeys(); Refresh(); Render();
    }

    private static Grid Row(string label, View right)
    {
        var g = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) }, MinimumHeightRequest = 40 };
        var l = T.L(label, 13); l.LineBreakMode = LineBreakMode.WordWrap; l.VerticalOptions = LayoutOptions.Center;
        g.Add(l, 0); g.Add(right, 1); return g;
    }

    private static void On(Button b, bool on)
    {
        if (on) { b.BackgroundColor = T.Accent; b.TextColor = Colors.White; }
        else { b.SetAppThemeColor(Button.BackgroundColorProperty, Colors.White, Color.FromArgb("#1A1D24")); b.SetAppThemeColor(Button.TextColorProperty, T.Accent, Color.FromArgb("#A5B4FC")); }
    }

    private void Styles()
    {
        On(usbB, !Ble); On(bleB, Ble);
        bool host = Preferences.Get("ldn_host", false); On(hostB, host); On(joinB, !host);
        bool lg = Preferences.Get("ldn_leafgreen", AppState.Sav?.Version == GameVersion.LG); On(frB, !lg); On(lgB, lg);
    }

    private void UpdateBoard()
    {
        if (Ble)
        {
            if (OperatingSystem.IsIOS())
            {
                (board.Text, board.TextColor) = LdnTrade.Bridge.RadioState() switch
                {
                    "ready" => ("● Bluetooth 연결됨 (POKELDN-RADIO)", T.Good),
                    "connecting" => ("● ESP32 보드에 연결 중…", T.Warn),
                    "scanning" => ("● POKELDN-RADIO 검색 중… BLE 펌웨어가 설치된 ESP32 보드에 전원을 연결하세요.", T.Warn),
                    "off" => ("● 교환 시작을 누르면 Bluetooth 보드를 검색합니다.", T.Warn),
                    "permission" => ("● Bluetooth 접근이 차단됐어요. 설정 → PKHeX 모바일에서 Bluetooth 권한을 허용하세요.", T.Bad),
                    "bluetooth_off" => ("● iPhone Bluetooth가 꺼져 있어요. 설정에서 Bluetooth를 켜세요.", T.Bad),
                    "unsupported" => ("● 이 기기에서 Bluetooth LE를 사용할 수 없어요.", T.Bad),
                    _ => ("● Bluetooth를 사용할 수 없어요. iPhone 설정에서 Bluetooth를 켜고 권한을 확인하세요.", T.Bad),
                };
                T.Text(board); return;
            }
            board.Text = "● 블루투스: 보드에 '블루투스 무선' 펌웨어가 있어야 하고, 보드는 USB로 전원만 받으면 돼요 (보조배터리·충전기). 시작하면 근처의 POKELDN-RADIO를 찾아요.";
            board.TextColor = null; T.Text(board); return;
        }
        if (OperatingSystem.IsIOS())
        {
            board.Text = "● 아이폰은 일반 ESP32 USB-시리얼 데이터 연결을 지원하지 않아요. PokeLDN BLE 펌웨어를 설치하고 블루투스 방식을 선택하세요. USB는 보드 전원 공급에만 사용할 수 있어요.";
            board.TextColor = T.Warn; T.Text(board); return;
        }
        string st; try { st = LdnTrade.Bridge.UsbState(); } catch { st = "none"; }
        (board.Text, board.TextColor) = st switch
        {
            "ready" => (LdnTrade.Bridge.UsbKind() is { Length: > 0 } k && k != "native" ? $"● 보드 연결됨 (클래식 ESP32 · {k})" : "● 보드 연결됨 (ESP32 내장 USB)", T.Good),
            "permission" => ("● 보드 발견 — USB 사용 허용이 필요해요 ('보드 확인 · USB 허용')", T.Warn),
            _ => ("● 보드 없음 — 데이터용 USB-C 케이블로 꽂고, 휴대폰 설정에 OTG 항목이 있으면 켜세요.", T.Bad),
        };
    }

    private void UpdateKeys()
    {
        bool ok = LdnTrade.HasKeys;
        keys.Text = ok ? "● prod.keys 준비됨" : "● prod.keys 필요 (커펌 스위치에서 Lockpick_RCM으로 뽑은 파일, 이 앱 안에만 보관)";
        keys.TextColor = ok ? T.Good : T.Bad;
    }

    private LdnTrade.ModeInfo ModeOf(string key) => LdnTrade.Modes.FirstOrDefault(m => m.Key == key) ?? LdnTrade.Modes[0];
    private LdnTrade.LdnGame GameOf(string key) => LdnTrade.Games.FirstOrDefault(g => g.Key == key) ?? LdnTrade.Games[2];
    private bool ModeOk(string m) => (" " + ModeOf(m).Games + " ").Contains(" " + game + " ");

    private void Refresh()
    {
        if (!ModeOk(mode)) mode = "trade";
        gameL.Text = GameOf(game).Name + (LdnTrade.KeyOf(AppState.Sav) == game ? " (지금 세이브)" : "");
        modeL.Text = ModeOf(mode).Name;
        bool trade = mode == "trade", frlg = game == "frlg";
        bool codeGame = game is not ("frlg" or "lgpe");
        roleCard.IsVisible = trade;
        ((View)roleCard.Children[1]).IsVisible = trade && manual.IsToggled;
        codeRow.IsVisible = trade && codeGame;
        code.Placeholder = IsHost ? (game is "za" or "pla" ? "비우면 00000000" : "비우면 코드 없이") : "비우면 아무 코드나 (8자리)";
        tradeCard.IsVisible = trade; giftCard.IsVisible = mode == "gift"; restoreCard.IsVisible = mode == "save_restore";
        if (autoOtRow != null) autoOtRow.IsVisible = game == "sv";
        dexStatus.Text = LdnTrade.QueueGame == game && LdnTrade.Offers.Count > 0 ? $"리빙덱스 대기열: {LdnTrade.Offers.Count}마리 남음 · 한 번 연결로 계속 교환하고, 끊기면 다시 시작하면 이어서 보내요 (콘솔에서 내줄 포켓몬이 같은 수만큼 필요)" : "";
        versionRow.IsVisible = frlg && !trade;
        Styles(); RefreshOffers(); RefreshGift(); RefreshRestore();
        string s = mode switch
        {
            "evolve" => (game == "lgpe" ? "게임에서 통신 → 근처의 사람과 교환 → 링크 코드 피카츄×3으로 검색을 연 뒤 시작을 누르세요." : "게임에서 Y통신 → 링크 교환 → 로컬 통신(코드 없이)으로 검색을 연 뒤 시작을 누르세요.")
                        + " 진화시킬 포켓몬(윤겔라·근육몬·데구리·고우스트 등)을 고르고 교환하면, 같은 포켓몬을 그대로 돌려보내서 게임 쪽에서 진화해요." + (game == "lgpe" ? " 한 번 연결로 최대 6번 이어서 할 수 있어요." : "")
                        + "\n· 돌려받는 건 내 포켓몬 그대로예요. 변함없는돌을 지니고 있으면 진화하지 않아요.",
            "gift" => game == "swsh" ? "시작을 누른 뒤 게임에서 이상한 소포 → 이상한 소포 받기 → 로컬 통신으로 받기. 몇 초 안에 카드가 보이면 받고, 다 받으면 중지를 누르세요 (5분 동안 보내요)."
                        : LdnTrade.FrlgMenu + " 시작을 누른 뒤 POKELDN을 고르세요. 카드를 바꿀지 물으면 '예'. 이상한 뉴스는 두 번째 메뉴(Wonder News)로 받아요. 위에서 카트리지를 맞게 고르세요.",
            "save_backup" => LdnTrade.FrlgMenu + " 시작을 누른 뒤 POKELDN을 고르세요. 128KB 세이브를 받는 데 몇 분 걸리고, 콘솔의 세이브는 바뀌지 않아요. 끊기면 다시 시작하면 이어서 받아요. 받은 세이브는 바로 열 수 있어요.",
            "save_restore" => LdnTrade.FrlgMenu + " 시작을 누른 뒤 POKELDN을 고르세요. 고른 세이브를 콘솔에 쓰고, 다 쓰고 확인한 뒤에만 게임이 불러와 저장해요. 도중에 실패하면 원래 세이브가 그대로 남아요.",
            _ => (IsHost ? GameOf(game).HostGuide : GameOf(game).JoinGuide)
                 + (manual.IsToggled ? "" : "\n· 이 게임은 자동으로 " + (IsHost ? "방을 열어요(호스트)." : "게임이 연 방을 찾아 들어가요(참가)."))
                 + (codeGame && !IsHost ? "\n· 교환 코드를 적으면 그 코드로 검색 중인 게임만 찾아요." : ""),
        };
        var sav = AppState.Sav;
        if (sav != null && LdnTrade.KeyOf(sav) == game) s += $"\n· 트레이너: {sav.OT} (지금 세이브의 이름·ID로 교환)";
        guide.Text = s;
        start.Text = LdnTrade.Running ? "진행 중…" : ModeOf(mode).Start;
        start.IsEnabled = !LdnTrade.Running; stop.IsEnabled = LdnTrade.Running;
    }

    private void RefreshOffers()
    {
        offersBox.Children.Clear();
        if (LdnTrade.Offers.Count == 0) { offersBox.Children.Add(T.L("없음", 13, sub: true)); return; }
        for (int i = 0; i < LdnTrade.Offers.Count; i++)
        {
            if (i >= 8) { offersBox.Children.Add(T.L($"… 외 {LdnTrade.Offers.Count - 8}마리", 12, sub: true)); break; }
            var p = LdnTrade.Offers[i]; int k = i;
            var g = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) }, ColumnSpacing = 8 };
            g.Add(new Image { Source = AppState.Sprite(p), WidthRequest = 40, HeightRequest = 34 }, 0);
            var ok = LdnExport.GameOf(p)?.Game == game;
            var l = T.L($"{i + 1}. {PKHeX.Core.GameInfo.Strings.Species[p.Species]} Lv.{p.CurrentLevel}{(p.IsShiny ? " ★" : "")}{(ok ? "" : $"  ⚠ {GameOf(game).Name} 포켓몬이 아님")}{(AppState.IsLegal(p) ? "" : "  · 불법")}", 13);
            l.VerticalOptions = LayoutOptions.Center; g.Add(l, 1);
            var x = T.Pill("✕", size: 12); x.WidthRequest = 40; x.Clicked += (_, _) => { LdnTrade.Offers.RemoveAt(k); if (LdnTrade.QueueGame != null) LdnTrade.SaveQueue(); Refresh(); };
            g.Add(x, 2);
            var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => await LdnTrade.OfferActions(k, Close); l.GestureRecognizers.Add(tap);
            offersBox.Children.Add(g);
        }
    }

    private void AddCurrent()
    {
        var pk = AppState.Pk;
        if (pk == null || pk.Species == 0) { Note.Show("편집 중인 포켓몬이 없습니다"); return; }
        if (LdnExport.GameOf(pk) == null) { Note.Show("이 게임은 PokeLDN 교환을 지원하지 않습니다"); return; }
        if (LdnTrade.QueueGame != null) { Note.Show("리빙덱스 대기열이 있어요 · 먼저 '비우기'를 누르세요"); return; }
        LdnTrade.Offers.Add(pk.Clone());
        if (LdnExport.GameOf(pk)?.Game is { } g && g != game) { game = g; mode = "trade"; }
        Refresh();
    }

    private async Task PickGame()
    {
        var page = Application.Current.Windows[0].Page;
        var pick = await page.DisplayActionSheetAsync("게임", "취소", null, LdnTrade.Games.Select(g => g.Name).ToArray());
        var g = LdnTrade.Games.FirstOrDefault(x => x.Name == pick); if (g == null) return;
        game = g.Key; Preferences.Set("ldn_game", game);
        mode = Preferences.Get("ldn_mode_" + game, "trade"); giftKey = Preferences.Get("ldn_gift_" + game, ""); giftFile = null;
        Refresh();
    }

    private async Task PickMode()
    {
        var page = Application.Current.Windows[0].Page;
        var ok = LdnTrade.Modes.Where(m => ModeOk(m.Key)).ToList();
        var pick = await page.DisplayActionSheetAsync("할 일", "취소", null, ok.Select(m => m.Name).ToArray());
        var m = ok.FirstOrDefault(x => x.Name == pick); if (m == null) return;
        mode = m.Key; Preferences.Set("ldn_mode_" + game, mode); Refresh();
    }

    // ===== 이상한 소포 =====
    private List<(string Key, string Group, string Label, string Note)> gifts;
    private string giftsGame;
    private List<(string Key, string Group, string Label, string Note)> GiftList() { if (giftsGame != game) { gifts = LdnTrade.Gifts(game); giftsGame = game; } return gifts; }
    private string giftGroup;

    private void RefreshGift()
    {
        if (mode != "gift") return;
        var list = GiftList();
        var cur = list.FirstOrDefault(x => x.Key == giftKey);
        if (giftFile != null) { giftGroupL.Text = "파일"; giftItemL.Text = Path.GetFileName(giftFile); giftNote.Text = ""; return; }
        if (cur.Key == null && list.Count > 0) { cur = list[0]; giftKey = cur.Key; }
        giftGroup = cur.Group ?? giftGroup;
        giftGroupL.Text = giftGroup ?? "-"; giftItemL.Text = cur.Label ?? "-"; giftNote.Text = cur.Note ?? "";
    }

    private async Task PickGiftGroup()
    {
        var groups = GiftList().Select(x => x.Group).Distinct().ToArray(); if (groups.Length == 0) return;
        var pick = await Application.Current.Windows[0].Page.DisplayActionSheetAsync("분류", "취소", null, groups);
        if (pick == null || !groups.Contains(pick)) return;
        giftGroup = pick; giftFile = null;
        var first = GiftList().First(x => x.Group == pick); giftKey = first.Key; Preferences.Set("ldn_gift_" + game, giftKey); RefreshGift();
    }

    private void PickGiftItem()
    {
        var items = GiftList().Select((x, i) => (x, i)).Where(t => giftGroup == null || t.x.Group == giftGroup).ToList();
        if (items.Count == 0) return;
        SheetHost.Show(new PickerSheet("보낼 소포", items.Select(t => new ComboItem(t.x.Label, t.i)).ToList(), items.FindIndex(t => t.x.Key == giftKey) is var at && at >= 0 ? items[at].i : -1,
            c => { giftKey = GiftList()[c.Value].Key; giftFile = null; Preferences.Set("ldn_gift_" + game, giftKey); RefreshGift(); }));
    }

    private async Task PickGiftFile()
    {
        var f = await FilePicker.PickAsync(); if (f == null) return;
        var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
        if (ext is not (".wc8" or ".wc3" or ".pokegift")) { Note.Show(".wc8 · .wc3 · .pokegift 파일만 됩니다"); return; }
        var dest = Path.Combine(LdnTrade.Home, "gift-file-" + game + ext);
        await using (var st = await f.OpenReadAsync()) await using (var o = File.Create(dest)) await st.CopyToAsync(o);
        giftFile = dest; RefreshGift();
    }

    // ===== 세이브 복원 =====
    private void RefreshRestore()
    {
        if (mode != "save_restore") return;
        restoreL.Text = restoreCurrent ? "지금 편집 중인 세이브" : restoreFile != null ? Path.GetFileName(restoreFile) : "고르세요";
    }

    private async Task PickRestore()
    {
        var page = Application.Current.Windows[0].Page;
        const string cur = "지금 편집 중인 세이브", file = "파일 고르기…";
        var backups = LdnTrade.SavedBackups().Take(10).ToList();
        var opts = new List<string>(); if (AppState.Sav is SAV3FRLG) opts.Add(cur); opts.Add(file); opts.AddRange(backups.Select(b => "백업: " + b.Name));
        var pick = await page.DisplayActionSheetAsync("콘솔에 쓸 세이브", "취소", null, opts.ToArray());
        if (pick == cur) { restoreCurrent = true; restoreFile = null; }
        else if (pick == file)
        {
            var f = await FilePicker.PickAsync(); if (f == null) return;
            var dest = Path.Combine(LdnTrade.Home, "saves", "imported-" + DateTimeOffset.Now.ToUnixTimeSeconds() + "_" + f.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            await using (var st = await f.OpenReadAsync()) await using (var o = File.Create(dest)) await st.CopyToAsync(o);
            if (new FileInfo(dest).Length != 0x20000) { File.Delete(dest); Note.Show("파이어레드·리프그린 세이브(128KB .sav)가 아닙니다"); return; }
            restoreFile = dest; restoreCurrent = false;
        }
        else if (pick?.StartsWith("백업: ") == true) { restoreFile = backups.First(b => "백업: " + b.Name == pick).FullName; restoreCurrent = false; }
        RefreshRestore();
    }

    // ===== 준비 =====
    private async Task PickKeys()
    {
        var f = await FilePicker.PickAsync(); if (f == null) return;
        await using var st = await f.OpenReadAsync(); using var ms = new MemoryStream(); await st.CopyToAsync(ms);
        var bad = LdnTrade.CheckKeys(ms.ToArray());
        if (bad != null) { await Application.Current.Windows[0].Page.DisplayAlertAsync("prod.keys", bad, "확인"); return; }
        await File.WriteAllBytesAsync(LdnTrade.KeysPath, ms.ToArray());
        UpdateKeys(); Note.Show("prod.keys를 가져왔습니다");
    }

    private async Task Flash()
    {
        if (OperatingSystem.IsIOS())
        {
            Note.Show("아이폰에서는 USB로 보드 펌웨어를 설치할 수 없어요. 컴퓨터의 PokeLDN에서 Bluetooth 무선 펌웨어를 먼저 설치해 주세요.");
            return;
        }
        if (LdnTrade.Running) { Note.Show("진행 중에는 설치할 수 없어요"); return; }
        var page = Application.Current.Windows[0].Page;
        const string normal = "무선 펌웨어 (USB 케이블로 쓸 때)", ble = "블루투스 무선 펌웨어 (ESP32-C6·클래식 ESP32)";
        var pick = await page.DisplayActionSheetAsync($"보드 펌웨어 설치 (v{LdnTrade.FwVersion})\n\nESP32-C3·C6·S3: 보드를 뽑고 BOOT 버튼을 누른 채 다시 꽂은 뒤 1~2초 뒤 떼요. 끝나면 RST를 누르거나 다시 꽂으세요.\n\n클래식 ESP32 (CP2102·CH340 칩): 그냥 꽂아 두면 앱이 알아서 다운로드 모드로 넣고, 끝나면 다시 켜요. 안 되면 BOOT를 누른 채 EN을 눌렀다 떼세요.\n\n실패해도 보드는 망가지지 않아요.", "취소", null, normal, ble);
        if (pick != normal && pick != ble) return;
        var st = LdnTrade.Bridge.UsbState();
        if (st == "none") { Note.Show("보드를 찾지 못했어요. 다시 꽂아 보세요"); return; }
        if (st == "permission") { LdnTrade.Bridge.RequestUsb(); Note.Show("USB 사용을 허용한 뒤 다시 누르세요"); return; }
        if (!LdnTrade.Begin(null, "flash", game, pick == ble, flash: true)) Note.Show("이미 진행 중입니다");
    }

    // ===== 시작 =====
    private async Task Start()
    {
        var page = Application.Current.Windows[0].Page;
        if (LdnTrade.Running) return;
        if (OperatingSystem.IsIOS() && !Ble)
        {
            Note.Show("아이폰은 일반 ESP32 USB-시리얼 데이터 연결을 지원하지 않아요. 블루투스 방식을 선택하세요.");
            return;
        }
        if (!LdnTrade.HasKeys) { await page.DisplayAlertAsync("PokeLDN 교환", "먼저 올바른 prod.keys를 가져오세요.", "확인"); return; }
        var sav = AppState.Sav;
        var offers = LdnTrade.Offers.Where(p => LdnExport.GameOf(p)?.Game == game).ToList();
        if (mode == "trade" && offers.Count == 0) { Note.Show("보낼 포켓몬을 추가하세요 (같은 게임 포켓몬)"); return; }
        if (mode == "gift" && giftFile == null && string.IsNullOrEmpty(giftKey)) { Note.Show("보낼 이상한 소포를 고르세요"); return; }
        if (mode == "save_restore" && !restoreCurrent && restoreFile == null) { Note.Show("콘솔에 쓸 세이브를 고르세요"); return; }
        if (Ble)
        {
            // iOS requests Bluetooth access when the native CoreBluetooth manager starts scanning.
            // MAUI's Permissions.Bluetooth permission is Android-only.
            if (OperatingSystem.IsAndroid())
            {
                var perm = await Permissions.RequestAsync<Permissions.Bluetooth>();
                if (perm != PermissionStatus.Granted) { Note.Show("블루투스로 보드를 찾으려면 '근처 기기' 권한이 필요해요"); return; }
            }
        }
        else
        {
            var st = LdnTrade.Bridge.UsbState();
            if (st == "none") { Note.Show("보드가 연결되어 있지 않아요"); return; }
            if (st == "permission") { LdnTrade.Bridge.RequestUsb(); Note.Show("USB 사용을 허용한 뒤 다시 시작하세요"); return; }
        }
        bool lg = Preferences.Get("ldn_leafgreen", sav?.Version == GameVersion.LG);
        string restore = null;
        if (mode == "save_restore")
        {
            if (restoreCurrent)
            {
                if (sav is not SAV3FRLG) { Note.Show("지금 세이브는 파이어레드·리프그린이 아닙니다"); return; }
                restore = Path.Combine(LdnTrade.Home, "saves", "from-editor.sav");
                Directory.CreateDirectory(Path.GetDirectoryName(restore));
                await File.WriteAllBytesAsync(restore, sav.Write().ToArray());
            }
            else restore = restoreFile;
            if (!await page.DisplayAlertAsync("세이브 복원", $"콘솔의 {(lg ? "리프그린" : "파이어레드")} 세이브를 이것으로 바꿀까요?\n\n{(restoreCurrent ? "지금 편집 중인 세이브" : Path.GetFileName(restore))}\n\n모두 쓰고 확인한 뒤에만 바뀌어요. 지금 콘솔 세이브를 아직 백업하지 않았다면 먼저 백업하세요.", "복원", "취소")) return;
        }
        // 보낼 포켓몬 → PokeLDN 교환 형식 파일
        var paths = new List<string>();
        if (mode == "trade")
        {
            var dir = Path.Combine(LdnTrade.Home, "offers", game); Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir)) try { File.Delete(f); } catch { }
            for (int i = 0; i < offers.Count; i++)
            {
                var path = Path.Combine(dir, $"{i + 1}_" + LdnExport.FileName(offers[i]));
                await File.WriteAllBytesAsync(path, LdnExport.Prepare(offers[i]));
                paths.Add(path);
            }
        }
        // 트레이너: 지금 세이브가 이 게임이면 그 이름·ID, 아니면 PokeLDN 기본
        bool same = sav != null && LdnTrade.KeyOf(sav) == game;
        string ot = same ? sav.OT : "", tid = "", sid = "";
        int lang = same ? sav.Language : 8, gender = same ? sav.Gender : 0;
        if (same) { if (game == "frlg") { tid = sav.TID16.ToString(); sid = sav.SID16.ToString(); } else { tid = sav.DisplayTID.ToString(); sid = sav.DisplaySID.ToString(); } }
        if (lang <= 0) lang = 8;
        string outDir = Path.Combine(LdnTrade.Home, "received", game), saveDir = Path.Combine(LdnTrade.Home, "saves");
        Directory.CreateDirectory(outDir); Directory.CreateDirectory(saveDir);
        // 요청 JSON (교환 앱 화면이 만드는 것과 같은 항목). 리플렉션 없이 직접 씀 (릴리스 빌드 트리밍 대비)
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("game", game); w.WriteString("mode", mode);
            w.WriteString("role", mode == "trade" ? (IsHost ? "host" : "join") : mode == "evolve" ? "join" : "host");
            w.WriteString("code", new string((code.Text ?? "").Where(char.IsDigit).ToArray()));
            w.WriteStartArray("offers"); foreach (var p in paths) w.WriteStringValue(p); w.WriteEndArray();
            w.WriteString("version", lg ? "leafgreen" : "firered");
            w.WriteString("save_dir", saveDir); w.WriteString("keys", LdnTrade.KeysPath); w.WriteString("out", outDir);
            w.WriteString("fw_version", LdnTrade.FwVersion); w.WriteNumber("tx_power", lowPower.IsToggled ? 34 : 0);
            w.WriteString("data", Path.Combine(LdnTrade.Home, "pokeldn"));
            w.WriteString("ot", ot); w.WriteString("tid", tid); w.WriteString("sid", sid); w.WriteNumber("language", lang); w.WriteNumber("gender", gender);
            if (mode == "trade" && game == "sv" && autoOt.IsToggled)
            {
                w.WriteBoolean("auto_ot", true);
                w.WriteStartArray("auto_ot_info");
                foreach (var p in offers)
                {
                    var (okOt, nick, names) = AutoOtInfo(p);
                    w.WriteStartObject(); w.WriteBoolean("ok", okOt); w.WriteBoolean("nick", nick);
                    w.WriteStartObject("names"); foreach (var (l, n) in names) w.WriteString(l.ToString(), n); w.WriteEndObject();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            if (restore != null) w.WriteString("restore", restore);
            if (mode == "gift")
            {
                w.WriteStartObject("gift");
                if (giftFile != null) { w.WriteString("key", "file"); w.WriteString("file", giftFile); } else w.WriteString("key", giftKey);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        if (!LdnTrade.Begin(System.Text.Encoding.UTF8.GetString(ms.ToArray()), mode, game, Ble))
        {
            Note.Show(LdnTrade.Running || LdnTrade.Bridge.Busy
                ? "이미 진행 중입니다"
                : $"PokeLDN 통신 모듈을 시작하지 못했어요.\n{LdnTrade.Bridge.Error}");
            return;
        }
        Refresh();
    }

    /// <summary>자동 어버이작을 해도 합법인지 (임시 트레이너로 바꿔 PKHeX로 확인), 별명 여부, 언어별 종 이름.</summary>
    private static (bool Ok, bool Nick, List<(int, string)> Names) AutoOtInfo(PKM p)
    {
        var names = new[] { 1, 2, 3, 4, 5, 7, 8, 9, 10 }.Select(l => (l, SpeciesName.GetSpeciesNameGeneration(p.Species, l, 9))).ToList();
        if (p.IsEgg || p.FatefulEncounter) return (false, p.IsNicknamed, names);
        try
        {
            var t = p.Clone();
            GenHost.AutoOT(t, "PKHeXKR", (uint)Random.Shared.Next(1, int.MaxValue), 0, p.Language);
            return (GenHost.IsLegal(t) || !GenHost.IsLegal(p), p.IsNicknamed, names);
        }
        catch { return (false, p.IsNicknamed, names); }
    }

    /// <summary>리빙덱스 교환: 이 게임 리빙덱스를 만들어 보낼 목록(대기열)으로. 콘솔 세이브는 교환으로 채움 (커펌 없이).</summary>
    private async Task LivingDex()
    {
        var page = Application.Current.Windows[0].Page;
        if (LdnTrade.Running) { Note.Show("교환 중에는 만들 수 없어요"); return; }
        if (game == "frlg") { Note.Show("파이어레드·리프그린은 '세이브 백업 → 리빙덱스 채우기 → 세이브 복원'이 훨씬 빨라요"); return; }
        int queued = LdnTrade.QueueCount(game);
        if (queued > 0)
        {
            var pick = await page.DisplayActionSheetAsync($"남아 있는 리빙덱스 대기열 {queued}마리", "취소", null, "이어서 보내기 (불러오기)", "새로 만들기");
            if (pick is null or "취소") return;
            if (pick.StartsWith("이어서")) { int n0 = LdnTrade.LoadQueue(game); Note.Show($"대기열 {n0}마리를 불러왔어요 · 교환 시작을 누르세요"); Refresh(); return; }
        }
        SheetHost.Show(new LivingDexSheet(game));
    }

    private void OnChanged() => MainThread.BeginInvokeOnMainThread(() => { Render(); Refresh(); });

    private void Render()
    {
        var lines = LdnTrade.StatusLines.Skip(Math.Max(0, LdnTrade.StatusLines.Count - 14)).ToList();
        if (LdnTrade.ProgressLine.Length > 0) lines.Add(LdnTrade.ProgressLine);
        if (LdnTrade.BoardLine.Length > 0) lines.Add("📶 " + LdnTrade.BoardLine);
        summary.Text = lines.Count > 0 ? string.Join("\n", lines) : (LdnTrade.Running ? "시작하는 중…" : "아직 시작하지 않았어요");
        if (log.IsVisible) { var t = LdnTrade.FullLog.ToString(); log.Text = t.Length > 20000 ? t[^20000..] : t; }
    }
}

/// <summary>PokeLDN 교환 목록 전체 (리빙덱스 대기열 포함). 누르면 편집기로 불러오기·바꾸기·빼기.</summary>
public class LdnOffersSheet : Sheet
{
    public record Cell(int Index, string Sprite, bool Shiny, string Info);
    private readonly CollectionView grid = new() { SelectionMode = SelectionMode.None };
    private readonly Label status = T.L("", 12, sub: true);

    private readonly bool fromLdn;
    public LdnOffersSheet(bool fromLdn = false) : base("PokeLDN 교환 목록", 0.85)
    {
        this.fromLdn = fromLdn;
        grid.ItemsLayout = new GridItemsLayout(6, ItemsLayoutOrientation.Vertical) { VerticalItemSpacing = 4, HorizontalItemSpacing = 4 };
        grid.ItemTemplate = new DataTemplate(() =>
        {
            var g = new Grid { HeightRequest = 58 };
            var bg = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 } };
            bg.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#F9FAFB"), Color.FromArgb("#22262F"));
            var img = new Image { WidthRequest = 52, HeightRequest = 44 }; img.SetBinding(Image.SourceProperty, "Sprite");
            var star = new Image { Source = "rare_icon.png", WidthRequest = 13, HeightRequest = 13, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Margin = 3 };
            star.SetBinding(IsVisibleProperty, "Shiny");
            var n = new Label { FontSize = 9, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.End, Margin = new Thickness(3, 0, 0, 1) }; T.Sub(n); n.SetBinding(Label.TextProperty, "Info");
            g.Add(bg); g.Add(img); g.Add(star); g.Add(n);
            var t = new TapGestureRecognizer(); t.Tapped += async (_, _) => { if (g.BindingContext is Cell c) await LdnTrade.OfferActions(c.Index, CloseAll); }; g.GestureRecognizers.Add(t);
            return g;
        });
        var open = T.Pill("교환 화면 열기", primary: true); open.HorizontalOptions = LayoutOptions.Fill;
        open.Clicked += async (_, _) => { Close(); if (!fromLdn) await LdnTrade.Open(null); };
        var clear = T.Pill("비우기"); clear.HorizontalOptions = LayoutOptions.Fill;
        clear.Clicked += async (_, _) =>
        {
            if (LdnTrade.Offers.Count == 0) return;
            if (!await Application.Current.Windows[0].Page.DisplayAlertAsync("교환 목록 비우기", $"{LdnTrade.Offers.Count}마리를 목록에서 뺄까요?" + (LdnTrade.QueueGame != null ? " (리빙덱스 대기열도 지워요)" : ""), "비우기", "취소")) return;
            LdnTrade.Offers.Clear(); if (LdnTrade.QueueGame is { } qg) LdnTrade.DropQueue(qg); LdnTrade.NotifyChanged();
        };
        var row = T.Cols(2, 8); row.Add(open, 0); row.Add(clear, 1);
        var hint = T.L("순서대로 교환해요. 누르면 편집기로 불러와 확인하거나, 편집기에서 고친 포켓몬으로 바꾸거나, 목록에서 뺄 수 있어요.", 12, sub: true); hint.LineBreakMode = LineBreakMode.WordWrap;
        Body.RowDefinitions.Add(new(GridLength.Auto)); Body.RowDefinitions.Add(new(GridLength.Star));
        Body.Add(new VerticalStackLayout { Spacing = 8, Children = { hint, row, status } }, 0, 0);
        Body.Add(grid, 0, 1);
        LdnTrade.Changed += OnChanged;
        Closed += () => LdnTrade.Changed -= OnChanged;
        Fill();
    }

    private void CloseAll() { Close(); while (SheetHost.CloseTop()) { } }
    private void OnChanged() => MainThread.BeginInvokeOnMainThread(Fill);

    private void Fill()
    {
        var list = LdnTrade.Offers;
        grid.ItemsSource = list.Select((p, i) => new Cell(i, AppState.Sprite(p), p.IsShiny, $"{i + 1}")).ToList();
        var game = list.Count > 0 ? LdnExport.GameOf(list[0])?.Game : null;
        var gname = game == null ? "" : LdnTrade.Games.FirstOrDefault(g => g.Key == game)?.Name ?? game;
        status.Text = list.Count == 0 ? "교환 목록이 비어 있어요 (박스에서 여러 칸 선택 → 'PokeLDN 교환으로 보내기', 또는 교환 화면의 '리빙덱스로 채우기')"
            : $"{gname} · {list.Count}마리" + (LdnTrade.QueueGame != null ? " · 리빙덱스 대기열 (교환할 때마다 줄어들고, 다음에 이어서)" : "") + (LdnTrade.Running ? " · 교환 중" : "");
    }
}
