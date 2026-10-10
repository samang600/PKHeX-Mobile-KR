using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// 스위치판 파이어레드·리프그린 기준 합법성 (설정 토글).
/// 스위치판은 GBA판과 달리 루비·사파이어·에메랄드·콜로세움/XD와 교환할 수 없고 옛 배포도 없음 → FRLG 안에서만 얻을 수 있는 개체만 합법.
/// · 출신 게임이 FR·LG가 아님 → 불법
/// · 배포(이상한 소포·이벤트) → 불법
/// · 이벤트 티켓 전용 장소(배꼽바위 루기아·칠색조, 탄생의섬 테오키스) → 불법
/// · 야생·선물·교환·알로 얻을 수 있는 포켓몬과 그 진화체(FRLG에서 가능한 진화 방법)만 → 그 외 종은 불법
/// </summary>
public static class SwitchFrlg
{
    public static bool Enabled { get => Preferences.Get("sw_frlg", false); set { Preferences.Set("sw_frlg", value); AppState.ClearLegalCache(); } }

    private static HashSet<ushort> obtain, eggOk;
    private static readonly object lk = new();

    /// <summary>적용 대상: 토글이 켜져 있고, FRLG 세이브의 3세대 개체.</summary>
    public static bool Applies(PKM p) => Enabled && AppState.Sav is SAV3FRLG && p is PK3 && p.Species != 0;

    /// <summary>DBI 백업 zip의 정보 파일로 스위치판 FRLG 세이브인지 확인 (TitleName에 FireRed/LeafGreen).</summary>
    public static bool IsSwitchZip(byte[] zip)
    {
        try
        {
            using var za = new System.IO.Compression.ZipArchive(new MemoryStream(zip), System.IO.Compression.ZipArchiveMode.Read);
            var e = za.Entries.FirstOrDefault(x => x.Name.Equals(".dbi_save_info.ini", StringComparison.OrdinalIgnoreCase));
            if (e == null) return false;
            using var r = new StreamReader(e.Open());
            var t = r.ReadToEnd();
            return t.Contains("FireRed", StringComparison.OrdinalIgnoreCase) || t.Contains("LeafGreen", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>불법 사유 (문제가 없으면 null).</summary>
    public static string Problem(PKM p, LegalityAnalysis la)
    {
        if (!Applies(p)) return null;
        try
        {
            if (p.Version is not (GameVersion.FR or GameVersion.LG)) return $"스위치판 FRLG: {AppState.GameName(p.Version)} 출신은 스위치판 FRLG로 데려올 수 없습니다";
            var enc = la.EncounterMatch;
            var tn = enc.GetType().Name;
            if (enc is MysteryGift || tn.StartsWith("EncounterGift3")) return "스위치판 FRLG: 옛 배포 포켓몬은 받을 수 없습니다";
            if (enc is EncounterStatic3 { FatefulEncounter: true }) return "스위치판 FRLG: 이벤트 티켓 전용 장소(배꼽바위·탄생의섬)의 포켓몬입니다";
            Build();
            if (enc is EncounterEgg3 egg && !eggOk.Contains(egg.Species)) return "스위치판 FRLG: 부모가 될 포켓몬을 FRLG에서 얻을 수 없어 이 알을 받을 수 없습니다";
            if (!obtain.Contains(p.Species)) return "스위치판 FRLG: FRLG에서 얻거나 진화시킬 수 없는 포켓몬입니다 (호연 지방 포켓몬, 시간 진화 에브이·블래키 등)";
        }
        catch { }
        return null;
    }

    /// <summary>FRLG 조우(야생·고정·선물·교환) 종 + FRLG에서 가능한 진화 → 얻을 수 있는 종 목록.</summary>
    private static void Build()
    {
        if (obtain != null) return;
        lock (lk)
        {
            if (obtain != null) return;
            var start = new HashSet<ushort>();
            var t = typeof(EncounterStatic3).Assembly.GetType("PKHeX.Core.Encounters3FRLG");
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (f.GetValue(null) is not Array arr) continue;
                foreach (var o in arr)
                {
                    if (o is EncounterArea3 area) { foreach (var s in area.Slots) start.Add(s.Species); continue; }
                    if (o is EncounterStatic3 st) { if (!st.FatefulEncounter) start.Add(st.Species); continue; }
                    if (o is IEncounterTemplate e && o.GetType().Name.StartsWith("EncounterTrade")) start.Add(e.Species);
                }
            }
            var fwd = EvolutionTree.GetEvolutionTree(EntityContext.Gen3).Forward;
            HashSet<ushort> Close(IEnumerable<ushort> seed)
            {
                var set = new HashSet<ushort>(seed); var q = new Queue<ushort>(set);
                while (q.Count > 0)
                {
                    var sp = q.Dequeue();
                    foreach (var m in fwd.GetForward(sp, 0).Span)
                    {
                        if (m.Species == 0 || m.Species > 386) continue;
                        if (m.Method is EvolutionType.LevelUpFriendshipMorning or EvolutionType.LevelUpFriendshipNight) continue;   // FRLG에는 시계가 없음 (에브이·블래키 불가)
                        if (m.Method.ToString().Contains("Beauty")) continue;
                        if (set.Add(m.Species)) q.Enqueue(m.Species);
                    }
                }
                return set;
            }
            var wild = Close(start);
            // 알: 그 알에서 나오는 종의 진화 계열에 FRLG에서 얻을 수 있는 종이 있으면 (예: 피츄 ← 피카츄)
            var eggs = new HashSet<ushort>();
            for (ushort sp = 1; sp <= 386; sp++) if (Close([sp]).Overlaps(wild)) eggs.Add(sp);
            eggOk = eggs;
            obtain = Close(wild.Concat(eggs));
        }
    }
}
