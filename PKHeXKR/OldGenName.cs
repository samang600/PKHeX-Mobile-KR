using PKHeX.Core;
namespace PKHeXKR;

/// <summary>
/// 1~3세대 트레이너 이름: 이 게임들은 세이브 형식(일본판/해외판)마다 쓸 수 있는 글자가 정해져 있음.
/// 해외판 형식 세이브에는 가나·한자를, 일본판 형식에는 반각 영문을 넣을 수 없어 PKHeX가 조용히 빈 이름으로 저장하던 문제 처리.
/// </summary>
public static class OldGenName
{
    public static bool HasJapanese(string s) => s.Any(c => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿' or >= 'ｦ' and <= 'ﾟ');
    public static bool IsOld(SaveFile s) => s is SAV1 or SAV2 or SAV3;
    public static bool IsJapaneseSave(SaveFile s) => s is SAV1 { Japanese: true } or SAV2 { Japanese: true } or SAV3 { Japanese: true };

    /// <summary>일본판 형식이면 반각 숫자(3세대는 영문도)를 전각으로 (일본판 게임의 글자표에는 전각만 있음).</summary>
    public static string ForSave(SaveFile s, string name)
    {
        if (!IsJapaneseSave(s)) return name;
        bool letters = s is SAV3;
        return string.Concat(name.Select(c => c is >= '0' and <= '9' || (letters && c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') ? (char)(c - '!' + '！') : c == ' ' && letters ? '　' : c));
    }

    /// <summary>3세대 해외판 형식 세이브 → 일본판 형식 (블록 구조는 같고 글자 체계만 다름. PKHeX는 이름 칸 7·8번째 바이트가 0이면 일본판으로 읽음).</summary>
    public static SaveFile ConvertGen3ToJapanese(SAV3 old, string name)
    {
        var data = old.Write(BinaryExportSetting.ExcludeHeader | BinaryExportSetting.ExcludeFooter | BinaryExportSetting.ExcludeFinalize).ToArray();
        var tmp = Make(old, data);
        var ot = tmp.Small[..8]; ot.Fill(0xFF);
        StringConverter3.SetString(ot[..6], name, 5, true, StringConverterOption.None);
        ot[6] = 0; ot[7] = 0;
        var data2 = tmp.Write(BinaryExportSetting.ExcludeHeader | BinaryExportSetting.ExcludeFooter | BinaryExportSetting.ExcludeFinalize).ToArray();
        var ns = Make(old, data2);
        if (!ns.Japanese) throw new InvalidOperationException("일본판 형식으로 바꾸지 못했습니다");
        old.Metadata.ShareExtraInfo(ns.Metadata);
        return ns;
    }
    private static SAV3 Make(SAV3 old, byte[] data)
    {
        SAV3 s = old switch { SAV3E => new SAV3E(data), SAV3FRLG => new SAV3FRLG(data), _ => new SAV3RS(data) };
        if (s is SAV3FRLG f) f.ResetPersonal(old.Version); else try { s.Version = old.Version; } catch { }
        return s;
    }

    /// <summary>
    /// 트레이너 정보를 현재 세이브에 적용하고, 1~3세대에서 이름이 안 들어가면 해결 (빈 세이브는 일본판으로 다시 만들기, 3세대 파일은 일본판 형식 전환 확인).
    /// 반환: 사용자에게 보여줄 결과 문구 (null이면 문제 없음).
    /// </summary>
    public static async Task<string> ApplyChecked()
    {
        var s = AppState.Sav; var v = s.Version;
        var want = AppState.OTFor(v);
        if (IsOld(s))
        {
            var fit = ForSave(s, want);
            if (fit != want) { AppState.SetTrainerFor(v, fit, AppState.GenderFor(v), AppState.TIDFor(v), AppState.SIDFor(v), AppState.LangFor(v)); want = fit; }
        }
        var prevOT = s.OT;
        AppState.ReapplyTrainer();
        if (!IsOld(s) || s.OT == want) return null;
        try { s.OT = prevOT; AppState.ReapplyTrainerKeepOT(); } catch { }   // 들어가지 않은 이름 대신 원래 이름 유지 (빈 이름 방지)

        if (HasJapanese(want) && !IsJapaneseSave(s))
        {
            var page = Application.Current.Windows[0].Page;
            if (string.IsNullOrEmpty(s.Metadata.FileName))   // 파일 없이 만든 빈 세이브 → 일본판으로 새로
            {
                AppState.SetTrainerFor(v, want, AppState.GenderFor(v), AppState.TIDFor(v), AppState.SIDFor(v), (int)LanguageID.Japanese);
                AppState.SetSave(AppState.NewBlankSave(v));
                return AppState.Sav.OT == want ? "일본어 이름이라 일본판 빈 세이브로 다시 만들었습니다" : "이 이름은 일본판 글자표에도 없는 글자가 있습니다";
            }
            if (s is SAV3 s3)
            {
                bool go = await page.DisplayAlertAsync("일본어 이름",
                    "이 세이브는 해외판(영어 등) 형식이라 가나·한자를 쓸 수 없습니다.\n\n세이브를 일본판 형식으로 바꿔 이름을 넣을까요?\n· 일본판 게임(롬)에서 쓰는 세이브일 때만 선택하세요. 해외판 게임에서는 이름이 깨져 보입니다.\n· 이름은 최대 5글자, 박스 이름 등 다른 글자는 그대로입니다.\n· 편집 중인 포켓몬은 저장되지 않으면 사라집니다.",
                    "일본판 형식으로 바꾸기", "취소");
                if (!go) return "이름을 바꾸지 않았습니다 (해외판 형식 세이브는 일본어를 쓸 수 없음)";
                try
                {
                    var name5 = new string(want.Take(5).ToArray());
                    var ns = ConvertGen3ToJapanese(s3, name5);
                    AppState.SetTrainerFor(v, name5, AppState.GenderFor(v), AppState.TIDFor(v), AppState.SIDFor(v), (int)LanguageID.Japanese);
                    AppState.SetSave(ns);
                    AppState.ReapplyTrainer();
                    return ns.OT == name5 ? "일본판 형식으로 바꾸고 이름을 넣었습니다 · 세이브 내보내기로 저장하세요" : "일본판 형식으로 바꿨지만 이름에 쓸 수 없는 글자가 있습니다";
                }
                catch (Exception ex) { return "일본판 형식 전환 실패: " + ex.Message; }
            }
            return "1·2세대 해외판 세이브는 일본판과 구조가 달라 일본어 이름을 넣을 수 없습니다 (일본판 세이브에서만 가능)";
        }
        if (IsJapaneseSave(s)) return "일본판 형식 세이브에 쓸 수 없는 글자가 있습니다 (가나·전각 숫자" + (s is SAV3 ? "·전각 영문" : "") + "만 가능, 최대 5글자)";
        return $"이 세이브에 쓸 수 없는 글자가 있습니다 (현재 이름: {s.OT})";
    }
}
