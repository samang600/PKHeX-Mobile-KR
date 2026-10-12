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

    /// <summary>"이름 TID" 줄들 → 어버이 목록.</summary>
    public static List<(string, int)> ParseOTs(string text) => (text ?? "").Replace("\r", "").Split('\n')
        .Select(l => l.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(a => a.Length >= 1)
        .Select(a => (a[0], a.Length > 1 && int.TryParse(a[1], out var tid) ? tid : -1)).ToList();
}

public sealed record LivingDexResult(int Made, int Legal, int Fail, int Targets, int Alpha, bool BoxFull, bool Canceled);

/// <summary>리빙덱스 채우기 엔진 (화면 없음). 모바일 앱과 PC 플러그인이 함께 씀.</summary>
public static class LivingDexGen
{
    public static bool IsAlphaGame(SaveFile sav) => sav is SAV8LA or SAV9ZA;

    /// <summary>도감 순서대로 만들어 시작 박스부터 빈 칸에. progress(완료 수, 전체, 종).</summary>
    public static LivingDexResult Run(SaveFile sav, LivingDexOptions o, CancellationToken tok, Action<int, int, ushort> progress = null)
    {
        bool al = o.Alpha && IsAlphaGame(sav);
        var from = o.From; var to = o.To;
        if (from != null && to == null) to = from; if (to != null && from == null) from = to;
        if (from > to) (from, to) = (to, from);

        // 대상 목록 (도감 순)
        var have = new HashSet<(ushort, byte)>();
        if (o.SkipExisting) for (int b = 0; b < sav.BoxCount; b++) foreach (var p in sav.GetBoxData(b)) if (p.Species != 0) have.Add((p.Species, o.Forms ? p.Form : (byte)0));
        var targets = new List<(ushort, byte)>();
        for (ushort s = 1; s <= sav.MaxSpeciesID; s++)
        {
            if (!sav.Personal.IsSpeciesInGame(s)) continue;
            var fc = o.Forms ? sav.Personal.GetFormEntry(s, 0).FormCount : (byte)1;
            for (byte f = 0; f < fc; f++)
            {
                if (o.Forms && (FormInfo.IsBattleOnlyForm(s, f, sav.Generation) || FormInfo.IsTotemForm(s, f, sav.Context) || !sav.Personal.IsPresentInGame(s, f))) continue;
                if (!have.Contains((s, f))) targets.Add((s, f));
            }
        }
        int made = 0, legal = 0, fail = 0, alphaMade = 0, box = o.StartBox, slot = 0, nth = 0;
        for (int i = 0; i < targets.Count && !tok.IsCancellationRequested; i++)
        {
            // 다음 빈칸
            while (box < sav.BoxCount && sav.GetBoxSlotAtIndex(box, slot).Species != 0) { if (++slot >= sav.BoxSlotCount) { slot = 0; box++; } }
            if (box >= sav.BoxCount) break;
            var (s, f) = targets[i];
            PKM pk = null;
            if (al) { pk = DexAlpha(sav, s, f, o.Shiny); if (pk != null) alphaMade++; }   // 우두머리 조우(진화 전 포함)로 만들기
            if (pk == null) try
            {
                var blank = sav.BlankPKM; blank.Species = s; blank.Form = f; blank.SetGender(blank.GetSaneGender());
                // 빈 개체의 전체 세트(레벨 1·개체값 0·특성 없음)를 그대로 쓰면 ALM이 실패 → 종·폼 줄만 사용
                var first = ShowdownParsing.GetShowdownText(blank).Split('\n')[0].Trim();
                first = System.Text.RegularExpressions.Regex.Replace(first, @"\s*\((M|F)\)\s*$", "");
                pk = GenHost.Legal(sav, first + (o.Shiny ? "\nShiny: Yes" : ""));
                if (o.Shiny && (pk == null || !pk.IsShiny || !new LegalityAnalysis(pk).Valid)) pk = GenHost.Legal(sav, first);
            }
            catch { }
            if (pk == null || pk.Species != s) { fail++; progress?.Invoke(i + 1, targets.Count, s); continue; }
            // 어버이 여러 명이면 돌아가며, 날짜 구간이면 그 안의 무작위 날짜 (합법이 유지될 때만)
            if (o.OTs.Count > 0)
            {
                var (on, otid) = o.OTs[nth++ % o.OTs.Count];
                var me = GenHost.TrainerOf(sav);
                bool six = pk.Format >= 7; int tid = otid >= 0 ? otid : (six ? (int)(me.ID32 % 1_000_000) : me.TID16);
                uint id32 = six ? (uint)(Random.Shared.Next(4295) * 1_000_000L + Math.Min(tid, 999_999)) : (uint)((Random.Shared.Next(65536) << 16) | Math.Min(tid, 65535));
                var t2 = pk.Clone(); GenHost.AutoOT(t2, on, id32, me.Gender, me.Language > 0 ? me.Language : pk.Language);
                if (new LegalityAnalysis(t2).Valid || !new LegalityAnalysis(pk).Valid) pk = t2;
            }
            if (from != null && pk.Format >= 4)
            {
                var span = to.Value.DayNumber - from.Value.DayNumber;
                var t3 = pk.Clone(); t3.MetDate = from.Value.AddDays(Random.Shared.Next(span + 1)); t3.RefreshChecksum();
                if (new LegalityAnalysis(t3).Valid || !new LegalityAnalysis(pk).Valid) pk = t3;
            }
            sav.SetBoxSlotAtIndex(pk, box, slot); made++;
            if (new LegalityAnalysis(pk).Valid) legal++;
            if (++slot >= sav.BoxSlotCount) { slot = 0; box++; }
            progress?.Invoke(i + 1, targets.Count, s);
        }
        return new LivingDexResult(made, legal, fail, targets.Count, alphaMade, box >= sav.BoxCount, tok.IsCancellationRequested);
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
