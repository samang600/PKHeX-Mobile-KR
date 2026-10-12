using PKHeX.Core;
namespace PKHeXKR;

public static class LegalTools
{
    /// <summary>이 개체에 합법인 볼 (볼 검사만 확인).</summary>
    public static HashSet<int> LegalBalls(PKM pk)
    {
        var ok = new HashSet<int>();
        if (pk == null || pk.Species == 0) return ok;
        // 기준: 지금 개체의 불법 사유. 볼만 바꿔서 "새로" 생기는 불법 사유가 하나라도 있으면 그 볼은 불가
        // (볼 검사 외에 조우 대조 등 다른 항목으로 걸리는 경우까지 반영: 예) 스칼렛 야생에 기가톤볼·스트레인지볼·프레셔스볼)
        HashSet<(CheckIdentifier, LegalityCheckResultCode)> Bad(PKM x)
        {
            try { return new LegalityAnalysis(x).Results.Where(r => r.Judgement == Severity.Invalid).Select(r => (r.Identifier, r.Result)).ToHashSet(); }
            catch { return [(CheckIdentifier.Ball, LegalityCheckResultCode.Valid)]; }
        }
        var baseBad = Bad(pk);
        foreach (var c in AppState.Src.Balls)
        {
            if (c.Value <= 0) continue;
            try
            {
                var t = pk.Clone(); t.Ball = (byte)c.Value; t.RefreshChecksum();
                if (!Bad(t).Except(baseBad).Any() && !(baseBad.Any(b => b.Item1 == CheckIdentifier.Ball) && c.Value != pk.Ball && Bad(t).Any(b => b.Item1 == CheckIdentifier.Ball))) ok.Add(c.Value);
            }
            catch { }
        }
        return ok;
    }

    /// <summary>이 개체가 달 수 있는 리본 이름 ("가능한 리본 전부"를 복사본에 적용해 확인).</summary>
    public static HashSet<string> PossibleRibbons(PKM pk)
    {
        var set = new HashSet<string>();
        if (pk == null || pk.Species == 0) return set;
        try
        {
            var t = pk.Clone(); AppState.SetAllRibbons(t);
            foreach (var r in RibbonInfo.GetRibbonInfo(t)) if (r.HasRibbon || r.RibbonCount > 0) set.Add(r.Name);
        }
        catch { }
        return set;
    }
}
