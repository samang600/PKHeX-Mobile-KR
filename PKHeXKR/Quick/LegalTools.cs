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

public static class EvoUtil
{
    /// <summary>진화 전 조우로 만든 개체를 검색한 종으로 진화 (그 종의 진화 계통일 때만).</summary>
    public static PKM ToTarget(PKM p, ushort species, int form = -1)
    {
        if (p == null || species == 0 || p.Species == species) return p;
        try
        {
            var tree = EvolutionTree.GetEvolutionTree(p.Context);
            var t = p.Clone(); t.Species = species; if (form >= 0) t.Form = (byte)form;
            bool related = tree.Reverse.GetPreEvolutions(species, t.Form).Any(x => x.Species == p.Species);
            if (!related) return p;
        }
        catch { }
        return Evolve(p, species, form);
    }

    /// <summary>진화: 닉네임이 없으면 이름도 바꾸고, 합법이 되는 레벨까지 올려 봄. Z-A는 특성을 그대로 둠(시드와 연결).</summary>
    public static PKM Evolve(PKM p, ushort species, int form = -1)
    {
        var baseLv = p.CurrentLevel;
        var e = p.Clone();
        e.Species = species; if (form >= 0) e.Form = (byte)form;
        if (!e.IsNicknamed) e.ClearNickname();
        // 모으령→타부자고(코인 999개)처럼 진화에 폼 인수가 필요한 경우 최솟값으로
        if (e is IFormArgument fa) { try { var min = FormArgumentUtil.GetFormArgumentMinEvolution(species, p.Species); if (fa.FormArgument < min) fa.FormArgument = min; } catch { } }
        if (e.Context != EntityContext.Gen9a) { try { e.RefreshAbility(e.AbilityNumber >> 1); } catch { } }
        // Z-A 등: 진화 후 현재 기술의 기술플러스 기록을 맞춤
        var plusOpts = e is IPlusRecord && e.PersonalInfo is IPermitPlus ? new[] { PlusRecordApplicatorOption.LegalCurrent, PlusRecordApplicatorOption.LegalSeedTM, PlusRecordApplicatorOption.LegalCurrentTM } : [PlusRecordApplicatorOption.None];
        uint fa0 = e is IFormArgument f0 ? f0.FormArgument : 0;
        foreach (var plus in plusOpts)
        {
        if (e is IPlusRecord pr && e.PersonalInfo is IPermitPlus pp && plus != PlusRecordApplicatorOption.None)
        { try { pr.ClearPlusFlags(pp.PlusCountTotal); pr.SetPlusFlags(e, pp, plus); } catch { } }   // 기술플러스 조건을 진화체 기준으로 채움
        foreach (var useArg in new[] { true, false })   // 폼 인수가 필요한 게임(SV)과 없는 게임(Z-A) 모두 시도
        {
            if (e is IFormArgument fx) fx.FormArgument = useArg ? fa0 : 0;
            foreach (var lv in new[] { baseLv, 16, 20, 30, 36, 40, 50, 52, 60, 64, 100 }.Where(x => x >= baseLv).Distinct())
            {
                e.CurrentLevel = (byte)lv; e.ResetPartyStats(); e.RefreshChecksum();
                if (AppState.IsLegal(e)) return e;
            }
        }
        }
        if (e is IFormArgument fb) fb.FormArgument = fa0;
        e.CurrentLevel = baseLv; e.ResetPartyStats(); e.RefreshChecksum();
        return e;
    }
}
