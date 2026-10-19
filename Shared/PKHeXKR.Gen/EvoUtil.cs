using PKHeX.Core;
namespace PKHeXKR;

public static class EvoUtil
{
    /// <summary>진화 전 조우로 만든 개체를 검색한 종으로 진화 (그 종의 진화 계통일 때만).</summary>
    public static PKM ToTarget(PKM p, ushort species, int form = -1)
    {
        if (p == null || species == 0 || p.Species == species) return p;
        var f = TargetForm(p, species, form);
        if (f == Unrelated) return p;
        return Evolve(p, species, f);
    }

    private const int Unrelated = -2;

    /// <summary>p → species 진화 경로의 방법들 (여러 단계면 모두: 사과농 → 단지래플 → 과미드라)</summary>
    private static List<EvolutionMethod> Path(PKM p, ushort species)
    {
        var tree = EvolutionTree.GetEvolutionTree(p.Context);
        List<EvolutionMethod> Walk(ushort sp, byte f, int depth)
        {
            if (depth > 3) return null;
            ReadOnlyMemory<EvolutionMethod> next;
            try { next = tree.Forward.GetForward(sp, f); } catch { return null; }
            foreach (var m in next.ToArray())
            {
                var df = m.GetDestinationForm(f);
                if (m.Species == species) return [m];
                if (Walk(m.Species, df, depth + 1) is { } rest) { rest.Insert(0, m); return rest; }
            }
            return null;
        }
        try { return Walk(p.Species, p.Form, 0) ?? []; } catch { return []; }
    }

    /// <summary>
    /// 진화체의 폼: 진화 전 폼과 다를 수 있음 (가라르 파오리 폼1 → 창파나이트 폼0, 가라르 지그제구리 → 가로막구리,
    /// 히스이 포푸니 → 포푸니크, 팔데아 우파 → 토오 등). 진화 전 폼을 그대로 쓰면 없는 폼이라 진화하지 못했음.
    /// 진화 계통이 아니면 Unrelated(-2), 알 수 없으면 -1.
    /// </summary>
    public static int TargetForm(PKM p, ushort species, int form = -1)
    {
        try
        {
            var tree = EvolutionTree.GetEvolutionTree(p.Context);
            var forms = form >= 0 ? new[] { form } : new[] { (int)p.Form }.Concat(Enumerable.Range(0, 32)).Distinct();
            int found = Unrelated;
            foreach (var f in forms)
            {
                List<(ushort Species, byte Form)> pre;
                try { pre = tree.Reverse.GetPreEvolutions(species, (byte)f).Select(x => (x.Species, x.Form)).ToList(); } catch { continue; }
                if (pre.Any(x => x.Species == p.Species && x.Form == p.Form)) return f;
                if (found == Unrelated && pre.Any(x => x.Species == p.Species)) found = f;   // 폼까지 맞는 게 없으면 종만 맞는 것
            }
            return found;
        }
        catch { return form; }
    }

    /// <summary>진화: 닉네임이 없으면 이름도 바꾸고, 합법이 되는 레벨까지 올려 봄. Z-A는 특성을 그대로 둠(시드와 연결).</summary>
    public static PKM Evolve(PKM p, ushort species, int form = -1)
    {
        var baseLv = p.CurrentLevel;
        if (form < 0 && p.Species != species && TargetForm(p, species) is var tf and >= 0) form = tf;   // 진화체 폼 (가라르 파오리 → 창파나이트 폼0 등)
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
        // 진화 조건 채우기: 기술을 알고 레벨업 (과미드라 = 용기백배 등), 통신교환 (후딘·괴력몬 등 → 교환받은 트레이너 기록)
        var path = Path(p, species);
        if (path.Count > 0)
        {
            var c = e.Clone();
            bool changed = false;
            foreach (var m in path)
            {
            if (m.Method is EvolutionType.LevelUpKnowMove or EvolutionType.LevelUpKnowMoveECElse or EvolutionType.LevelUpKnowMoveEC100 && m.Argument != 0
                && !c.HasMove(m.Argument))
            {
                var mv = new[] { c.Move1, c.Move2, c.Move3, c.Move4 };
                int slot = Array.IndexOf(mv, (ushort)0); if (slot < 0) slot = 3;
                c.SetMove(slot, m.Argument); c.HealPP(); changed = true;
                // 기술머신으로 배우는 기술이면 기술머신 기록도 (SV 용기백배 등)
                try
                {
                    if (c is PK9 k9) k9.SetRecordFlags(TechnicalRecordApplicatorOption.LegalCurrent);
                    else if (c is PA9 a9) a9.SetRecordFlags(TechnicalRecordApplicatorOption.LegalCurrent);
                    else if (c is PK8 k8) k8.SetRecordFlags(TechnicalRecordApplicatorOption.LegalCurrent);
                }
                catch { }
            }
            if (m.Method.IsTrade && c.CurrentHandler == 0 && c.Format >= 6)
            {
                c.HandlingTrainerName = c.OriginalTrainerName == "PKHeX" ? "KR" : "PKHeX";
                c.HandlingTrainerGender = 0; c.CurrentHandler = 1;
                c.HandlingTrainerFriendship = c.PersonalInfo.BaseFriendship;
                if (c is IHandlerLanguage hl) hl.HandlingTrainerLanguage = (byte)c.Language;
                changed = true;
            }
            }
            if (changed)
                foreach (var lv in new[] { baseLv, 16, 20, 30, 36, 40, 50, 60, 100 }.Where(x => x >= baseLv).Distinct())
                {
                    c.CurrentLevel = (byte)lv; c.ResetPartyStats(); c.RefreshChecksum();
                    if (GenHost.IsLegal(c)) return c;
                }
        }
        e.CurrentLevel = baseLv; e.ResetPartyStats(); e.RefreshChecksum();
        return e;
    }
}
