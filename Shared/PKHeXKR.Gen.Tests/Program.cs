using PKHeX.Core;
using PKHeXKR;

// 생성 엔진 문장 해석 시험 (표로 적은 예문 → 기대 결과). 코드 주석·변경 기록에 나온 사례 위주.
// 실행: dotnet run --project Shared/PKHeXKR.Gen.Tests  (실패가 있으면 종료 코드 1)

GameInfo.CurrentLanguage = "ko";
GameInfo.Strings = GameInfo.GetStrings("ko");
Lexicon.Language = "ko";
var sav = BlankSaveFile.Get(GameVersion.SL, "TEST", LanguageID.Korean);

int pass = 0, fail = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) pass++;
    else { fail++; Console.WriteLine($"실패: {name}{(detail.Length > 0 ? " — " + detail : "")}"); }
}
string Show(OrderSpec o) => $"종 {o.Species} 폼 {o.Form} 성별 {o.Gender} 이로치 {o.Shiny} 볼 {o.Ball} 성격 {o.Nature} 특성 {o.Ability} {o.FlawlessIVs}V Lv{o.Level} 어버이 {o.OT}/{o.TID} 날짜 {o.MetFrom}~{o.MetTo} 모름[{string.Join(",", o.Unknown)}]";

// ===== 빠른 생성 문장 (QuickParse) =====
var parseCases = new (string Text, Func<List<OrderSpec>, bool> Ok)[]
{
    // '다'로 시작하는 이름이 '모두(다)' 공통 줄로 잡히던 문제
    ("다투곰 이로치", l => l.Count == 1 && l[0].Species == (ushort)Species.Ursaluna && l[0].Shiny == 1),
    ("다크라이", l => l.Count == 1 && l[0].Species == (ushort)Species.Darkrai),
    ("다투곰 붉은달", l => l.Count == 1 && l[0].Species == (ushort)Species.Ursaluna && l[0].Form == 1),
    // 진짜 공통 줄: 띄어쓰기 뒤 내용은 모든 포켓몬에
    ("모두 암컷\n피카츄\n이브이", l => l.Count == 2 && l.All(o => o.Gender == 1) && l[0].Species == (ushort)Species.Pikachu && l[1].Species == (ushort)Species.Eevee),
    // 이름에 ':'가 든 포켓몬
    ("타입:널 이로치", l => l.Count == 1 && l[0].Species == (ushort)Species.TypeNull && l[0].Shiny == 1),
    ("타입 : 널", l => l.Count == 1 && l[0].Species == (ushort)Species.TypeNull),
    // 날짜는 범위(도감 번호)가 아님
    ("피카츄 2024-03-05", l => l.Count == 1 && l[0].MetFrom == new DateOnly(2024, 3, 5) && l[0].MetTo == new DateOnly(2024, 3, 5)),
    ("피카츄 2024-01-01~2024-06-30", l => l.Count == 1 && l[0].MetFrom == new DateOnly(2024, 1, 1) && l[0].MetTo == new DateOnly(2024, 6, 30)),
    ("2024년 피카츄", l => l.Count == 1 && l[0].Species == (ushort)Species.Pikachu && l[0].MetFrom == new DateOnly(2024, 1, 1) && l[0].MetTo == new DateOnly(2024, 12, 31)),
    ("2024-03-05 피카츄", l => l.Count == 1 && l[0].Species == (ushort)Species.Pikachu && l[0].MetFrom == new DateOnly(2024, 3, 5)),
    // 번호 범위는 그 게임에 나오는 종마다 한 줄
    ("1~3 이로치", l => l.Count == 3 && l.Select(o => (int)o.Species).SequenceEqual([1, 2, 3]) && l.All(o => o.Shiny == 1)),
    // 여러 조건 (스트레인지볼 주문)
    ("피카츄 스트레인지볼 고집 6V 숨특 Lv50", l => l.Count == 1 && l[0].Ball == (int)Ball.Strange && l[0].Nature == (int)Nature.Adamant && l[0].FlawlessIVs == 6 && l[0].Ability == 2 && l[0].Level == 50),
    // 공통 어버이 줄
    ("어버이: 새아 468686\n피카츄", l => l.Count == 1 && l[0].OT == "새아" && l[0].TID == 468686),
    ("피카츄 암컷 러브볼", l => l.Count == 1 && l[0].Gender == 1 && l[0].Ball == (int)Ball.Love),
};
foreach (var (text, ok) in parseCases)
{
    List<OrderSpec> l;
    try { l = QuickParse.Parse(text, sav); } catch (Exception ex) { Check(text.Replace("\n", " / "), false, ex.Message); continue; }
    Check(text.Replace("\n", " / "), ok(l), string.Join(" | ", l.Select(Show)));
}

// ===== 일괄 수정 문장 (BatchKo) =====
var batchCases = new (string Text, string[] Must, string[] MustNot)[]
{
    ("피카츄 전부 암컷으로 바꾸기", ["=Species=25", "@성별=1"], []),
    // 계열 = 진화 계통 전부 (랄토스·킬리아·가디안·엘레이드). '계열' 뒤 낱말은 바꿀 것으로 읽음 (지금 동작)
    ("가디안 계열 암컷 문볼로", ["=Species=280", "=Species=281", "=Species=282", "=Species=475", $".Ball={(int)Ball.Moon}"], []),
    ("암컷 가디안 계열 문볼로", ["=Gender=1", "=Species=282", $".Ball={(int)Ball.Moon}"], []),
    ("닉네임 붙은 포켓몬 닉네임 없애기", ["=IsNicknamed=true", ".IsNicknamed=false"], []),
    ("이로치 포켓몬 어버이 미월로", ["=IsShiny=true", "@어버이=미월"], []),
    // '로'로 끝나는 이름에서 '로'를 떼어 '히'가 되던 문제
    ("어버이 히로 바꾸기", ["@어버이=히로"], ["@어버이=히"]),
    ("어버이 히로로 바꾸기", ["@어버이=히로"], []),
    ("수컷 피카츄 암컷으로", ["=Gender=0", "@성별=1", "=Species=25"], []),
    ("다투곰 6v로", ["=Species=901", ".IV_HP=31", ".IV_SPE=31"], []),
    ("일반색 피카츄 이로치로", ["=IsShiny=false", ".PID=$shiny"], []),
    ("피카츄 네모로", [".PID=$shiny0"], []),
};
foreach (var (text, must, mustNot) in batchCases)
{
    BatchKo.Result r;
    try { r = BatchKo.Convert(text, sav); } catch (Exception ex) { Check(text, false, ex.Message); continue; }
    bool ok = must.All(r.Lines.Contains) && !mustNot.Any(r.Lines.Contains);
    Check("일괄: " + text, ok, string.Join(" ", r.Lines) + (r.Unknown.Count > 0 ? $" 모름[{string.Join(",", r.Unknown)}]" : ""));
}

// ===== 작은 도우미 =====
Check("Id32 7세대", GenTrainers.Id32(true, 123456, 1234) == 1234_123456u);
Check("Id32 6세대 이하", GenTrainers.Id32(false, 12345, 54321) == ((54321u << 16) | 12345u));
Check("Id32 자릿수 제한 (넘치지 않음)", GenTrainers.Id32(true, 9_999_999, 9999) == uint.MaxValue);

// 조건 빌려 쓰기: 만드는 동안 화면에서 바꾼 칸은 되돌리지 않음
{
    EncCond.Nature = -1; EncCond.Gender = -1; EncCond.IVMin = new int[6];
    var snap = QuickGen.SnapshotEncCond();
    EncCond.Nature = 3; EncCond.IVMin = [31, 31, 31, 31, 31, 31];   // 주문 조건
    var mine = QuickGen.SnapshotEncCond();
    EncCond.Gender = 1; EncCond.IVMin[5] = 20;                     // 그사이 화면에서 바꿈
    QuickGen.RestoreEncCond(snap, mine);
    Check("조건 되돌리기: 주문 값은 되돌림", EncCond.Nature == -1);
    Check("조건 되돌리기: 화면 값은 남김", EncCond.Gender == 1);
    Check("조건 되돌리기: 개체값은 칸마다", EncCond.IVMin.SequenceEqual([0, 0, 0, 0, 0, 20]), string.Join(",", EncCond.IVMin));
    EncCond.Gender = -1; EncCond.IVMin = new int[6];
}

// 카레의증표: 생성기가 만든 개체값(6V)을 다시 뽑지 않음
{
    var pk = new PK8 { Species = (ushort)Species.Pikachu, TID16 = 1, SID16 = 2 };
    pk.IV_HP = pk.IV_ATK = pk.IV_DEF = pk.IV_SPA = pk.IV_SPD = pk.IV_SPE = 31;
    EncCond.Mark = (int)RibbonIndex.MarkCurry; EncCond.IVExact = false; EncCond.IVMin = new int[6]; EncCond.ShinyMode = 0;
    EncCond.ApplyMark(pk);
    Check("카레의증표 6V 유지", QuickGen.FlawlessCount(pk) == 6 && pk.RibbonMarkCurry, $"{pk.IV_HP}/{pk.IV_ATK}/{pk.IV_DEF}/{pk.IV_SPA}/{pk.IV_SPD}/{pk.IV_SPE}");
    EncCond.IVMin = [0, 0, 0, 0, 0, 25]; pk.IV_SPE = 3;
    EncCond.ApplyMark(pk);
    Check("카레의증표 최솟값 반영", pk.IV_SPE >= 25 && pk.IV_HP == 31, $"S {pk.IV_SPE}");
    EncCond.Mark = -1; EncCond.IVMin = new int[6];
}

// ===== 실제 생성 (느림: GEN_SLOW=1일 때만) =====
if (Environment.GetEnvironmentVariable("GEN_SLOW") == "1")
{
    (PKM, string) Make(string text) { var o = QuickParse.Parse(text, sav)[0]; return QuickGen.Make(o, sav, CancellationToken.None); }
    var (p1, how1) = Make("피카츄 스트레인지볼 고집 6V Lv50");
    Check("스트레인지볼 + 성격·6V·레벨", p1 != null && p1.Ball == (int)Ball.Strange && p1.Nature == Nature.Adamant && QuickGen.FlawlessCount(p1) == 6 && p1.CurrentLevel == 50, how1 + (p1 == null ? "" : QuickGen.Stats(p1)));
    var (p2, how2) = Make("피카츄 스트레인지볼 숨특");   // GO 출신은 숨겨진 특성 불가 → 조건을 무시하고 만들지 않음
    Check("스트레인지볼 + 불가능한 조건은 실패", p2 == null || p2.AbilityNumber == 4, how2);
    Console.WriteLine("  " + how1 + " / " + how2);
}

Console.WriteLine($"통과 {pass} · 실패 {fail}");
return fail == 0 ? 0 : 1;
