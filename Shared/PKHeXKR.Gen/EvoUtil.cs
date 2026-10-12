using PKHeX.Core;
namespace PKHeXKR;

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
        if (e is IScaledSizeValue sz) { sz.ResetHeight(); sz.ResetWeight(); }   // PLA 등: 실제 키·몸무게는 종족값 기준이라 진화체로 다시 계산
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
                if (GenHost.IsLegal(e)) return e;
            }
        }
        }
        if (e is IFormArgument fb) fb.FormArgument = fa0;
        e.CurrentLevel = baseLv; e.ResetPartyStats(); e.RefreshChecksum();
        return e;
    }
}
