using PKHeX.Core;
namespace PKHeXKR;

/// <summary>도감 진행(본 것·잡은 것·빠진 종)과 박스 통계 (이로치·우두머리·불법·알·출신 게임·중복 종).</summary>
public class StatsSheet : Sheet
{
    public StatsSheet() : base("도감 진행 · 박스 통계", 0.9)
    {
        var sav = AppState.Sav; var s = GameInfo.Strings;
        var stack = new VerticalStackLayout { Spacing = 10 };
        var busy = new ActivityIndicator { IsRunning = true, Color = T.Accent };
        stack.Children.Add(busy);
        Body.Add(new ScrollView { Content = stack });
        _ = Fill();

        async Task Fill()
        {
            // ── 도감 ──
            var inGame = new List<ushort>(); var seen = new List<ushort>(); var caught = new List<ushort>(); bool dexOk = false;
            // ── 박스 ──
            int total = 0, shiny = 0, square = 0, alpha = 0, illegal = 0, eggs = 0, nick = 0, gmax = 0, lv100 = 0, sixV = 0;
            var byGame = new Dictionary<string, int>(); var byGen = new Dictionary<int, int>(); var bySpecies = new Dictionary<ushort, int>(); var byBall = new Dictionary<int, int>(); var ots = new Dictionary<string, int>();
            var boxSpecies = new HashSet<ushort>();
            await Task.Run(() =>
            {
                for (ushort sp = 1; sp <= sav.MaxSpeciesID; sp++)
                {
                    if (!sav.Personal.IsSpeciesInGame(sp)) continue;
                    inGame.Add(sp);
                    bool se = false, ca = false;
                    try { se = sav.GetSeen(sp); ca = sav.GetCaught(sp); } catch { }
                    if (se) seen.Add(sp); if (ca) caught.Add(sp);
                }
                dexOk = seen.Count > 0 || caught.Count > 0;
                for (int b = 0; b < sav.BoxCount; b++)
                    for (int i = 0; i < sav.BoxSlotCount; i++)
                    {
                        var p = sav.GetBoxSlotAtIndex(b, i); if (p.Species == 0) continue;
                        total++; boxSpecies.Add(p.Species);
                        if (p.IsShiny) { shiny++; if (p.ShinyXor == 0) square++; }
                        if (p is IAlpha { IsAlpha: true }) alpha++;
                        if (p.IsEgg) eggs++;
                        if (p.IsNicknamed) nick++;
                        if (p is IGigantamax { CanGigantamax: true }) gmax++;
                        if (p.CurrentLevel >= 100) lv100++;
                        if (p.IV_HP == 31 && p.IV_ATK == 31 && p.IV_DEF == 31 && p.IV_SPA == 31 && p.IV_SPD == 31 && p.IV_SPE == 31) sixV++;
                        if (!AppState.IsLegal(p)) illegal++;
                        string g; try { g = AppState.GameName(p.Version); } catch { g = "?"; }
                        byGame[g] = byGame.GetValueOrDefault(g) + 1;
                        int gen; try { gen = p.Version.Generation; } catch { gen = 0; }
                        byGen[gen] = byGen.GetValueOrDefault(gen) + 1;
                        bySpecies[p.Species] = bySpecies.GetValueOrDefault(p.Species) + 1;
                        byBall[p.Ball] = byBall.GetValueOrDefault(p.Ball) + 1;
                        var ot = p.OriginalTrainerName ?? ""; ots[ot] = ots.GetValueOrDefault(ot) + 1;
                    }
            });
            stack.Children.Remove(busy);

            // 도감 카드
            var dexStack = new VerticalStackLayout { Spacing = 6 };
            dexStack.Children.Add(T.L($"도감 · {AppState.GameName(sav.Version)}", 15, bold: true));
            if (!dexOk) dexStack.Children.Add(T.L("이 게임의 도감 기록을 읽지 못했거나 아직 아무것도 등록되지 않았습니다", 13, sub: true));
            else
            {
                dexStack.Children.Add(T.L($"본 것 {seen.Count} · 잡은 것 {caught.Count} / 게임에 나오는 종 {inGame.Count}", 14));
                var bar = new ProgressBar { Progress = inGame.Count > 0 ? (double)caught.Count / inGame.Count : 0, ProgressColor = T.Accent };
                dexStack.Children.Add(bar);
                var missing = inGame.Where(x => !caught.Contains(x)).ToList();
                var notInBox = missing.Where(x => !boxSpecies.Contains(x)).ToList();
                dexStack.Children.Add(T.L($"못 잡은 종 {missing.Count} (그중 박스에도 없는 종 {notInBox.Count})", 13, sub: true));
                if (missing.Count > 0)
                {
                    var preview = T.L(string.Join(", ", missing.Take(40).Select(x => s.Species[x])) + (missing.Count > 40 ? $" 외 {missing.Count - 40}종" : ""), 12, sub: true); preview.LineBreakMode = LineBreakMode.WordWrap;
                    dexStack.Children.Add(preview);
                    var toQuick = T.Pill("빠른 생성으로", primary: true);
                    toQuick.Clicked += async (_, _) =>
                    {
                        var page = Application.Current.Windows[0].Page;
                        var which = await page.DisplayActionSheetAsync("어떤 종을 넣을까요?", "취소", null, $"못 잡은 종 전부 ({missing.Count})", $"박스에도 없는 종만 ({notInBox.Count})");
                        if (which == null || which == "취소") return;
                        var list = which.StartsWith("박스") ? notInBox : missing;
                        QuickSheet.Preset(string.Join("\n", list.Select(x => s.Species[x])));
                        Close(); SheetHost.Show(new QuickSheet());
                        Note.Show($"{list.Count}종을 빠른 생성에 넣었습니다 · 이로치·볼 등을 '모두 …' 줄로 덧붙일 수 있어요");
                    };
                    var toDex = T.Pill("리빙덱스로 채우기");
                    toDex.Clicked += (_, _) => { Close(); SheetHost.Show(new LivingDexSheet()); Note.Show("'이미 있는 종 건너뛰기'를 켜고 시작하세요"); };
                    dexStack.Children.Add(T.Btns(2, toQuick, toDex));
                }
            }
            stack.Children.Add(T.Card(dexStack));

            // 박스 통계 카드
            var bs = new VerticalStackLayout { Spacing = 6 };
            bs.Children.Add(T.L("박스", 15, bold: true));
            bs.Children.Add(T.L($"포켓몬 {total}마리 / {sav.BoxCount * sav.BoxSlotCount}칸 · 종류 {boxSpecies.Count}종", 14));
            bs.Children.Add(T.L($"이로치 {shiny} (네모 {square}) · 우두머리 {alpha} · 거다이맥스 {gmax} · 알 {eggs} · 별명 {nick} · Lv100 {lv100} · 6V {sixV}", 13, sub: true).Also(l => l.LineBreakMode = LineBreakMode.WordWrap));
            var ill = T.L($"불법 {illegal}마리", 13, sub: true); if (illegal > 0) ill.TextColor = T.Bad;
            bs.Children.Add(ill);
            if (byGame.Count > 0) bs.Children.Add(T.L("출신 게임: " + string.Join(" · ", byGame.OrderByDescending(x => x.Value).Take(8).Select(x => $"{x.Key} {x.Value}")) + (byGame.Count > 8 ? " …" : ""), 12, sub: true).Also(l => l.LineBreakMode = LineBreakMode.WordWrap));
            if (byGen.Count > 1) bs.Children.Add(T.L("세대: " + string.Join(" · ", byGen.OrderBy(x => x.Key).Select(x => $"{x.Key}세대 {x.Value}")), 12, sub: true).Also(l => l.LineBreakMode = LineBreakMode.WordWrap));
            if (byBall.Count > 0) bs.Children.Add(T.L("볼: " + string.Join(" · ", byBall.OrderByDescending(x => x.Value).Take(6).Select(x => $"{(x.Key < s.balllist.Length ? s.balllist[x.Key] : x.Key.ToString())} {x.Value}")), 12, sub: true).Also(l => l.LineBreakMode = LineBreakMode.WordWrap));
            if (ots.Count > 0) bs.Children.Add(T.L("어버이: " + string.Join(" · ", ots.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} {x.Value}")), 12, sub: true).Also(l => l.LineBreakMode = LineBreakMode.WordWrap));
            var dup = bySpecies.Where(x => x.Value > 1).OrderByDescending(x => x.Value).ToList();
            if (dup.Count > 0)
            {
                bs.Children.Add(T.L($"중복 종 {dup.Count}: " + string.Join(" · ", dup.Take(12).Select(x => $"{s.Species[x.Key]} ×{x.Value}")) + (dup.Count > 12 ? " …" : ""), 12, sub: true).Also(l => l.LineBreakMode = LineBreakMode.WordWrap));
                var find = T.Pill("가장 많은 중복 종 강조");
                find.Clicked += (_, _) => { BoxFilter.Clear(); BoxFilter.Species = dup[0].Key; Close(); AppState.NotifyBox(); Note.Show($"{s.Species[dup[0].Key]} 칸을 강조합니다 (박스 검색에서 해제)"); };
                bs.Children.Add(find);
            }
            if (illegal > 0)
            {
                var findBad = T.Pill("불법 포켓몬 강조");
                findBad.Clicked += (_, _) => { BoxFilter.Clear(); BoxFilter.Illegal = true; Close(); AppState.NotifyBox(); };
                bs.Children.Add(findBad);
            }
            stack.Children.Add(T.Card(bs));
        }
    }
}
