using System.Text;
using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// 쇼다운 세트 + SysBot·ALM이 읽는 추가 줄 (디스코드 교환봇에 그대로 붙여넣기용).
/// Ball·Shiny(Square/Star)·Language·OT/TID/SID/OTGender·Alpha 설정 줄과 .속성=값 줄(증표·크기)을 덧붙임.
/// </summary>
public static class SysBotText
{
    public static string Get(PKM pk)
    {
        var sb = new StringBuilder(ShowdownParsing.GetShowdownText(pk).TrimEnd());
        var lines = sb.ToString().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        // 이로치 종류: 8세대 이후는 별·네모 구분
        int si = lines.FindIndex(l => l.StartsWith("Shiny:"));
        if (pk.IsShiny && pk.Format >= 8)
        {
            var v = pk.ShinyXor == 0 ? "Square" : "Star";
            if (si >= 0) lines[si] = "Shiny: " + v; else lines.Add("Shiny: " + v);
        }
        var add = new List<string>();
        var ball = (Ball)pk.Ball;
        if (ball != Ball.None) add.Add("Ball: " + (ball.ToString().StartsWith("LA") ? ball.ToString() : ball + " Ball"));
        if (pk.Language > 0 && Enum.IsDefined(typeof(LanguageID), (LanguageID)pk.Language)) add.Add("Language: " + (LanguageID)pk.Language);
        if (pk is IAlphaReadOnly { IsAlpha: true }) add.Add("Alpha: Yes");
        add.Add("OT: " + pk.OriginalTrainerName);
        if (pk.Format >= 7) { add.Add($"TID: {pk.DisplayTID}"); add.Add($"SID: {pk.DisplaySID}"); }
        else { add.Add($"TID: {pk.TID16}"); add.Add($"SID: {pk.SID16}"); }
        add.Add("OTGender: " + (pk.OriginalTrainerGender == 1 ? "Female" : "Male"));
        // 증표 (붙어 있는 것 전부)
        if (pk is IRibbonSetMark8 or IRibbonSetMark9)
            foreach (var prop in pk.GetType().GetProperties().Where(p => p.PropertyType == typeof(bool) && p.Name.StartsWith("RibbonMark")))
                if (prop.GetValue(pk) is true) add.Add($".{prop.Name}=True");
        if (pk is IRibbonSetAffixed { AffixedRibbon: >= 0 } af) add.Add($".AffixedRibbon={af.AffixedRibbon}");
        // 크기
        if (pk is IScaledSize3 s3) add.Add($".Scale={s3.Scale}");
        if (pk is IScaledSize ss && pk is not PA9) { add.Add($".HeightScalar={ss.HeightScalar}"); add.Add($".WeightScalar={ss.WeightScalar}"); }
        // 기술 줄(- …) 앞에 넣음: 봇마다 세트 끝을 기술 줄로 보는 경우가 있음
        int mi = lines.FindIndex(l => l.StartsWith("- "));
        if (mi < 0) lines.AddRange(add); else lines.InsertRange(mi, add);
        return string.Join(Environment.NewLine, lines);
    }

    public static string GetMany(IEnumerable<PKM> mons) => string.Join(Environment.NewLine + Environment.NewLine, mons.Select(Get));
}
