using PKHeX.Core;
using PKHeX.Core.AutoMod;
namespace PKHeXKR;

/// <summary>리빙덱스 채우기 옵션.</summary>
public sealed class LivingDexOptions
{
    public bool Shiny;                 // 이로치 (불가능하면 일반 색)
    public bool Forms;                 // 모든 폼 포함
    public bool SkipExisting = true;   // 이미 박스에 있는 포켓몬 건너뛰기
    public bool Alpha;                 // 우두머리 (PLA·Z-A, 우두머리 조우가 없으면 일반)
    public DateOnly? From, To;         // 만난 날짜 구간 (비우면 그대로)
    public List<(string Name, int TID)> OTs = [];   // 어버이 여러 명 (돌아가며), 비우면 세이브 트레이너
    public int StartBox;
    public Func<ushort, bool> SpeciesFilter;   // 시험용: 일부 종만
    /// <summary>다른 게임에서 옮겨 온 개체(HOME·GO 등)도 만들기. null이면 게임별 기본값 (Z-A는 끔: HOME에서 옮겨 올 수 없는 폼이 섞이지 않게).</summary>
    public bool? Transfer;
    public static bool DefaultTransfer(SaveFile sav) => sav is not SAV9ZA;
    /// <summary>특정 포켓몬의 성별·볼·폼 미리 정하기 (텍스트 생성과 같은 문법, 한 줄에 하나. 예: "피카츄 ♀ 몬스터볼")</summary>
    public List<OrderSpec> Presets = [];

    /// <summary>미리 정하기 줄들 → 목록 (포켓몬 이름이 없는 줄은 무시).</summary>
    public static List<OrderSpec> ParsePresets(string text, SaveFile sav, Dictionary<string, string> userDict = null)
    {
        var list = new List<OrderSpec>();
        foreach (var line in (text ?? "").Replace("\r", "").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try { foreach (var o in QuickGen.Parse(line, sav, userDict ?? [])) if (o.Species != 0) list.Add(o); } catch { }
        }
        return list;
    }

    /// <summary>"이름 TID" 줄들 → 어버이 목록.</summary>
    public static List<(string, int)> ParseOTs(string text) => (text ?? "").Replace("\r", "").Split('\n')
        .Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(a => a.Length >= 1)
        .Select(a => (a[0], a.Length > 1 && int.TryParse(a[1], out var tid) ? tid : -1)).ToList();
}

public sealed record LivingDexResult(int Made, int Legal, int Fail, int Targets, int Alpha, bool BoxFull, bool Canceled)
{
    /// <summary>미리 정한 성별·볼로는 합법이 안 돼서 정하지 않고 만든 수</summary>
    public int PresetMissed { get; init; }
}

/// <summary>리빙덱스 채우기 엔진 (화면 없음). 모바일 앱과 PC 플러그인이 함께 씀.</summary>
public static class LivingDexGen
{
    public static bool IsAlphaGame(SaveFile sav) => sav is SAV8LA or SAV9ZA;

    /// <summary>도구를 지니면 폼이 바뀌는 포켓몬 (기본 폼만 만듦): 아르세우스·실버디·게노세크트·오거폰·자시안·자마젠타·기라티나·디아루가·펄기아</summary>
    public static bool IsHeldItemForm(ushort species) => species is (int)Species.Arceus or (int)Species.Silvally or (int)Species.Genesect or (int)Species.Ogerpon
        or (int)Species.Zacian or (int)Species.Zamazenta or (int)Species.Giratina or (int)Species.Dialga or (int)Species.Palkia;

    /// <summary>만들 목록 (도감 순, 폼 포함 시 이 게임에 있는 폼만).</summary>
    public static List<(ushort Species, byte Form)> Targets(SaveFile sav, LivingDexOptions o)
    {
        var have = new HashSet<(ushort, byte)>();
        if (o.SkipExisting) for (int b = 0; b < sav.BoxCount; b++) foreach (var p in sav.GetBoxData(b)) if (p.Species != 0) have.Add((p.Species, o.Forms ? p.Form : (byte)0));
        var targets = new List<(ushort, byte)>();
        for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
        {
            if (!sav.Personal.IsSpeciesInGame(s)) continue;
            if (o.SpeciesFilter != null && !o.SpeciesFilter(s)) continue;
            var fc = o.Forms && !IsHeldItemForm(s) ? sav.Personal.GetFormEntry(s, 0).FormCount : (byte)1;
            for (byte f = 0; f < fc; f++)
            {
                if (o.Forms && f > 0 && (FormInfo.IsBattleOnlyForm(s, f, sav.Generation) || FormInfo.IsTotemForm(s, f, sav.Context) || !sav.Personal.IsPresentInGame(s, f))) continue;
                if (!have.Contains((s, f))) targets.Add((s, f));
            }
        }
        return targets;
    }

    /// <summary>도감 순서대로 만들어 시작 박스부터 빈 칸에. progress(완료 수, 전체, 종).
    /// 이 게임에서 합법으로 만들 수 있는 개체만 넣음 (얻을 수 없는 폼·불법 개체는 건너뜀).</summary>
    public static LivingDexResult Run(SaveFile sav, LivingDexOptions o, CancellationToken tok, Action<int, int, ushort> progress = null)
    {
        bool al = o.Alpha && IsAlphaGame(sav);
        var from = o.From; var to = o.To;
        if (from != null && to == null) to = from; if (to != null && from == null) from = to;
        if (from > to) (from, to) = (to, from);
        var targets = Targets(sav, o);
        var me = GenHost.TrainerOf(sav);
        int made = 0, fail = 0, alphaMade = 0, box = o.StartBox, slot = 0, nth = 0, presetMissed = 0;
        for (int i = 0; i < targets.Count && !tok.IsCancellationRequested; i++)
        {
            // 다음 빈칸
            while (box < sav.BoxCount && sav.GetBoxSlotAtIndex(box, slot).Species != 0) { if (++slot >= sav.BoxSlotCount) { slot = 0; box++; } }
            if (box >= sav.BoxCount) break;
            var (s, f) = targets[i];
            var pre = Preset(o, s, f);
            PKM pk = null;
            if (al && pre?.Ball is not > 0) { pk = DexAlpha(sav, s, f, o.Shiny); if (pk != null) alphaMade++; }   // 우두머리 조우(진화 전 포함)로 만들기
            if (pk == null) pk = MakeOne(sav, s, f, o.Shiny, pre, tok, o.Transfer ?? LivingDexOptions.DefaultTransfer(sav));
            if (pk == null && pre != null && !tok.IsCancellationRequested) { pk = MakeOne(sav, s, f, o.Shiny, null, tok, o.Transfer ?? LivingDexOptions.DefaultTransfer(sav)); if (pk != null) presetMissed++; }   // 미리 정한 값으로 안 되면 정하지 않고
            if (pk != null && pk.GetType() != sav.PKMType) pk = EntityConverter.ConvertToType(pk, sav.PKMType, out _);
            if (pk == null || pk.Species != s || pk.Form != f || pk.GetType() != sav.PKMType) { fail++; progress?.Invoke(i + 1, targets.Count, s); continue; }
            // 언어가 비어 있으면 트레이너 언어로 (합법이 유지될 때만)
            if (pk.Language == 0 && pk.Format >= 3 && me.Language > 0) { var t0 = pk.Clone(); t0.Language = me.Language; t0.RefreshChecksum(); if (GenHost.IsLegal(t0)) pk = t0; }
            // 어버이 여러 명이면 돌아가며, 날짜 구간이면 그 안의 무작위 날짜 (합법이 유지될 때만)
            if (o.OTs.Count > 0)
            {
                var (on, otid) = o.OTs[nth++ % o.OTs.Count];
                bool six = pk.Format >= 7; int tid = otid >= 0 ? otid : (six ? (int)(me.ID32 % 1_000_000) : me.TID16);
                uint id32 = six ? (uint)(Random.Shared.Next(4295) * 1_000_000L + Math.Min(tid, 999_999)) : (uint)((Random.Shared.Next(65536) << 16) | Math.Min(tid, 65535));
                var t2 = pk.Clone(); GenHost.AutoOT(t2, on, id32, me.Gender, me.Language > 0 ? me.Language : pk.Language);
                if (GenHost.IsLegal(t2) || !GenHost.IsLegal(pk)) pk = t2;
            }
            if (from != null && pk.Format >= 4)
            {
                var span = to.Value.DayNumber - from.Value.DayNumber;
                var t3 = pk.Clone(); t3.MetDate = from.Value.AddDays(Random.Shared.Next(span + 1)); t3.RefreshChecksum();
                if (GenHost.IsLegal(t3) || !GenHost.IsLegal(pk)) pk = t3;
            }
            { var chk = pk.Clone(); sav.AdaptToSaveFile(chk, false); if (GenHost.IsLegal(chk)) pk = chk; }   // 박스에 넣을 때 세이브가 하는 처리(맡은 트레이너 등)를 미리
            if (!GenHost.IsLegal(pk)) { fail++; progress?.Invoke(i + 1, targets.Count, s); continue; }   // 합법이 아니면 넣지 않음
            sav.SetBoxSlotAtIndex(pk, box, slot); made++;
            if (++slot >= sav.BoxSlotCount) { slot = 0; box++; }
            progress?.Invoke(i + 1, targets.Count, s);
        }
        return new LivingDexResult(made, made, fail, targets.Count, alphaMade, box >= sav.BoxCount, tok.IsCancellationRequested) { PresetMissed = presetMissed };
    }

    private static OrderSpec Preset(LivingDexOptions o, ushort s, byte f)
        => o.Presets.LastOrDefault(p => p.Species == s && p.Form == f) ?? o.Presets.LastOrDefault(p => p.Species == s && p.Form < 0);

    /// <summary>한 마리: 자동 합법화(ALM, 빠름) → 폼이 다르거나 불법이면 조우에서 직접(다른 게임에서 옮겨 온 조우 포함). 성별·볼은 미리 정한 값.</summary>
    public static PKM MakeOne(SaveFile sav, ushort s, byte f, bool shiny, OrderSpec pre, CancellationToken tok, bool transfer = true)
    {
        bool Ok(PKM p)
        {
            if (p == null || p.Species != s || p.Form != f || p.GetType() != sav.PKMType) return false;
            if (!transfer && (p.Version == GameVersion.GO || p.Version.Context != sav.Context)) return false;   // 이 게임 출신만
            if (pre != null && ((pre.Gender >= 0 && p.Gender != pre.Gender) || (pre.Ball > 0 && p.Ball != pre.Ball))) return false;
            if (GenHost.IsLegal(p)) return true;
            var c = p.Clone(); sav.AdaptToSaveFile(c, false); return GenHost.IsLegal(c);
        }
        foreach (var wantShiny in shiny ? new[] { true, false } : new[] { false })
        {
            // 1) 자동 합법화: 종·폼(+성별·볼) 줄
            try
            {
                var blank = sav.BlankPKM; blank.Species = s; blank.Form = f; blank.SetGender(pre?.Gender >= 0 ? (byte)pre.Gender : blank.GetSaneGender());
                // 빈 개체의 전체 세트(레벨 1·개체값 0·특성 없음)를 그대로 쓰면 ALM이 실패 → 종·폼 줄만 사용
                var first = ShowdownParsing.GetShowdownText(blank).Split('\n')[0].Trim();
                if (pre?.Gender is not (0 or 1)) first = System.Text.RegularExpressions.Regex.Replace(first, @"\s*\((M|F)\)\s*$", "");
                var lines = new List<string> { first };
                if (wantShiny) lines.Add("Shiny: Yes");
                if (pre?.Ball > 0) lines.Add("Ball: " + GameInfo.GetStrings("en").balllist[pre.Ball]);
                var p = GenHost.Legal(sav, string.Join("\n", lines));
                if (p != null && pre?.Ball > 0 && p.Ball != pre.Ball) { var t = p.Clone(); t.Ball = (byte)pre.Ball; t.RefreshChecksum(); if (GenHost.IsLegal(t)) p = t; }
                if (Ok(p) && (!wantShiny || p.IsShiny)) return p;
            }
            catch { }
            if (tok.IsCancellationRequested) return null;
            // 2) 조우에서 직접 (폼이 정해지는 조우·3DS 비비용 무늬 등)
            var o = new OrderSpec
            {
                Species = s, Form = f, Shiny = wantShiny ? 1 : 0, AllowTransfer = transfer, SkipAlm = true, Seconds = 0.7,
                Gender = pre?.Gender ?? -1, Ball = pre?.Ball ?? -1,
            };
            try
            {
                var (p, _) = QuickGen.Make(o, sav, tok);
                if (Ok(p)) return p;
            }
            catch { }
        }
        return null;
    }

    /// <summary>우두머리 조우(진화 전 포켓몬 포함)로 합법 개체 만들기. 없으면 null.</summary>
    public static PKM DexAlpha(SaveFile sav, ushort species, byte form, bool shiny)
    {
        try
        {
            var b = sav.BlankPKM; b.Species = species; b.Form = form; b.SetGender(b.GetSaneGender());
            var encs = EncounterMovesetGenerator.GenerateEncounters(b, sav, ReadOnlyMemory<ushort>.Empty, GameUtil.GetVersionsWithinRange(b, b.Context).ToArray())
                .Where(e => e is IAlphaReadOnly { IsAlpha: true } && e.Context == sav.Context).OrderBy(e => e.Species == species ? 0 : 1).Take(6).ToList();
            if (shiny)   // 이로치: 무작위 생성으로는 거의 안 나옴 → 조우의 시드를 돌려 이로치가 나오는 시드를 찾음 (PLA·Z-A 시드 방식)
                foreach (var e in encs)
                {
                    Seeder sd = null; try { sd = Seeder.For(e, GenHost.TrainerOf(sav)); } catch { }
                    if (sd == null) continue;
                    ulong seed = (ulong)Random.Shared.NextInt64();
                    for (int n = 0; n < 300_000; n++, seed = seed * 6364136223846793005UL + 1442695040888963407UL)
                    {
                        var p = sd.Template.Clone();
                        try { if (!sd.Gen(p, seed)) continue; } catch { break; }
                        if (!p.IsShiny) continue;
                        try { p = sd.Finish(p) ?? p; } catch { }
                        p = EntityConverter.ConvertToType(p, sav.PKMType, out _) ?? p;
                        if (p.Species != species) p = EvoUtil.Evolve(p, species, form);
                        if (p.Species == species && p.IsShiny && p is IAlpha { IsAlpha: true } && new LegalityAnalysis(p).Valid) return p;
                        break;
                    }
                }
            foreach (var e in encs)
                foreach (var wantShiny in shiny ? new[] { true, false } : new[] { false })
                {
                    var crit = EncounterCriteria.Unrestricted with { Shiny = wantShiny ? PKHeX.Core.Shiny.Always : PKHeX.Core.Shiny.Random };
                    for (int t = 0; t < 3; t++)
                    {
                        PKM p; try { p = e.ConvertToPKM(GenHost.TrainerOf(sav), crit); } catch { break; }
                        p = EntityConverter.ConvertToType(p, sav.PKMType, out _) ?? p;
                        if (wantShiny && !p.IsShiny) continue;
                        if (p.Species != species) p = EvoUtil.Evolve(p, species, form);
                        if (p.Species == species && p is IAlpha { IsAlpha: true } && new LegalityAnalysis(p).Valid) return p;
                    }
                }
        }
        catch { }
        return null;
    }
}
