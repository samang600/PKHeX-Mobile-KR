using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// 합법성 검사 결과를 초보자도 알 수 있게: 자주 나오는 사유마다 '왜 그런지'와 '어떻게 고치는지' 한 줄, 그리고 바로 고치는 버튼.
/// 고치기는 대부분 PKHeX 일괄 편집기의 추천값($suggest)을 그 항목에만 적용하는 것.
/// </summary>
public static class LegalHelp
{
    public sealed record Tip(string Why, string How, string FixLabel = null, Func<PKM, bool> Fix = null);

    /// <summary>항목별 고치기: 일괄 편집 명령 한 줄 적용 (바뀌었는지 반환).</summary>
    private static Func<PKM, bool> Batch(params string[] lines) => p =>
    {
        try
        {
            var before = new byte[p.SIZE_STORED]; p.WriteDecryptedDataStored(before);
            EntityBatchProcessor.Execute(lines, [p]);
            p.RefreshChecksum();
            var after = new byte[p.SIZE_STORED]; p.WriteDecryptedDataStored(after);
            return !before.AsSpan().SequenceEqual(after);
        }
        catch { return false; }
    };
    private static readonly Func<PKM, bool> Trash = p => { GenHost.ClearNickTrash(p); GenHost.ClearOTTrash(p); try { p.HandlingTrainerTrash.Clear(); var h = p.HandlingTrainerName; p.HandlingTrainerName = h; } catch { } p.RefreshChecksum(); return true; };

    private static readonly Dictionary<LegalityCheckResultCode, Tip> Tips = new()
    {
        // 조우·출신
        [LegalityCheckResultCode.EncInvalid] = new("이 종·레벨·게임 조합으로는 게임에서 얻을 수 있는 방법(조우)이 없습니다.", "만난 장소·레벨·출신 게임을 실제 입수 방법에 맞추거나 '자동 합법화'로 다시 만드세요."),
        [LegalityCheckResultCode.EncUnreleased] = new("아직 배포되지 않았거나 게임에 풀리지 않은 포켓몬·폼입니다.", "다른 종·폼으로 바꾸거나 배포 데이터 업데이트를 확인하세요."),
        [LegalityCheckResultCode.EncGiftNotFound] = new("배포 개체로 표시돼 있지만 맞는 배포 카드를 찾지 못했습니다.", "운명적인 만남 표시를 끄거나(배포가 아니면) 설정 → 배포 데이터 업데이트."),
        [LegalityCheckResultCode.EncGiftShinyMismatch] = new("이 배포 카드는 이로치 여부가 정해져 있습니다.", "카드대로 이로치를 켜거나 끄세요."),
        [LegalityCheckResultCode.EncGiftNicknamed] = new("이 배포 개체는 별명을 바꿀 수 없습니다.", "별명을 원래대로.", "별명 원래대로", Batch(".IsNicknamed=false")),
        [LegalityCheckResultCode.EncTradeChangedNickname] = new("게임 내 교환으로 받은 포켓몬은 별명이 정해져 있습니다.", "별명을 원래대로.", "별명 원래대로", Batch(".IsNicknamed=false")),
        [LegalityCheckResultCode.EncTradeChangedOT] = new("게임 내 교환으로 받은 포켓몬은 어버이가 정해져 있습니다.", "어버이를 원래 NPC 이름·ID로 (자동 합법화가 맞춰 줌)."),
        [LegalityCheckResultCode.EncStaticPIDShiny] = new("이 고정 조우는 이로치가 나올 수 없습니다(이로치 잠금).", "이로치를 끄거나 다른 조우로 만드세요."),
        [LegalityCheckResultCode.EncConditionBadSpecies] = new("이 조우에서는 이 종이 나오지 않습니다.", "종을 바꾸거나 자동 합법화."),
        [LegalityCheckResultCode.EvoInvalid] = new("이 진화 단계·레벨 조합은 불가능합니다 (예: 진화 레벨보다 낮음).", "레벨을 올리거나 진화 전 종으로.", "레벨을 추천값으로", Batch(".CurrentLevel=$suggest")),
        [LegalityCheckResultCode.EvoTradeRequired] = new("통신 교환으로만 진화하는 종인데 교환 기록이 없습니다.", "현재 트레이너(HT) 정보를 넣거나 자동 합법화."),
        // 만난 정보
        [LegalityCheckResultCode.LevelMetBelow] = new("지금 레벨이 만난 레벨보다 낮습니다.", "레벨을 만난 레벨 이상으로.", "레벨을 추천값으로", Batch(".CurrentLevel=$suggest")),
        [LegalityCheckResultCode.LevelMetSane] = new("만난 레벨이 이 조우에서 나올 수 없는 값입니다.", "만난 장소·레벨을 추천값으로.", "만난 정보 추천값", Batch(".MetLocation=$suggest")),
        [LegalityCheckResultCode.LevelEXPTooHigh] = new("경험치가 레벨 100보다 많습니다.", "레벨을 100으로.", "레벨 100으로", Batch(".CurrentLevel=100")),
        [LegalityCheckResultCode.LevelEXPThreshold] = new("경험치가 지금 레벨 구간과 맞지 않습니다.", "레벨을 다시 설정하면 경험치가 맞춰집니다.", "경험치 맞추기", p => { p.CurrentLevel = p.CurrentLevel; p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.TransferMetLocation] = new("만난 장소가 이 게임·출신 조합에 없습니다.", "만난 정보를 추천값으로.", "만난 정보 추천값", Batch(".MetLocation=$suggest")),
        [LegalityCheckResultCode.EggLocationInvalid] = new("알 받은 장소가 잘못되었습니다.", "알이 아니면 알 받은 장소를 0으로, 알이면 추천값으로.", "만난 정보 추천값", Batch(".MetLocation=$suggest")),
        [LegalityCheckResultCode.EggLocationNone] = new("알에서 태어난 것으로 돼 있는데 알 받은 장소가 없습니다.", "알 받은 장소를 넣거나 알 출신 표시를 끄세요.", "만난 정보 추천값", Batch(".MetLocation=$suggest")),
        [LegalityCheckResultCode.DateOutsideDistributionWindow] = new("만난 날짜가 이 배포의 배포 기간 밖입니다.", "만난 날짜를 배포 기간 안으로 (빠른 생성은 자동으로 맞춤).", "배포 기간 날짜로", p => { GenHost.FixEventDate(p); p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.DateCalendarInvalidMet] = new("만난 날짜가 달력에 없는 날입니다.", "만난 날짜를 다시 고르세요.", "오늘로", p => { p.MetDate = DateOnly.FromDateTime(DateTime.Today); p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.MetDetailTimeOfDay] = new("만난 시간대가 이 조우와 맞지 않습니다 (2세대).", "만난 시간대를 바꾸세요."),
        // 별명·어버이·쓰레기 바이트
        [LegalityCheckResultCode.NickMatchLanguage] = new("별명을 쓰지 않은 포켓몬인데 이름이 그 언어의 종 이름과 다릅니다.", "별명을 원래대로.", "별명 원래대로", Batch(".IsNicknamed=false")),
        [LegalityCheckResultCode.NickMatchLanguageFail] = new("별명을 쓰지 않은 포켓몬인데 이름이 그 언어의 종 이름과 다릅니다.", "별명을 원래대로.", "별명 원래대로", Batch(".IsNicknamed=false")),
        [LegalityCheckResultCode.NickMatchLanguageFlag] = new("이름이 종 이름과 같은데 '별명 있음'으로 표시돼 있습니다.", "별명 표시를 끄세요.", "별명 표시 끄기", Batch(".IsNicknamed=false")),
        [LegalityCheckResultCode.NickLengthLong] = new("별명이 이 게임의 글자 수 제한을 넘습니다.", "별명을 줄이세요.", "별명 원래대로", Batch(".IsNicknamed=false")),
        [LegalityCheckResultCode.NickInvalidChar] = new("별명에 이 게임에서 쓸 수 없는 글자가 있습니다.", "별명을 바꾸세요.", "별명 원래대로", Batch(".IsNicknamed=false")),
        [LegalityCheckResultCode.NickFlagEggYes] = new("알인데 이름이 '알'이 아닙니다.", "알 이름으로.", "별명 원래대로", Batch(".IsNicknamed=false")),
        [LegalityCheckResultCode.OTLong] = new("어버이 이름이 글자 수 제한을 넘습니다.", "어버이 이름을 줄이세요."),
        [LegalityCheckResultCode.OTShort] = new("어버이 이름이 비어 있습니다.", "어버이 이름을 넣으세요."),
        [LegalityCheckResultCode.OT_IDs0] = new("어버이 ID와 비밀 ID가 모두 0입니다.", "트레이너 설정의 ID를 넣으세요 (어버이 탭 '세이브 트레이너로')."),
        [LegalityCheckResultCode.TrashBytesExpected] = new("이름 뒤에 남아 있어야 할 흔적(쓰레기 바이트)이 없습니다 — 게임에서 이름을 바꾸면 이전 글자가 남습니다.", "자동 합법화가 흔적을 만들어 줍니다."),
        [LegalityCheckResultCode.TrashBytesShouldBeEmpty] = new("이름 뒤에 있어서는 안 되는 흔적(쓰레기 바이트)이 있습니다.", "이름을 다시 쓰면 지워집니다.", "쓰레기 바이트 지우기", Trash),
        [LegalityCheckResultCode.TrashBytesMismatchInitial] = new("이름 뒤 흔적(쓰레기 바이트)이 예상과 다릅니다.", "이름을 다시 쓰면 지워집니다.", "쓰레기 바이트 지우기", Trash),
        [LegalityCheckResultCode.TrashBytesMissingTerminatorFinal] = new("이름 끝 표시가 빠졌습니다.", "이름을 다시 쓰세요.", "쓰레기 바이트 지우기", Trash),
        // 기술
        [LegalityCheckResultCode.MovesShouldMatchRelearnMoves] = new("알 기술(떠올리기 기술)과 지금 기술이 맞지 않습니다.", "떠올리기 기술을 추천값으로.", "떠올리기 기술 추천값", Batch(".RelearnMoves=$suggest")),
        [LegalityCheckResultCode.EggMoveCombination] = new("이 알 기술 조합은 유전으로 나올 수 없습니다.", "기술을 추천값으로.", "기술 추천값", Batch(".Moves=$suggest", ".RelearnMoves=$suggest")),
        [LegalityCheckResultCode.MoveTechRecordFlagMissing_0] = new("기술레코드·기술머신으로 배운 기술인데 배운 기록이 없습니다.", "기술 기록을 켜세요.", "기술 기록 켜기", p => { try { if (p is ITechRecord tr) { tr.SetRecordFlags(p, TechnicalRecordApplicatorOption.LegalCurrent); p.RefreshChecksum(); return true; } return false; } catch { return false; } }),
        [LegalityCheckResultCode.MovePPTooHigh_01] = new("기술 PP가 최대치보다 많습니다.", "PP를 회복하세요.", "PP 회복", Batch(".HealPP=$suggest")),
        [LegalityCheckResultCode.MovePPUpsTooHigh_01] = new("포인트업 횟수가 3을 넘습니다.", "포인트업을 3 이하로.", "PP 회복", Batch(".HealPP=$suggest")),
        [LegalityCheckResultCode.MoveShopAlphaMoveShouldBeMastered_0] = new("우두머리 기술은 마스터 상태여야 합니다 (PLA).", "기술 숙련을 추천값으로.", "기술 숙련 추천값", Batch(".MoveMastery=$suggest")),
        [LegalityCheckResultCode.MoveShopMasterNotLearned_0] = new("배우지 않은 기술이 마스터로 표시돼 있습니다 (PLA).", "기술 숙련을 추천값으로.", "기술 숙련 추천값", Batch(".MoveMastery=$suggest")),
        [LegalityCheckResultCode.MoveShopMasterInvalid_0] = new("기술 숙련 기록이 잘못되었습니다 (PLA).", "기술 숙련을 추천값으로.", "기술 숙련 추천값", Batch(".MoveMastery=$suggest")),
        // 특성·성격·성별·PID
        [LegalityCheckResultCode.AbilityMismatch] = new("이 조우에서는 이 특성이 나올 수 없습니다 (숨겨진 특성 등).", "특성을 일반 특성으로 바꾸거나 다른 조우로.", "특성 1로", Batch(".AbilityNumber=$0")),
        [LegalityCheckResultCode.AbilityHiddenFail] = new("이 조우에서는 숨겨진 특성이 나오지 않습니다.", "특성을 일반 특성으로.", "특성 1로", Batch(".AbilityNumber=$0")),
        [LegalityCheckResultCode.AbilityHiddenUnavailable] = new("이 종은 숨겨진 특성을 얻을 수 없습니다.", "특성을 일반 특성으로.", "특성 1로", Batch(".AbilityNumber=$0")),
        [LegalityCheckResultCode.AbilityMismatchPID] = new("3~5세대는 특성이 성격값(PID)으로 정해집니다 — 지금 특성과 맞지 않습니다.", "특성을 PID에 맞추거나 PID를 새로.", "PID 새로 (성별·성격 유지)", Batch(".PID=$rand")),
        [LegalityCheckResultCode.AbilityUnexpected] = new("이 종이 가질 수 없는 특성입니다.", "특성을 다시 고르세요.", "특성 1로", Batch(".AbilityNumber=$0")),
        [LegalityCheckResultCode.PIDGenderMismatch] = new("3~5세대는 성별이 성격값(PID)으로 정해집니다 — 지금 성별과 맞지 않습니다.", "PID를 성별에 맞게 새로.", "PID 새로 (성별·성격 유지)", Batch(".PID=$rand")),
        [LegalityCheckResultCode.PIDNatureMismatch] = new("3~4세대는 성격이 성격값(PID)으로 정해집니다 — 지금 성격과 맞지 않습니다.", "PID를 성격에 맞게 새로.", "PID 새로 (성별·성격 유지)", Batch(".PID=$rand")),
        [LegalityCheckResultCode.PIDTypeMismatch] = new("이 조우 방식에서 나올 수 없는 성격값(PID)입니다 (시드 연동).", "인카운터 검색·시드로 다시 만들거나 자동 합법화."),
        [LegalityCheckResultCode.PIDEqualsEC] = new("성격값과 암호화 상수가 같습니다 (옮겨 온 개체 규칙 위반).", "암호화 상수를 새로.", "암호화 상수 새로", Batch(".EncryptionConstant=$rand")),
        [LegalityCheckResultCode.PIDEncryptZero] = new("암호화 상수가 0입니다.", "암호화 상수를 새로.", "암호화 상수 새로", Batch(".EncryptionConstant=$rand")),
        [LegalityCheckResultCode.PIDEncryptWurmple] = new("개무소 계열은 진화형이 암호화 상수로 정해집니다 — 맞지 않습니다.", "암호화 상수를 새로.", "암호화 상수 새로", Batch(".EncryptionConstant=$rand")),
        [LegalityCheckResultCode.GenderInvalidNone] = new("성별이 없는 종인데 성별이 있습니다 (또는 반대).", "성별을 맞추세요.", "성별 맞추기", p => { p.SetGender(p.GetSaneGender()); p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.StatAlignmentInvalid] = new("능력 성격(민트)이 이 게임에서 불가능한 값입니다.", "민트를 성격과 같게.", "민트 = 성격", p => { p.StatAlignment = p.Nature; p.RefreshChecksum(); return true; }),
        // 개체값·노력치·하이퍼트레이닝
        [LegalityCheckResultCode.IVFlawlessCountGEQ_0] = new("이 조우는 V(31) 개체값이 일정 개수 이상 보장됩니다 — 지금은 모자랍니다.", "개체값을 V로 채우세요.", "개체값 추천값", Batch(".IVs=$suggest")),
        [LegalityCheckResultCode.IVNotCorrect] = new("이 조우에서 정해진 개체값과 다릅니다.", "개체값을 추천값으로.", "개체값 추천값", Batch(".IVs=$suggest")),
        [LegalityCheckResultCode.IVAllEqual_0] = new("개체값이 모두 같은 값일 수 없는 조우입니다.", "개체값을 다시."),
        [LegalityCheckResultCode.EffortAbove252] = new("노력치 하나가 252를 넘습니다.", "252 이하로.", "노력치 추천값", Batch(".EVs=$suggest")),
        [LegalityCheckResultCode.EffortAbove510] = new("노력치 합이 510을 넘습니다.", "합을 510 이하로.", "노력치 추천값", Batch(".EVs=$suggest")),
        [LegalityCheckResultCode.EffortEgg] = new("알은 노력치가 0이어야 합니다.", "노력치를 0으로.", "노력치 0", Batch(".EVs=0")),
        [LegalityCheckResultCode.EffortShouldBeZero] = new("이 상태(레벨 1 배포 등)에서는 노력치가 0이어야 합니다.", "노력치를 0으로.", "노력치 0", Batch(".EVs=0")),
        [LegalityCheckResultCode.HyperPerfectAll] = new("개체값이 이미 31인 능력에는 하이퍼트레이닝을 할 수 없습니다.", "하이퍼트레이닝 표시를 끄세요.", "하이퍼트레이닝 추천값", Batch(".HyperTrainFlags=$suggest")),
        [LegalityCheckResultCode.HyperPerfectOne] = new("개체값이 이미 31인 능력에 하이퍼트레이닝이 켜져 있습니다.", "그 능력의 표시를 끄세요.", "하이퍼트레이닝 추천값", Batch(".HyperTrainFlags=$suggest")),
        [LegalityCheckResultCode.HyperPerfectUnavailable] = new("이 레벨·게임에서는 하이퍼트레이닝을 할 수 없습니다.", "표시를 끄거나 레벨을 올리세요.", "하이퍼트레이닝 추천값", Batch(".HyperTrainFlags=$suggest")),
        [LegalityCheckResultCode.HyperTrainLevelGEQ_0] = new("하이퍼트레이닝은 일정 레벨 이상에서만 가능합니다.", "레벨을 올리거나 표시를 끄세요.", "하이퍼트레이닝 추천값", Batch(".HyperTrainFlags=$suggest")),
        // 볼
        [LegalityCheckResultCode.BallEncMismatch] = new("이 조우(배포·고정)에서는 볼이 정해져 있습니다.", "정해진 볼로 바꾸세요 (볼 목록에서 초록 표시).", "합법 볼로", Batch(".Ball=$suggest")),
        [LegalityCheckResultCode.BallSpecies] = new("이 종은 이 볼에 들어갈 수 없습니다 (유전 불가 등).", "볼을 바꾸세요.", "합법 볼로", Batch(".Ball=$suggest")),
        [LegalityCheckResultCode.BallUnavailable] = new("이 볼은 이 게임·조우에서 쓸 수 없습니다.", "볼을 바꾸세요.", "합법 볼로", Batch(".Ball=$suggest")),
        [LegalityCheckResultCode.BallEggMaster] = new("마스터볼은 유전되지 않습니다.", "알 출신은 다른 볼로.", "합법 볼로", Batch(".Ball=$suggest")),
        [LegalityCheckResultCode.BallEggCherish] = new("프레셔스볼은 유전되지 않습니다.", "알 출신은 다른 볼로.", "합법 볼로", Batch(".Ball=$suggest")),
        [LegalityCheckResultCode.BallAbility] = new("이 볼과 숨겨진 특성 조합은 나올 수 없습니다.", "볼 또는 특성을 바꾸세요.", "합법 볼로", Batch(".Ball=$suggest")),
        // 리본·증표
        [LegalityCheckResultCode.RibbonsInvalid_0] = new("이 포켓몬이 받을 수 없는 리본이 있습니다 (세대·게임 불일치).", "그 리본을 끄세요.", "불가능한 리본 끄기", p => { try { RibbonApplicator.RemoveAllValidRibbons(p); return true; } catch { return false; } }),
        [LegalityCheckResultCode.RibbonsMissing_0] = new("반드시 있어야 할 리본이 빠졌습니다 (배포·챔피언 등).", "리본을 추천값으로.", "리본 추천값", Batch(".Ribbons=$suggest")),
        [LegalityCheckResultCode.RibbonMarkingInvalid_0] = new("이 조우에서 붙을 수 없는 증표입니다 (날씨·시간·낚시·카레 등).", "증표를 끄세요.", "리본·증표 추천값", Batch(".Ribbons=$suggest")),
        [LegalityCheckResultCode.RibbonMarkingAffixed_0] = new("달고 있는 리본·증표가 실제로는 없는 것입니다.", "달기 표시를 해제하세요.", "달기 해제", p => { if (p is IRibbonSetAffixed a) { a.AffixedRibbon = -1; p.RefreshChecksum(); return true; } return false; }),
        [LegalityCheckResultCode.RibbonEgg] = new("알은 리본을 가질 수 없습니다.", "리본을 모두 끄세요.", "리본 모두 끄기", Batch(".Ribbons=$suggestNone")),
        // 전송·트래커·현재 트레이너
        [LegalityCheckResultCode.TransferTrackerMissing] = new("HOME을 거친 포켓몬은 추적 ID(트래커)가 있어야 합니다 — 앱에서 만든 개체에는 없습니다.", "실제로 HOME을 거치면 생깁니다. 설정에서 '홈 트래커 없음 검사'를 끄면 이 항목만 무시됩니다."),
        [LegalityCheckResultCode.TransferTrackerShouldBeZero] = new("HOME을 거치지 않은 포켓몬에 추적 ID가 있습니다.", "추적 ID를 0으로.", "추적 ID 0", p => { if (p is IHomeTrack h) { h.Tracker = 0; p.RefreshChecksum(); return true; } return false; }),
        [LegalityCheckResultCode.TransferCurrentHandlerInvalid] = new("현재 트레이너 표시(어버이/다른 트레이너)가 상황과 맞지 않습니다.", "현재 트레이너 표시를 바꾸세요.", "현재 트레이너 = 어버이", p => { p.CurrentHandler = 0; p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.TransferHandlerMismatchName] = new("현재 트레이너 이름이 세이브 트레이너와 다릅니다 (교환받은 포켓몬 검사).", "설정에서 '현재 트레이너를 세이브 주인과 대조'를 끄거나 이름을 맞추세요."),
        [LegalityCheckResultCode.TransferHandlerMismatchGender] = new("현재 트레이너 성별이 세이브 트레이너와 다릅니다.", "설정에서 대조를 끄거나 성별을 맞추세요."),
        [LegalityCheckResultCode.TransferHandlerFlagRequired] = new("다른 게임에서 온 포켓몬은 현재 트레이너가 '다른 트레이너'여야 합니다.", "현재 트레이너 표시를 바꾸세요.", "현재 트레이너 = 다른 트레이너", p => { p.CurrentHandler = 1; p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.TransferNotPossible] = new("이 포켓몬은 이 게임으로 옮겨 올 수 없습니다.", "출신 게임을 바꾸거나 다른 종으로."),
        [LegalityCheckResultCode.TransferObedienceLevel] = new("복종 레벨이 만난 레벨·현재 레벨과 맞지 않습니다 (SV).", "복종 레벨을 맞추세요.", "복종 레벨 맞추기", p => { if (p is IObedienceLevel o) { o.ObedienceLevel = p.MetLevel; p.RefreshChecksum(); return true; } return false; }),
        [LegalityCheckResultCode.FatefulMissing] = new("배포 개체는 '운명적인 만남' 표시가 있어야 합니다.", "표시를 켜세요.", "운명적인 만남 켜기", p => { p.FatefulEncounter = true; p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.FatefulInvalid] = new("배포가 아닌데 '운명적인 만남' 표시가 있습니다.", "표시를 끄세요.", "운명적인 만남 끄기", p => { p.FatefulEncounter = false; p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.FatefulMysteryMissing] = new("이 배포 개체에는 '운명적인 만남' 표시가 필요합니다.", "표시를 켜세요.", "운명적인 만남 켜기", p => { p.FatefulEncounter = true; p.RefreshChecksum(); return true; }),
        // 추억·친밀도
        [LegalityCheckResultCode.MemoryMissingHT] = new("현재 트레이너와의 추억이 비어 있습니다.", "추억을 넣거나 현재 트레이너 정보를 지우세요.", "추억 지우기", p => { try { p.ClearMemories(); p.RefreshChecksum(); return true; } catch { return false; } }),
        [LegalityCheckResultCode.MemoryMissingOT] = new("어버이와의 추억이 비어 있습니다 (6세대).", "추억을 넣으세요 (자동 합법화)."),
        [LegalityCheckResultCode.MemoryHTLanguage] = new("현재 트레이너 언어가 비어 있거나 잘못되었습니다.", "현재 트레이너 언어를 넣으세요."),
        [LegalityCheckResultCode.MemoryStatFriendshipHT0] = new("현재 트레이너 친밀도가 0이면 안 됩니다.", "친밀도를 추천값으로.", "친밀도 추천값", Batch(".HandlingTrainerFriendship=$suggest")),
        // 크기·형태·기타
        [LegalityCheckResultCode.StatIncorrectHeight] = new("키가 이 조우·HOME 규칙과 맞지 않습니다.", "키를 배율과 같게 (HOME 규칙).", "키·몸무게 맞추기", p => AppState.SyncHomeScale(p)),
        [LegalityCheckResultCode.StatIncorrectWeight] = new("몸무게가 이 조우·HOME 규칙과 맞지 않습니다.", "몸무게를 맞추세요.", "키·몸무게 맞추기", p => AppState.SyncHomeScale(p)),
        [LegalityCheckResultCode.StatIncorrectScaleValue_0] = new("배율(크기)이 이 조우에서 정해진 값과 다릅니다 (Z-A 우두머리 255 등).", "배율을 정해진 값으로."),
        [LegalityCheckResultCode.StatGigantamaxInvalid] = new("이 종·조우는 거다이맥스가 불가능합니다.", "거다이맥스를 끄세요.", "거다이맥스 끄기", p => { if (p is IGigantamax g) { g.CanGigantamax = false; p.RefreshChecksum(); return true; } return false; }),
        [LegalityCheckResultCode.StatDynamaxInvalid] = new("다이맥스 레벨이 이 상황에서 불가능합니다.", "다이맥스 레벨을 0으로.", "다이맥스 레벨 0", p => { if (p is IDynamaxLevel d) { d.DynamaxLevel = 0; p.RefreshChecksum(); return true; } return false; }),
        [LegalityCheckResultCode.StatAlphaInvalid] = new("우두머리 표시가 이 조우와 맞지 않습니다.", "우두머리 표시를 끄거나 우두머리 조우로.", "우두머리 끄기", p => { if (p is IAlpha a) { a.IsAlpha = false; p.RefreshChecksum(); return true; } return false; }),
        [LegalityCheckResultCode.TeraTypeMismatch] = new("원래 테라 타입이 이 조우에서 나올 수 없습니다.", "원래 테라 타입을 종 타입으로.", "테라 타입 추천값", Batch(".TeraTypeOriginal=$suggest")),
        [LegalityCheckResultCode.TeraTypeIncorrect] = new("변경된 테라 타입이 잘못되었습니다.", "변경 테라 타입을 '없음'으로.", "변경 테라 타입 없음", p => { if (p is ITeraType t) { t.TeraTypeOverride = (MoveType)TeraTypeUtil.OverrideNone; p.RefreshChecksum(); return true; } return false; }),
        [LegalityCheckResultCode.FormInvalidGame] = new("이 폼은 이 게임에 없습니다.", "폼을 바꾸세요.", "기본 폼으로", p => { p.Form = 0; p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.FormBattle] = new("배틀 중에만 되는 폼입니다 (메가진화 등).", "기본 폼으로.", "기본 폼으로", p => { p.Form = 0; p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.FormItemInvalid] = new("지닌 물건과 폼이 맞지 않습니다 (아르세우스·실버디 등).", "폼에 맞는 물건을 지니게 하세요."),
        [LegalityCheckResultCode.FormArgumentInvalid] = new("폼 인수(진화 조건 횟수 등)가 범위를 벗어났습니다.", "폼 인수를 0 또는 최대값으로.", "폼 인수 0", p => { if (p is IFormArgument f) { f.FormArgument = 0; p.RefreshChecksum(); return true; } return false; }),
        [LegalityCheckResultCode.ContestZero] = new("콘테스트 능력치가 있어야 하는 조우입니다.", "콘테스트 능력치를 추천값으로.", "콘테스트 추천값", Batch(".ContestStats=$suggest")),
        [LegalityCheckResultCode.MarkValueShouldBeZero] = new("마킹 값이 이 게임에서 쓰지 않는 비트를 포함합니다.", "마킹을 지우세요.", "마킹 지우기", p => { try { if (p is IAppliedMarkings7 m7) m7.MarkingValue = 0; else if (p is IAppliedMarkings<bool> mb) for (int i = 0; i < mb.MarkingCount; i++) mb.SetMarking(i, false); } catch { } p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.ItemUnreleased] = new("아직 풀리지 않은 도구를 지니고 있습니다.", "도구를 빼세요.", "도구 빼기", p => { p.HeldItem = 0; p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.ItemEgg] = new("알은 도구를 지닐 수 없습니다.", "도구를 빼세요.", "도구 빼기", p => { p.HeldItem = 0; p.RefreshChecksum(); return true; }),
        [LegalityCheckResultCode.GeoNoCountryHT] = new("현재 트레이너의 나라 정보가 없습니다 (6·7세대).", "지역 정보를 넣거나 지우세요.", "지역 정보 지우기", p => { if (p is IGeoTrack g) { g.ClearGeoLocationData(); p.RefreshChecksum(); return true; } return false; }),
        [LegalityCheckResultCode.GeoMemoryMissing] = new("지역 정보가 비어 있습니다 (6·7세대).", "나라·지역을 넣으세요."),
        [LegalityCheckResultCode.OTLanguageShouldBe_0] = new("어버이 언어가 이 출신·배포에 정해진 언어와 다릅니다.", "언어를 바꾸세요."),
        [LegalityCheckResultCode.OTLanguageCannotPlayOnVersion_0] = new("이 언어로는 이 게임을 할 수 없습니다 (한국어 미발매 게임 등).", "언어를 바꾸세요."),
        [LegalityCheckResultCode.AwakenedCap] = new("각성 능력치가 최대치를 넘습니다 (레츠고).", "각성 능력치를 낮추세요."),
    };

    /// <summary>기술 판정 (la.Info.Moves / Relearn) 에서 불법인 칸마다 설명.</summary>
    public static List<Tip> ForMoves(LegalityAnalysis la, PKM pk)
    {
        var list = new List<Tip>();
        try
        {
            var names = GameInfo.Strings.movelist;
            var bad = new List<string>();
            for (int i = 0; i < la.Info.Moves.Length; i++)
            {
                if (la.Info.Moves[i].Valid) continue;
                var m = pk.GetMove(i);
                bad.Add($"{i + 1}번 {(m > 0 && m < names.Length ? names[m] : "(빈 칸)")}");
            }
            if (bad.Count > 0)
                list.Add(new($"배울 수 없는 기술: {string.Join(", ", bad)}", "다른 기술로 바꾸거나 추천값으로 맞추세요.", "기술 추천값", Batch(".Moves=$suggest", ".RelearnMoves=$suggest")));
            if (!MoveResult.AllValid(la.Info.Relearn))
                list.Add(new("떠올리기 기술(알 기술 등)이 맞지 않습니다.", "떠올리기 기술을 추천값으로.", "떠올리기 기술 추천값", Batch(".RelearnMoves=$suggest")));
        }
        catch { }
        return list;
    }
    public static Tip For(CheckResult r) => Tips.TryGetValue(r.Result, out var t) ? t : null;

    /// <summary>항목(CheckIdentifier)만으로 대략의 안내 (코드별 설명이 없을 때).</summary>
    public static string ForIdentifier(CheckIdentifier id) => id switch
    {
        CheckIdentifier.Encounter => "입수 방법(조우)과 관련된 문제입니다. 만난 장소·레벨·출신 게임·볼이 실제 입수 방법과 맞아야 합니다.",
        CheckIdentifier.Nickname => "별명·이름 문제입니다.",
        CheckIdentifier.Trainer => "어버이·ID 문제입니다.",
        CheckIdentifier.Ball => "볼 문제입니다. 볼 목록에서 초록으로 표시된 볼만 가능합니다.",
        CheckIdentifier.Ability => "특성 문제입니다.",
        CheckIdentifier.Shiny => "이로치 관련 문제입니다.",
        CheckIdentifier.PID or CheckIdentifier.EC => "성격값·암호화 상수 문제입니다 (대개 PID를 새로 뽑으면 됩니다).",
        CheckIdentifier.Level => "레벨·경험치 문제입니다.",
        CheckIdentifier.Memory => "추억 문제입니다 (6세대 이후).",
        CheckIdentifier.Ribbon => "리본·증표 문제입니다.",
        CheckIdentifier.RelearnMove or CheckIdentifier.CurrentMove => "기술 문제입니다. '기술 추천값'으로 맞출 수 있습니다.",
        CheckIdentifier.Evolution => "진화 단계 문제입니다.",
        CheckIdentifier.Egg => "알 관련 문제입니다.",
        CheckIdentifier.Form => "폼 문제입니다.",
        CheckIdentifier.Gender => "성별 문제입니다.",
        CheckIdentifier.Nature => "성격 문제입니다.",
        CheckIdentifier.Handler or CheckIdentifier.TrashBytes => "현재 트레이너·교환 기록 문제입니다.",
        CheckIdentifier.Language => "언어 문제입니다.",
        CheckIdentifier.Training => "하이퍼트레이닝·노력치 문제입니다.",
        CheckIdentifier.Marking => "마킹 문제입니다.",
        CheckIdentifier.Misc => "그 밖의 항목입니다.",
        CheckIdentifier.GameOrigin => "출신 게임 문제입니다.",
        CheckIdentifier.HeldItem => "지닌 물건 문제입니다.",
        CheckIdentifier.RibbonMark => "증표 문제입니다.",
        CheckIdentifier.Geography => "지역 정보 문제입니다 (6·7세대).",
        _ => "",
    };
}
