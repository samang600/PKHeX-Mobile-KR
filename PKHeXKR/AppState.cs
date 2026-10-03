using PKHeX.Core;
using PKHeX.Core.Injection;
using PKHeX.Core.AutoMod;
namespace PKHeXKR;

/// <summary>앱 전역 상태: 세이브, 편집 중인 개체, 박스 위치, 라이브헥스 연결.</summary>
public static class AppState
{
    public static SaveFile Sav { get; private set; }
    public static PKM Pk { get; private set; }
    public static bool Dirty { get; private set; }
    public static int Box;
    public static bool PartyMode;
    public static (bool Party, int Box, int Index)? Source;   // 편집 중인 개체를 불러온 칸
    public static FilteredGameDataSource Src { get; private set; }
    public static PokeSysBotMini Remote;
    public static bool LiveConnected => Remote?.Connected == true && (LiveSav == null || LiveSav == Sav);

    public static event Action PkLoaded;
    public static event Action HistoryChanged;
    public abstract record Act;
    public sealed record PkAct(PKM Pk, bool Dirty, (bool, int, int)? Src) : Act;
    public sealed record SlotAct(bool Party, int Box, int Index, PKM Before, PKM After) : Act;
    public sealed record SwapAct(int BoxA, int IndexA, int BoxB, int IndexB) : Act;
    private static readonly Stack<Act> undo = new(), redo = new();
    private static DateTime lastEdit;
    public static bool CanUndo => undo.Count > 0;
    public static bool CanRedo => redo.Count > 0;
    private static void Push(Act a) { undo.Push(a); redo.Clear(); if (undo.Count > 100) { var keep = undo.Take(100).Reverse().ToList(); undo.Clear(); foreach (var k in keep) undo.Push(k); } HistoryChanged?.Invoke(); }
    /// <summary>편집 직전 상태 기록 (연속 입력은 1.5초 단위로 묶음).</summary>
    public static void Checkpoint(bool force = false)
    {
        if (!force && (DateTime.Now - lastEdit).TotalSeconds < 1.5 && undo.TryPeek(out var top) && top is PkAct) { lastEdit = DateTime.Now; return; }
        lastEdit = DateTime.Now;
        Push(new PkAct(Pk.Clone(), Dirty, Source));
    }
    public static void Undo() => Step(undo, redo);
    public static void Redo() => Step(redo, undo);
    private static void Step(Stack<Act> from, Stack<Act> to)
    {
        if (from.Count == 0) return;
        var a = from.Pop();
        if (a is PkAct p) { to.Push(new PkAct(Pk.Clone(), Dirty, Source)); Pk = p.Pk; Dirty = p.Dirty; Source = p.Src; lastEdit = default; PkLoaded?.Invoke(); }
        else if (a is SlotAct sa) { SetSlotRaw(sa.Party, sa.Box, sa.Index, from == undo ? sa.Before : sa.After); to.Push(sa); BoxChanged?.Invoke(); }
        else if (a is SwapAct sw) { SwapRaw(sw.BoxA, sw.IndexA, sw.BoxB, sw.IndexB); to.Push(sw); BoxChanged?.Invoke(); }
        HistoryChanged?.Invoke();
    }
    private static PKM GetSlot(bool party, int box, int index) => party ? (index < Sav.PartyCount ? Sav.GetPartySlotAtIndex(index) : Sav.BlankPKM) : Sav.GetBoxSlotAtIndex(box, index);
    private static void SetSlotRaw(bool party, int box, int index, PKM p)
    {
        if (!party) { Sav.SetBoxSlotAtIndex(p, box, index); return; }
        if (p.Species == 0) { if (index < Sav.PartyCount) Sav.DeletePartySlot(index); }
        else Sav.SetPartySlotAtIndex(p, Math.Min(index, Sav.PartyCount));
    }    // 편집기 전체 다시 채우기
    public static event Action PkEdited;    // 요약·합법성 갱신
    public static event Action BoxChanged;  // 박스 다시 그리기
    public static event Action SaveChanged; // 세이브 교체

    public static readonly (string Code, string Name)[] Languages = [("ko", "한국어"), ("ja", "日本語"), ("en", "English")];
    public static string LangCode => Preferences.Get("data_lang", "ko");

    public static void InitLanguage()
    {
        GameInfo.CurrentLanguage = LangCode;
        GameInfo.Strings = GameInfo.GetStrings(LangCode);
        SetSave(NewBlankSave(), notify: false);
        _ = LoadCardWindowsAsync();
    }

    // ===== 기본 트레이너(어버이)와 게임 =====
    public static string DefOT { get => Preferences.Get("def_ot", "PKHeX"); set => Preferences.Set("def_ot", value); }
    public static byte DefGender { get => (byte)Preferences.Get("def_gender", 0); set => Preferences.Set("def_gender", (int)value); }
    public static int DefTID { get => Preferences.Get("def_tid", 123456); set => Preferences.Set("def_tid", value); }
    public static int DefSID { get => Preferences.Get("def_sid", 1234); set => Preferences.Set("def_sid", value); }
    public static int DefLang { get => Preferences.Get("def_lang", (int)LanguageID.Korean); set => Preferences.Set("def_lang", value); }
    public static GameVersion DefGame { get => (GameVersion)Preferences.Get("def_game", (int)GameVersion.ZA); set => Preferences.Set("def_game", (int)value); }

    public static readonly GameVersion[] Games = [GameVersion.ZA, GameVersion.SL, GameVersion.VL, GameVersion.PLA, GameVersion.BD, GameVersion.SP, GameVersion.SW, GameVersion.SH,
        GameVersion.GP, GameVersion.GE, GameVersion.US, GameVersion.UM, GameVersion.SN, GameVersion.MN, GameVersion.OR, GameVersion.AS, GameVersion.X, GameVersion.Y,
        GameVersion.B2, GameVersion.W2, GameVersion.B, GameVersion.W, GameVersion.HG, GameVersion.SS, GameVersion.Pt, GameVersion.D, GameVersion.P,
        GameVersion.E, GameVersion.FR, GameVersion.LG, GameVersion.R, GameVersion.S, GameVersion.C, GameVersion.GD, GameVersion.SI, GameVersion.YW, GameVersion.RD, GameVersion.BU];
    public static string GameName(GameVersion v) { var g = GameInfo.Strings.gamelist; return (int)v < g.Length && !string.IsNullOrEmpty(g[(int)v]) ? g[(int)v] : v.ToString(); }

    // 게임별 트레이너 저장 (없으면 공통 기본값)
    private static string K(string n, GameVersion v) => $"{n}_{v}";
    public static string OTFor(GameVersion v) => Preferences.Get(K("def_ot", v), DefOT);
    public static byte GenderFor(GameVersion v) => (byte)Preferences.Get(K("def_gender", v), (int)DefGender);
    public static int TIDFor(GameVersion v) => Preferences.Get(K("def_tid", v), DefTID);
    public static int SIDFor(GameVersion v) => Preferences.Get(K("def_sid", v), DefSID);
    public static int LangFor(GameVersion v) => Preferences.Get(K("def_lang", v), DefaultLang(v));
    /// <summary>게임에 실제로 있는 언어 중 기본값: 한국어판이 없으면 일본 전용판은 일본어, 나머지는 영어.</summary>
    public static int DefaultLang(GameVersion v)
    {
        bool noKorean = v is GameVersion.RD or GameVersion.GN or GameVersion.BU or GameVersion.YW or GameVersion.C
            or GameVersion.R or GameVersion.S or GameVersion.E or GameVersion.FR or GameVersion.LG or GameVersion.CXD;
        if (!noKorean) return DefLang;
        if (v is GameVersion.GN or GameVersion.BU) return (int)LanguageID.Japanese;   // 그린·블루는 일본판만
        return DefLang == (int)LanguageID.Korean ? (int)LanguageID.English : DefLang;
    }
    /// <summary>저장된 언어가 그 게임에 없는 언어면 바로잡음.</summary>
    private static int SafeLang(GameVersion v) { var l = LangFor(v); return l == (int)LanguageID.Korean && DefaultLang(v) != DefLang ? DefaultLang(v) : l; }
    public static void SetTrainerFor(GameVersion v, string ot, byte gender, int tid, int sid, int lang)
    {
        Preferences.Set(K("def_ot", v), ot); Preferences.Set(K("def_gender", v), (int)gender);
        Preferences.Set(K("def_tid", v), tid); Preferences.Set(K("def_sid", v), sid); Preferences.Set(K("def_lang", v), lang);
    }

    public static SaveFile NewBlankSave() => NewBlankSave(DefGame);
    public static SaveFile NewBlankSave(GameVersion v)
    {
        var s = BlankSaveFile.Get(v, OTFor(v), (LanguageID)SafeLang(v));
        ApplyTrainer(s);
        return s;
    }

    /// <summary>기본 트레이너 정보를 세이브에 적용 (7세대 이상은 6자리 TID/4자리 SID 표시 기준).</summary>
    public static void ApplyTrainer(SaveFile s)
    {
        var v = s.Version;
        // 게임마다 없는 항목이 있음 (1세대는 성별·SID 없음 등) → 항목별로 따로 시도해 하나가 실패해도 계속
        try { s.OT = OTFor(v); } catch { }
        if (s.Generation >= 2) try { s.Gender = GenderFor(v); } catch { }
        try { s.Language = SafeLang(v); } catch { }
        try
        {
            if (s.Generation >= 7) s.ID32 = (uint)(SIDFor(v) * 1_000_000L + TIDFor(v));
            else { s.TID16 = (ushort)Math.Min(TIDFor(v), 65535); if (s.Generation >= 3) s.SID16 = (ushort)Math.Min(SIDFor(v), 65535); }
        }
        catch { }
        if (s is IRegionOrigin r) { r.ConsoleRegion = 5; r.Country = 136; r.Region = 1; }   // 3DS: 한국 본체
    }

    public static void SetSave(SaveFile s, bool notify = true)
    {
        Sav = s;
        Src = new FilteredGameDataSource(s, GameInfo.Sources, HaX);
        ParseSettings.InitFromSaveFileData(s);
        // 홈 트래커 없음: 소드실드에서만 검사 생략(주의 수준), 그 외 게임은 불법
        ParseSettings.Settings.HOMETransfer.HOMETransferTrackerNotPresent = TrackerSeverity(s);
        if (!HandlerCheck) ParseSettings.ClearActiveTrainer();
        ClearLegalCache();
        // ALM(자동 합법화) 설정: 세이브 트레이너 기준, 실패 시 장난 개체(이스터에그) 끔
        APILegality.AllowTrainerOverride = true; APILegality.SetMatchingBalls = true; APILegality.ForceSpecifiedBall = true;
        APILegality.SetAllLegalRibbons = false; APILegality.Timeout = 45; Legalizer.EnableEasterEggs = false;
        TrainerSettings.Clear(); TrainerSettings.Register(s);
        Box = Math.Clamp(s.CurrentBox, 0, Math.Max(0, s.BoxCount - 1));
        PartyMode = false;
        Pk = string.IsNullOrEmpty(s.Metadata.FileName) ? Placeholder(s) : s.BlankPKM; Dirty = false; Source = null; undo.Clear(); redo.Clear(); HistoryChanged?.Invoke();
        if (Remote != null) { var r = Remote; Remote = null; LiveSav = null; _ = Task.Run(() => { try { r.com.Disconnect(); } catch { } }); }   // 세이브를 바꾸면 연결 해제 (백그라운드)
        if (notify) { SaveChanged?.Invoke(); PkLoaded?.Invoke(); BoxChanged?.Invoke(); }
    }

    public static void Load(PKM p, (bool, int, int)? source = null)
    {
        if (Pk != null && Pk.Species != 0) Checkpoint(force: true);
        Source = source;
        var conv = p.GetType() == Sav.PKMType ? p : EntityConverter.ConvertToType(p, Sav.PKMType, out _);
        Pk = (conv ?? p).Clone();
        Dirty = false;
        PkLoaded?.Invoke();
    }

    public static void ReapplyTrainer()
    {
        ClearLegalCache();
        ApplyTrainer(Sav);
        ParseSettings.InitFromSaveFileData(Sav);
        if (!HandlerCheck) ParseSettings.ClearActiveTrainer();
        TrainerSettings.Clear(); TrainerSettings.Register(Sav);
        SaveChanged?.Invoke();
    }

    /// <summary>박스 칸끼리 옮기기: 대상 칸에 포켓몬이 있으면 서로 자리 바꿈.</summary>
    public static void MoveSlot(int boxA, int indexA, int boxB, int indexB)
    {
        if (boxA == boxB && indexA == indexB) return;
        SwapRaw(boxA, indexA, boxB, indexB);
        Push(new SwapAct(boxA, indexA, boxB, indexB));
        if (Source is { Party: false } src)
        {
            if (src.Box == boxA && src.Index == indexA) Source = (false, boxB, indexB);
            else if (src.Box == boxB && src.Index == indexB) Source = (false, boxA, indexA);
        }
        BoxChanged?.Invoke();
    }
    private static void SwapRaw(int boxA, int indexA, int boxB, int indexB)
    {
        var a = Sav.GetBoxSlotAtIndex(boxA, indexA); var b = Sav.GetBoxSlotAtIndex(boxB, indexB);
        Sav.SetBoxSlotAtIndex(b, boxA, indexA); Sav.SetBoxSlotAtIndex(a, boxB, indexB);
    }
    /// <summary>요약 카드 등에서 바꾼 값을 편집 탭에도 반영.</summary>
    public static void ReloadEditors() => PkLoaded?.Invoke();

    // ===== 여러 인스턴스 (세이브 여러 개를 동시에 열어 오가며 편집) =====
    public sealed class Instance
    {
        public SaveFile Sav; public FilteredGameDataSource Src; public PKM Pk; public bool Dirty; public int Box; public bool PartyMode;
        public (bool, int, int)? Source; public Act[] Undo = []; public Act[] Redo = [];
        public string Title => $"{GameName(Sav.Version)} · {Sav.OT}" + (string.IsNullOrEmpty(Sav.Metadata.FileName) ? "" : $" · {Sav.Metadata.FileName}");
    }
    public static readonly List<Instance> Instances = [];
    public static int Active;
    public static SaveFile LiveSav;              // 라이브헥스는 한 번에 한 인스턴스(세이브)만
    public static PKM Clipboard;                 // 인스턴스 간 복사·붙여넣기

    private static Instance Snapshot() => new() { Sav = Sav, Src = Src, Pk = Pk, Dirty = Dirty, Box = Box, PartyMode = PartyMode, Source = Source, Undo = undo.Reverse().ToArray(), Redo = redo.Reverse().ToArray() };
    private static void Restore(Instance i)
    {
        Sav = i.Sav; Src = i.Src; Pk = i.Pk; Dirty = i.Dirty; Box = i.Box; PartyMode = i.PartyMode; Source = i.Source;
        undo.Clear(); foreach (var a in i.Undo) undo.Push(a); redo.Clear(); foreach (var a in i.Redo) redo.Push(a);
        ParseSettings.InitFromSaveFileData(Sav);
        ParseSettings.Settings.HOMETransfer.HOMETransferTrackerNotPresent = TrackerSeverity(Sav);
        TrainerSettings.Clear(); TrainerSettings.Register(Sav);
        SaveChanged?.Invoke(); PkLoaded?.Invoke(); BoxChanged?.Invoke(); HistoryChanged?.Invoke();
    }
    public static void SaveActive() { if (Instances.Count == 0) Instances.Add(Snapshot()); else Instances[Active] = Snapshot(); }
    public static void SwitchTo(int index)
    {
        if (index < 0 || index >= Instances.Count || index == Active) return;
        SaveActive(); Active = index; Restore(Instances[index]);
    }
    /// <summary>새 인스턴스를 만들고 그 인스턴스로 전환.</summary>
    public static void AddInstance(SaveFile s)
    {
        SaveActive();
        Instances.Add(null); Active = Instances.Count - 1;
        SetSave(s);
        Instances[Active] = Snapshot();
    }
    public static void CloseInstance(int index)
    {
        if (Instances.Count <= 1) return;
        SaveActive();
        if (Instances[index].Sav == LiveSav) { try { Remote?.com.Disconnect(); } catch { } LiveSav = null; }
        Instances.RemoveAt(index);
        Active = Math.Min(Active > index ? Active - 1 : Active, Instances.Count - 1);
        Restore(Instances[Active]);
    }
    /// <summary>다른 인스턴스의 편집기로 보내기 (대상 게임 형식으로 변환).</summary>
    public static bool SendTo(int index, PKM p)
    {
        if (index < 0 || index >= Instances.Count || index == Active) return false;
        SaveActive();
        var t = Instances[index];
        var conv = p.GetType() == t.Sav.PKMType ? p.Clone() : EntityConverter.ConvertToType(p, t.Sav.PKMType, out _);
        if (conv == null) return false;
        t.Undo = [.. t.Undo, new PkAct(t.Pk.Clone(), t.Dirty, t.Source)]; t.Redo = [];
        t.Pk = conv; t.Dirty = true; t.Source = null;
        return true;
    }

    /// <summary>저장된 정보가 없을 때 편집기에 넣는 빈 피카츄 (PKHeX 기본 템플릿 + 세이브 트레이너).</summary>
    public static PKM Placeholder(SaveFile s)
    {
        var p = s.BlankPKM;
        try
        {
            EntityTemplates.TemplateFields(p, s);
            if (s.Personal.IsSpeciesInGame((ushort)PKHeX.Core.Species.Pikachu)) p.Species = (ushort)PKHeX.Core.Species.Pikachu;
            p.Form = 0; p.SetGender(p.GetSaneGender());
            if (p.Format >= 3) p.RefreshAbility(0);
            p.Nickname = PKHeX.Core.SpeciesName.GetSpeciesNameGeneration(p.Species, p.Language, p.Format); p.IsNicknamed = false;
            p.RefreshChecksum();
        }
        catch { }
        return p;
    }

    // ===== 배포 기간 (불바피디아 기준, 5~7세대 카드) =====
    public static Dictionary<string, string[][]> CardWindows = new();
    public static async Task LoadCardWindowsAsync()
    {
        try
        {
            await using var st = await FileSystem.OpenAppPackageFileAsync("card_windows.json");
            CardWindows = await System.Text.Json.JsonSerializer.DeserializeAsync<Dictionary<string, string[][]>>(st) ?? new();
        }
        catch { }
    }
    /// <summary>배포 카드의 배포 기간 중 개체 언어 지역에 맞는 첫 날짜.</summary>
    public static DateOnly? DistributionDate(MysteryGift g, int lang)
    {
        if (g.Generation is < 5 or > 7) return null;
        var tid = g.Generation >= 7 ? (g.ID32 % 1000000).ToString("000000") : g.TID16.ToString("00000");
        var key = $"{g.GetType().Name}|{g.CardID}|{GameInfo.GetStrings("en").Species[g.Species]}|{tid}";
        if (!CardWindows.TryGetValue(key, out var ws)) return null;
        bool Ok(string r) { r = r.ToLowerInvariant(); if (r.Trim() is "all" or "") return true;
            return (LanguageID)lang switch { LanguageID.Korean => r.Contains("korea"), LanguageID.Japanese => r.Contains("japan"), _ => r.Contains("american") || r.Contains("pal") || r.Contains("english") }; }
        var pick = ws.FirstOrDefault(w => Ok(w[2])) ?? ws.FirstOrDefault();
        return pick != null && DateOnly.TryParse(pick[0], out var d) ? d : null;
    }

    // ===== 교환 상대 정보 (SysBot.NET 포인터 기준) =====
    public record Partner(string OT, uint ID32, bool SixDigit, byte Gender, int Lang);
    public static Partner LastPartner;
    private static string Ptr(params long[] p)   // SysBot 포인터 목록 → Injection 포인터 문자열
    {
        var str = $"main+{p[0]:X}";
        for (int i = 1; i < p.Length; i++) str = $"[{str}]+{p[i]:X}";
        return str;
    }
    public static Partner ReadTradePartner()
    {
        if (!LiveConnected || Remote.com is not ICommunicatorNX nx) return null;
        byte[] Read(string ptr, int len) { var ofs = nx.GetPointerAddress(ptr); return ofs == 0 ? null : Remote.com.ReadBytes(ofs, len).ToArray(); }
        try
        {
            switch (Sav)
            {
                case SAV9SV:
                {
                    Partner Parse(byte[] d) => d == null ? null : new Partner(StringConverter8.GetString(d.AsSpan(8, 24)), BitConverter.ToUInt32(d, 0), true, d[5], d[6]);
                    var a = Parse(Read(Ptr(0x473A110, 0x48, 0xB0, 0x0), 0x30));
                    if (a != null && !(a.OT == Sav.OT && a.ID32 == Sav.ID32) && a.OT.Length > 0) return LastPartner = a;
                    var b = Parse(Read(Ptr(0x473A110, 0x48, 0xE0, 0x0), 0x30));
                    return b != null && b.OT.Length > 0 ? LastPartner = b : null;
                }
                case SAV9ZA:
                {
                    var baseOfs = nx.GetPointerAddress(Ptr(0x40FC3D8, 0x1D8, 0x30, 0xA0, 0x0));
                    if (baseOfs == 0) return null;
                    foreach (var shift in new ulong[] { 0x74, 0x74 + 0x598 })
                    {
                        var d = Remote.com.ReadBytes(baseOfs + shift, 34).ToArray();
                        var name = StringConverter8.GetString(d.AsSpan(8, 26));
                        if (name.Length > 0) return LastPartner = new Partner(name, BitConverter.ToUInt32(d, 0), true, Sav.Gender, Sav.Language);
                    }
                    return null;
                }
                case SAV8SWSH:
                {
                    var name = StringConverter8.GetString(Remote.com.ReadBytes(0xAF28384C, 0x1A).ToArray());
                    var id = BitConverter.ToUInt32(Remote.com.ReadBytes(0xAF28384C - 0x8, 4).ToArray(), 0);
                    return name.Length > 0 ? LastPartner = new Partner(name, id, false, Sav.Gender, Sav.Language) : null;
                }
            }
        }
        catch { }
        return null;
    }

    public static void Edited() { Dirty = true; PkEdited?.Invoke(); }
    public static void RefreshSummary() => PkEdited?.Invoke();
    public static void NotifyBox() => BoxChanged?.Invoke();

    public static PKM[] CurrentSlots()
        => PartyMode ? Enumerable.Range(0, 6).Select(i => i < Sav.PartyCount ? Sav.GetPartySlotAtIndex(i) : Sav.BlankPKM).ToArray()
                     : Sav.GetBoxData(Box);

    public static void WriteSlot(int index, PKM p)
    {
        Push(new SlotAct(PartyMode, Box, index, GetSlot(PartyMode, Box, index).Clone(), p.Clone()));
        if (PartyMode) { if (index >= Sav.PartyCount) Sav.SetPartySlotAtIndex(p, Sav.PartyCount); else Sav.SetPartySlotAtIndex(p, index); }
        else
        {
            Sav.SetBoxSlotAtIndex(p, Box, index);
            if (LiveConnected)   // 라이브헥스: 게임에도 바로 기록
            {
                try
                {
                    var q = p.Clone(); q.ResetPartyStats();
                    var data = new byte[Sav.SIZE_PARTY];
                    q.WriteEncryptedDataParty(data);
                    lock (LiveLock) Remote.SendSlot(data, Box, index);
                }
                catch { MarkLiveLost(); }
            }
        }
        Dirty = false; Source = (PartyMode, Box, index);
        BoxChanged?.Invoke();
    }

    public static void DeleteSlot(int index)
    {
        Push(new SlotAct(PartyMode, Box, index, GetSlot(PartyMode, Box, index).Clone(), Sav.BlankPKM));
        if (PartyMode) { if (index < Sav.PartyCount) Sav.DeletePartySlot(index); }
        else Sav.SetBoxSlotAtIndex(Sav.BlankPKM, Box, index);
        BoxChanged?.Invoke();
    }

    public static string BoxName(int b)
    {
        try { if (Sav is IBoxDetailName n) { var s = n.GetBoxName(b); if (!string.IsNullOrWhiteSpace(s)) return s; } } catch { }
        return $"박스 {b + 1}";
    }

    // ===== 스프라이트 =====
    public static string Sprite(ushort species, byte form, bool shiny)
    {
        if (species == 0) return null;
        var s = shiny ? "s" : "";
        var withForm = $"b_{species}_{form}{s}";
        if (SpriteIndex.Names.Contains($"b_{species}_{form}_0{s}")) withForm = $"b_{species}_{form}_0{s}";   // 마휘핑: 크림_장식 (장식 정보가 없으면 첫 장식)
        if (SpriteIndex.Names.Contains(withForm)) return withForm + ".png";
        if (form > 0 && SpriteIndex.Names.Contains(withForm)) return withForm + ".png";
        var baseName = $"b_{species}{s}";
        if (SpriteIndex.Names.Contains(baseName)) return baseName + ".png";
        // 9세대 등 박스 스프라이트가 없는 종은 SV 아이콘(a_)으로 (이로치 색 없음 → 별 표시로 구분)
        var a = $"a_{species}_{form}";
        if (form > 0 && SpriteIndex.Names.Contains(a)) return a + ".png";
        if (SpriteIndex.Names.Contains($"a_{species}")) return $"a_{species}.png";
        return shiny ? Sprite(species, form, false) : null;
    }
    public static string Sprite(PKM p)
    {
        if (p.Species == 0) return null;
        if (p.IsEgg) return "b_0.png";
        if (p.Species == (ushort)PKHeX.Core.Species.Alcremie && p is IFormArgument fa)   // 마휘핑: 크림(폼)과 장식(폼 인수)별 아이콘
        {
            var n = $"b_869_{p.Form}_{Math.Min(fa.FormArgument, 6u)}{(p.IsShiny ? "s" : "")}";
            if (SpriteIndex.Names.Contains(n)) return n + ".png";
        }
        return Sprite(p.Species, p.Form, p.IsShiny);
    }
    public static string ItemSprite(int item) => item > 0 && SpriteIndex.Names.Contains($"bitem_{item}") ? $"bitem_{item}.png" : null;
    public static string BallSprite(int ball) => ball > 0 ? $"ball{ball}.png" : null;

    /// <summary>PKHaX 모드: 기술·특성·도구 등을 제한 없이 고르고, 합법성 검사를 하지 않음.</summary>
    public static bool HaX { get; private set; } = Preferences.Get("hax", false);
    public static void SetHaX(bool on)
    {
        HaX = on; Preferences.Set("hax", on); ClearLegalCache();
        Src = new FilteredGameDataSource(Sav, GameInfo.Sources, on);
        PkLoaded?.Invoke(); BoxChanged?.Invoke(); SaveChanged?.Invoke();
    }
    // 합법성 캐시: 개체 데이터(해시)가 같으면 재검사하지 않음. 세이브·트레이너·게임·PKHaX·HOME 설정이 바뀌면 비움.
    private static readonly Dictionary<ulong, bool> legalCache = new();
    private static readonly object legalLock = new();
    /// <summary>켜면 교환받은 개체의 현재 트레이너 이름·성별을 세이브 주인과 대조. 기본 꺼짐.</summary>
    public static bool HandlerCheck { get => Preferences.Get("handler_check", false); set { Preferences.Set("handler_check", value); ApplyParse(); } }
    /// <summary>홈 트래커 없음 검사: 소드실드 / 다른 게임 따로.</summary>
    public static bool TrackerCheckSWSH { get => Preferences.Get("trk_swsh", false); set { Preferences.Set("trk_swsh", value); ApplyParse(); } }
    public static bool TrackerCheckOther { get => Preferences.Get("trk_other", true); set { Preferences.Set("trk_other", value); ApplyParse(); } }
    public static Severity TrackerSeverity(SaveFile s) => (s is SAV8SWSH ? TrackerCheckSWSH : TrackerCheckOther) ? Severity.Invalid : Severity.Fishy;
    /// <summary>"현재 트레이너는 어버이가 될 수 없습니다"(전송 개체의 현재 트레이너 표시) 검사. 기본 꺼짐.</summary>
    public static bool HTFlagCheck { get => Preferences.Get("htflag_check", false); set { Preferences.Set("htflag_check", value); ClearLegalCache(); } }
    /// <summary>끈 검사 항목만 불법 사유이면 합법으로 봄.</summary>
    public static bool EffectiveValid(LegalityAnalysis la)
    {
        if (la.Valid) return true;
        var bad = la.Results.Where(r => r.Judgement == Severity.Invalid).ToList();
        if (!HTFlagCheck) bad = bad.Where(r => r.Result != LegalityCheckResultCode.TransferHandlerFlagRequired).ToList();
        if (bad.Count == 0) return true;
        // 리본만 문제: 진화 전 포켓몬이 받을 수 있는 리본이면 허용 (예: BDSP 리본을 단 이브이 → BDSP에 없는 님피아로 진화)
        if (bad.All(r => r.Identifier == CheckIdentifier.Ribbon) && RibbonOkViaPreEvo(la)) return true;
        return false;
    }
    private static bool RibbonOkViaPreEvo(LegalityAnalysis la)
    {
        try
        {
            var pk = typeof(LegalityAnalysis).GetField("Entity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(la) as PKM;
            if (pk == null) return false;
            foreach (var s in Family(pk.Species).Where(x => x != pk.Species && x < pk.Species + 0x7FFF))
            {
                var c = pk.Clone(); c.Species = s; c.Form = 0; c.RefreshChecksum();
                var lc = Analyze(c, out _);
                if (!lc.Results.Any(r => r.Judgement == Severity.Invalid && r.Identifier == CheckIdentifier.Ribbon)) return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>같은 진화 계열 (알에서 나오는 종이 같은 포켓몬).</summary>
    public static HashSet<ushort> Family(ushort species)
    {
        var set = new HashSet<ushort> { species };
        try
        {
            ushort Hatch(ushort s) { var pi = Sav.Personal.GetFormEntry(s, 0); var v = pi.GetType().GetProperty("HatchSpecies")?.GetValue(pi); return v is ushort u && u != 0 ? u : s; }
            var root = Hatch(species);
            for (ushort s = 1; s <= Sav.MaxSpeciesID; s++) if (Hatch(s) == root) set.Add(s);
        }
        catch { }
        return set;
    }

    /// <summary>쓰레기 바이트 지우기 (이름은 그대로).</summary>
    public static void ClearNickTrash(PKM p) { try { var n = p.Nickname; p.NicknameTrash.Clear(); p.Nickname = n; } catch { } }
    public static void ClearOTTrash(PKM p) { try { var n = p.OriginalTrainerName; p.OriginalTrainerTrash.Clear(); p.OriginalTrainerName = n; } catch { } }

    public static void ApplyParse()
    {
        if (Sav == null) return;
        ParseSettings.InitFromSaveFileData(Sav);
        ParseSettings.Settings.HOMETransfer.HOMETransferTrackerNotPresent = TrackerSeverity(Sav);
        if (!HandlerCheck) ParseSettings.ClearActiveTrainer();   // "현재 트레이너 이름이 예상치와 일치하지 않습니다" 검사 생략
        ClearLegalCache();
    }
    public static readonly object LiveLock = new();
    public static event Action LiveLost;
    /// <summary>라이브헥스 통신이 실패했을 때: 연결을 정리하고 버튼 표시를 되돌림 (주기적 감시 대신 실패 시점에만).</summary>
    public static void MarkLiveLost()
    {
        var r = Remote; if (r == null) return;
        Remote = null; LiveSav = null;
        _ = Task.Run(() => { try { r.com.Disconnect(); } catch { } });
        LiveLost?.Invoke();
    }   // 라이브헥스 통신은 한 번에 하나씩

    /// <summary>성격 목록 (능력치 상승·하락 표시).</summary>
    public static List<ComboItem> NatureItems()
    {
        string[] st = ["공격", "방어", "스피드", "특공", "특방"];
        return Src.Natures.Select(c =>
        {
            int up = c.Value / 5, dn = c.Value % 5;
            var mod = c.Value is < 0 or > 24 ? "" : up == dn ? "  (보정 없음)" : $"  ({st[up]}↑ {st[dn]}↓)";
            return new ComboItem(c.Text + mod, c.Value);
        }).ToList();
    }

    /// <summary>데이터 언어를 바로 적용 (재시작 없이).</summary>
    public static void ReloadLanguage()
    {
        GameInfo.CurrentLanguage = LangCode;
        GameInfo.Strings = GameInfo.GetStrings(LangCode);
        Src = new FilteredGameDataSource(Sav, GameInfo.Sources, HaX);
        ClearLegalCache();
        SaveChanged?.Invoke(); PkLoaded?.Invoke(); BoxChanged?.Invoke();
    }

    public static void ClearLegalCache() { lock (legalLock) legalCache.Clear(); }
    private static ulong Hash(PKM p)
    {
        var b = new byte[p.SIZE_STORED]; p.WriteDecryptedDataStored(b);
        ulong h = 14695981039346656037UL; foreach (var x in b) { h ^= x; h *= 1099511628211UL; }
        return h ^ ((ulong)p.Format << 56);
    }
    public static bool IsLegal(PKM p)
    {
        if (HaX) return true;
        if (p.Species == 0) return false;
        try
        {
            var key = Hash(p);
            lock (legalLock) if (legalCache.TryGetValue(key, out var v)) return v;
            var r = EffectiveValid(Analyze(p, out _));
            lock (legalLock) { if (legalCache.Count > 5000) legalCache.Clear(); legalCache[key] = r; }
            return r;
        }
        catch { return false; }
    }

    /// <summary>
    /// 합법성 검사. 원래 게임에서 나온 개체(전송 이력 없음)가 다른 게임 리본 때문에 홈 트래커를 요구받는 경우만 추적 ID 없음을 허용.
    /// 증표 중복, 세대에 맞지 않는 리본 등 다른 불법 사유는 그대로 불법.
    /// </summary>
    /// <summary>가능한 리본 전부: 원래 게임 출신이면 홈 트래커가 필요한 리본까지 (Analyze와 같은 기준).</summary>
    private static readonly object settingLock = new();
    public static void SetAllRibbons(PKM p)
    {
        var set = ParseSettings.Settings.HOMETransfer; var old = set.HOMETransferTrackerNotPresent;
        bool native; try { native = p.Context == p.Version.Context; } catch { native = false; }
        lock (settingLock)
        {
            try { if (native) set.HOMETransferTrackerNotPresent = Severity.Fishy; RibbonApplicator.SetAllValidRibbons(p); }
            finally { set.HOMETransferTrackerNotPresent = old; }
        }
    }

    public static LegalityAnalysis Analyze(PKM p, out bool trackerWaived)
    {
        trackerWaived = false;
        var la = new LegalityAnalysis(p);
        if (la.Valid || p.Species == 0) return la;
        var set = ParseSettings.Settings.HOMETransfer;
        if (set.HOMETransferTrackerNotPresent != Severity.Invalid) return la;
        bool native;
        try { native = p.Context == p.Version.Context; } catch { native = false; }
        if (!native) return la;   // 실제로 전송된 개체는 추적 ID 필요 (소드실드 제외 규칙 유지)
        var rib = RibbonInfo.GetRibbonInfo(p).Any(r => r.HasRibbon || r.RibbonCount > 0);
        if (!rib) return la;
        lock (settingLock)
        {
            try
            {
                set.HOMETransferTrackerNotPresent = Severity.Fishy;
                var la2 = new LegalityAnalysis(p);
                if (la2.Valid) { trackerWaived = true; return la2; }   // 추적 ID만 문제였음
            }
            finally { set.HOMETransferTrackerNotPresent = Severity.Invalid; }
        }
        return la;
    }
    public static string LangName(int lang) => (LanguageID)lang switch
    {
        LanguageID.Japanese => "일본어", LanguageID.English => "영어", LanguageID.French => "프랑스어", LanguageID.Italian => "이탈리아어", LanguageID.German => "독일어",
        LanguageID.Spanish => "스페인어", LanguageID.Korean => "한국어", LanguageID.ChineseS => "중국어(간체)", LanguageID.ChineseT => "중국어(번체)", LanguageID.SpanishL => "스페인어(중남미)", _ => "",
    };
    /// <summary>개체 언어 기준의 종 이름.</summary>
    public static string SpeciesName(PKM p) { try { return PKHeX.Core.SpeciesName.GetSpeciesNameGeneration(p.Species, p.Language > 0 ? p.Language : 8, p.Format); } catch { return GameInfo.Strings.Species[p.Species]; } }

    /// <summary>라이브헥스로 게임 속 트레이너(이름·ID·성별)를 읽어 세이브·게임별 트레이너에 반영.</summary>
    public static bool ReadLiveTrainer()
    {
        if (!LiveConnected) return false;
        foreach (var name in new[] { "Trainer Data", "MyStatus" })
        {
            try
            {
                bool okRead; lock (LiveLock) okRead = Remote.Injector.ReadBlockFromString(Remote, Sav, name, out _);
                if (!okRead) continue;
                var v = Sav.Version;
                int tid = Sav.Generation >= 7 ? (int)(Sav.ID32 % 1_000_000) : Sav.TID16, sid = Sav.Generation >= 7 ? (int)(Sav.ID32 / 1_000_000) : Sav.SID16;
                SetTrainerFor(v, Sav.OT, Sav.Gender, tid, sid, Sav.Language);
                ParseSettings.InitFromSaveFileData(Sav);
                TrainerSettings.Clear(); TrainerSettings.Register(Sav);
                SaveChanged?.Invoke();
                return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>자동 어버이작: 편집 중인 개체의 어버이를 게임 트레이너로. 이로치였다면 모양(별·네모)을 유지해 다시 이로치로.</summary>
    public static void AutoOT(PKM p, Partner t)
    {
        bool shiny = p.IsShiny; bool square = shiny && p.ShinyXor == 0;
        p.OriginalTrainerName = t.OT; p.OriginalTrainerGender = t.Gender; p.ID32 = t.ID32;
        if (t.Lang > 0) { var nick = p.IsNicknamed; p.Language = t.Lang; if (!nick) { p.Nickname = PKHeX.Core.SpeciesName.GetSpeciesNameGeneration(p.Species, p.Language, p.Format); p.IsNicknamed = false; } }
        if (p.Format >= 6) { p.HandlingTrainerName = ""; p.CurrentHandler = 0; }
        if (shiny && !p.IsShiny)   // ID가 바뀌어 이로치가 풀리면 같은 모양(별·네모)으로 다시 이로치
        {
            if (p.Generation >= 6) p.PID = ShinyUtil.GetShinyPID(p.TID16, p.SID16, p.PID, square ? 0u : 1u);
            else p.SetShiny();
        }
        p.RefreshChecksum();
    }
}


/// <summary>PKHeX 한국어 번역 교정 (합법성 보고서 표시용). 원본 번역 파일은 라이브러리 안에 있어 표시할 때 바꿔 보여줌.</summary>
public static class KoFix
{
    private static readonly (string From, string To)[] Map =
    [
        ("적법한!", "합법!"),
        ("현재 어버이 값이 유효하지 않습니다. 저장 데이터의 트레이너 정보와 일치하지 않습니다.", "현재 소유자 값이 세이브의 트레이너 정보와 맞지 않습니다."),
        ("모든 리본이 채워졌습니다.", "모든 리본이 확인되었습니다(문제 없음)."),
        ("인카운터 유형 PID가 일치하지 않습니다.", "PID·개체값 생성 방식이 이 조우 유형과 맞지 않습니다."),
        ("동떨어진(Outsider) ", "교환해 온 "),
        ("정규화된", "가려진(치환된)"),
        ("Pokémon HOME 트래커 데이터가 누락되었습니다.", "홈 트래커가 없습니다."),
        ("Pokémon HOME 트래커 수치는 0이어야 합니다.", "홈 트래커는 0이어야 합니다."),
        ("Pokémon HOME 트래커", "홈 트래커"),
        ("이전 트레이너", "현재 트레이너"),
        ("원래 트레이너", "어버이"),
        ("TextVar", "대상 변수"), ("Feeling", "감정"),
        ("TID16", "TID"), ("SID16", "SID"),
        ("이상한소포", "이상한 소포"), ("클래식리본", "클래식 리본"), ("세이브게임", "세이브"),
    ];
    public static string Fix(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var (a, b) in Map) text = text.Replace(a, b);
        // 형용사로 끝나는 직역: "유효한." / 줄 머리 "유효한:" 
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(^|\n)유효한\.", "$1유효함.");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(^|\n)유효한:", "$1유효:");
        return text;
    }
}
