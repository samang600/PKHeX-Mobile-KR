namespace PKHeXKR.Plugin;

/// <summary>
/// PokeLDN 교환 트레이너 국적(언어). PokeLDN은 SV·Z-A에서 녹화해 둔 트레이너(영어)의 이름만 바꿔 보내서
/// 한글 이름이 게임에서 ??로 보임 → 언어 칸만 고친 사본을 --record-set / --game-dir로 넘김. BDSP는 --language.
/// SV 레코드 1(zlib, 4KB 창)은 PokeLDN과 같은 방식으로 미리 압축해 둔 것 (원본 SHA-256이 같을 때만 씀).
/// </summary>
internal static class LdnLanguage
{
    public static readonly (int Id, string Name)[] Languages =
        [(8, "한국어"), (1, "일본어"), (2, "영어"), (3, "프랑스어"), (4, "이탈리아어"), (5, "독일어"), (7, "스페인어"), (9, "중국어 (간체)"), (10, "중국어 (번체)")];

    public const string SvRecord1Sha256 = "4abc162411f89a86996b50e54174df93cf4156a232296101f9724773f47ab80c";
    public static readonly Dictionary<int, string> SvRecord1 = new()
    {
        [1] = "SEvMkr9KxEAQxjfnn+IQrpLbImCTKgRBSbFFIE3KFJYLNooIFtpY5cAn0CfwFWbBrbXIIr6NeYPAuc6uiUnu4uUUixv49rezMws77OeQEbGxc/V2cDQ/Jo5zQq7JOZmRS3JL/hLOUMOGxV2Gy57d7uLo27g2YYZ5rfanKIq6QO2jnlE3qGlVH6O2UKPW/fZ+xdmZ8yXqUtfkS/SoCyAV0qvyX3LqQp7DcN8CA+oVcA+G+gMsi7Is2/WHQmuGBK21yUFpZUh8f4wMTB8L2FqkIQ2kVIqFLLTnDYOnQ99vchpO4njS09dLHrFQCgE84lGd4/MIT3g010oaSgGATGy9YpZmaYdJ1qlvKut3LjGdJfhPUM/zmPIUv03PLN/LzDDumXtNrvKvMv4wPhZCDPruv/ntQ+qVbf8u0NSVkmLIpz8SfYr+1bXvKOYvue7knwAAAP//AwBa6byZ",
        [2] = "SEvMkr9KxEAQxjfxT3EIV8ltEbBJFYKgpNgikCZlCsuAjSKChTZWCfgE+gS+wiy4tRZZxLcxbxA419k1McldvJxicQPf/nZ2ZmGH/SxiExM7V28HR/NjYlsn5Jqck5xcklvyl7DGGjYs7jJc9sx2F0ffxrUNPcxrvT9FUdQFah/1jLpBzer6BLWFsjv3u/sVZ2fWl6hDHZ0v0aUOgJBIt85/yZkDRQHjfQv0qVvCPWiqDzAsq6rq1h9KpRgSlFI6B6mkJvG8CdLXfcxna5EG1BdCShawwJy39J8OPa/NaTCNoulA3yDTkAWCc0jDNGxyfB5J4zScKyk0BQdAxqZeM0uypMc469U3lc07l5jkMf4TNPM8JmmC36Zyw/cq04wG5l6Tq/wrtT+0jznno777b377kLpV178L1HUpBR/z6Y9En6J/VeM7ivlLoXr5JwAAAP//AwBgS7ya",
        [3] = "SEvMkrFKxEAQhjd3anEIV8ltEbBJFYKgpNgikCZlCssFG0UEC22sEvAJ9Al8hVlway2yiG9j3iBwrrO5xCR38XKKxQ38++3szMIO+1tkRMrYvX4/PJ6fkLF1Sm7IBUnJFbkjfwlrqGHL4j7BZb/c7uHoO7g2YYZ5q/ZnKIq6RB2gXlC3qFlVn6DGqFHrfnu/5uzcWoja1Db5Ch1qA0iFdKr8l5zZkGUw3LdEjzo5PICh/oSSeVEU7fpjrjVDgtba5KC0MiSuO0F6po95bCNSn3pSKsV85pfnDb3nI9dtcupPw3Da09dLHjBfCgE84EGd4/MIj3gw10oaSgGAjMp6xSRO4g6jpFPfVtbvXGGcRvhPUM/zFPMYv02nJT+KxDDsmXtDrvOvMv4wPhZCDPruv/ntQ+oUbf8u0dSVkmLIpz8SfYr+1bXvKOavme7kXwAAAP//AwBlrbyb",
        [4] = "SEvMkrFKxEAQhjfnaXEIV8ltEbBJFYKgpNgikCZlCssFG0UEC22sEvAJ9Al8hVlway2yiG9j3iBwrrO5xCR38XKKxQ38++3szMIO+1tkRMrYvX4/PJ6fkLF1Sm7IBUnJFbkjfwlrqGHL4j7BZb/c7uHoY1ybMMO8VfszFEVdog5QL6hb1KyqT1A7qFHrfnu/5uzcWoja1Db5Ch1qA0iFdKr8l5zZkGUw3LdEjzo5PICh/oSSeVEU7fpjrjVDgtba5KC0MiSuO0F6po95bCNSn3pSKsV85pfnDb3nI9dtcupPw3Da09dLHjBfCgE84EGd4/MIj3gw10oaSgGAjMp6xSRO4g6jpFPfVtbvXGGcRvhPUM/zFPMYv02nJT+KxDDsmXtDrvOvMv4wPhZCDPruv/ntQ+oUbf8u0dSVkmLIpz8SfYr+1bXvKOavme7kXwAAAP//AwBrD7yc",
        [5] = "SEvMkrFKxEAQhjenZ3EIV8ltEbBJFYKgpNgikCZlCssFG0UEC22sEvAJ9Al8hVlway2yiG9j3iBwrrO5xCR38XKKxQ38++3szMIO+1tkRMoYX78fHs9PyNg6JTfkgqTkityRv4Q11LBlcZ/gsl9u93D0XVybMMO8VfszFEVdog5QL6hb1KyqT1A7qFHrfnu/5uzcWoja1Db5Ch1qA0iFdKr8l5zZkGUw3LdEjzo5PICh/oSSeVEU7fpjrjVDgtba5KC0MiSuO0F6po95bCNSn3pSKsV85pfnDb3nI9dtcupPw3Da09dLHjBfCgE84EGd4/MIj3gw10oaSgGAjMp6xSRO4g6jpFPfVtbvXGGcRvhPUM/zFPMYv02nJT+KxDDsmXtDrvOvMv4wPhZCDPruv/ntQ+oUbf8u0dSVkmLIpz8SfYr+1bXvKOavme7kXwAAAP//AwBwcbyd",
        [7] = "SEvMkr9KxEAQxjfnPziEq+S2CNikCkFQUmwRSJMyhWXA5kQEC22sEvAJ9Al8hVlway2yiG9j3iBwrrNrYpK7eDnF4ga+/e3szMIO+1lkREzsXL0dHs9PyJ51Sq7JOcnIJbklfwlrqGHD4i7FZd9sd3H0bVyb0MO8VvszFEVdoA5Qz6gb1LSqj1FbqFHrfnu/4mxmfYna1Nb5Eh1qAwiJdKr8l5zakOcw3LdAjzoF3IOm+gDDoizLdv2hUIohQSmlc5BKahLXHSM93cc8thapTz0hpGQ+8815Q+/pyHWbnPqTMJz09PUyCZgvOIckSII6x+eRJEqCuZJCU3AAZGTqFdM4jTuM0k59U1m/c4lxFuE/QT3PY5zE+G0qM3wvU82wZ+41ucq/UvtD+5hzPui7/+a3D6lTtv27QF2XUvAhn/5I9Cn6V9W+o5i/5KqTfwIAAP//AwB7Nbyf",
        [8] = "SEvMkr9KxEAQxjfnH+QQrpLbImCTKgRBSbFFIE3KFJYBmxMRLLSxSsAn0CfwFWbBrbXIIr6NeYPAuc6uiUnu4uUUixv49rezMws77GeRETGxc/V2eDw/IXvWKbkm5yQjl+SW/CWsoYYNi7sUl32z3cXRt3FtQg/zWu3PUBR1gTpAPaNuUNOqPkZtoUat++39irOZ9SVqU1vnS3SoDSAk0qnyX3JqQ57DcN8CPeoUcA+a6gMMi7Is2/WHQimGBKWUzkEqqUlcd4z0dB/z2FqkPvWEkJL5zDfnDb2nI9dtcupPwnDS09fLJGC+4BySIAnqHJ9HkigJ5koKTcEBkJGpV0zjNO4wSjv1TWX9ziXGWYT/BPU8j3ES47epzPC9TDXDnrnX5Cr/Su0P7WPO+aDv/pvfPqRO2fbvAnVdSsGHfPoj0afoX1X7jmL+kqtO/gkAAP//AwCAl7yg",
        [9] = "SEvMkr9KxEAQxjfnH/AQrpLbImCTKgRBSbFFIE3KFJYBmxMRLLSxSsAn0CfwFWbBrbXIIr6NeYPAuc6uiUnu4uUUixv49rezMws77GeRETGxc/V2eDw/IXvWKbkm5yQjl+SW/CWsoYYNi7sUl32z3cXRt3FtQg/zWu3PUBR1gTpAPaNuUNOqPkZtoUat++39irOZ9SVqU1vnS3SoDSAk0qnyX3JqQ57DcN8CPeoUcA+a6gMMi7Is2/WHQimGBKWUzkEqqUlcd4z0dB/z2FqkPvWEkJL5zDfnDb2nI9dtcupPwnDS09fLJGC+4BySIAnqHJ9HkigJ5koKTcEBkJGpV0zjNO4wSjv1TWX9ziXGWYT/BPU8j3ES47epzPC9TDXDnrnX5Cr/Su0P7WPO+aDv/pvfPqRO2fbvAnVdSsGHfPoj0afoX1X7jmL+kqtO/gkAAP//AwCF+byh",
        [10] = "SEvMkr9KxEAQxjfnnyIIV8ltEbBJFYKgpNgikCZlCsuAjSKChTZWCfgE+gS+wiy4tRZZxLcxbxA419k1McldvJxicQPf/nZ2ZmGH/SwyISZ2rt4OjubHxLZOyDU5Jzm5JLfkL2GNNWxY3GW47JntLo6+jWsbepjXen+KoqgL1D7qGXWDmtV1G7WFmnTud/crzs6sL1GHOjpfoksdACGRbp3/kjMHigLG+xboU7eEe9BUH2BYVlXVrT+USjEkKKV0DlJJTeJ5NtLXfcxna5EG1BdCShawwJy39J8OPa/NaTCNoulA3yDTkAWCc0jDNGxyfB5J4zScKyk0BQdAxqZeM0uypMc469U3lc07l5jkMf4TNPM8JmmC36Zyw/cq04wG5l6Tq/wrtT+0jznno777b377kLpV178L1HUpBR/z6Y9En6J/VeM7ivlLoXr5JwAAAP//AwCLW7yi",
    };

    /// <summary>게임별로 PokeLDN에 덧붙일 인자. pokeldnPath: pokeldn.exe 또는 소스 폴더. 못 하면 note에 이유.</summary>
    public static List<string> Args(string game, int lang, string pokeldnPath, string work, out string note)
    {
        note = "";
        var args = new List<string>();
        if (game == "bdsp") { args.AddRange(["--language", lang.ToString()]); return args; }
        if (game is not ("sv" or "za")) return args;   // 소드실드·레츠고는 콘솔이 보낸 자기 정보를 그대로 돌려줘서 콘솔 언어를 따름
        var data = FindData(pokeldnPath, game == "sv" ? Path.Combine("sv", "data", "records") : Path.Combine("za", "data"));
        if (data == null) { note = "PokeLDN 데이터 폴더를 찾지 못해 국적을 바꾸지 못했습니다"; return args; }
        var dest = Path.Combine(work, $"{game}_lang{lang}");
        if (Directory.Exists(dest)) Directory.Delete(dest, true);
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(data)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
        if (game == "sv")
        {
            var r1 = Path.Combine(dest, "001.bin");
            if (!File.Exists(r1) || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(r1))).ToLowerInvariant() != SvRecord1Sha256)
            { note = "이 PokeLDN 버전의 SV 트레이너 레코드가 달라 국적을 바꾸지 못했습니다"; return args; }
            File.WriteAllBytes(r1, Convert.FromBase64String(SvRecord1[lang]));
            args.AddRange(["--record-set", dest]);
        }
        else
        {
            foreach (var (name, nameAt) in new[] { ("identity10.bin", 24), ("identity11.bin", 28) })
            {
                var f = Path.Combine(dest, name);
                if (!File.Exists(f)) continue;
                var b = File.ReadAllBytes(f);
                // ... <언어 1바이트> BC 1A <이름 UTF-16> — 녹화된 트레이너(Player, 영어=2)일 때만
                if (b.Length <= nameAt + 12 || b[nameAt - 2] != 0xBC || b[nameAt - 1] != 0x1A || b[nameAt - 3] >= 0x80
                    || System.Text.Encoding.Unicode.GetString(b, nameAt, 12) != "Player")
                { note = "이 PokeLDN 버전의 Z-A 트레이너 정보가 달라 국적을 바꾸지 못했습니다"; return []; }
                b[nameAt - 3] = (byte)lang;
                File.WriteAllBytes(f, b);
            }
            args.AddRange(["--game-dir", dest]);
        }
        return args;
    }

    private static string FindData(string pokeldnPath, string sub)
    {
        var p = pokeldnPath.Trim().Trim('"');
        var root = File.Exists(p) ? Path.GetDirectoryName(p) : Directory.Exists(p) ? p : null;
        if (root == null) return null;
        foreach (var start in new[] { root, Path.Combine(root, "_internal") })
        {
            var direct = Path.Combine(start, "pokeldn", sub);
            if (Directory.Exists(direct)) return direct;
        }
        try
        {
            var marker = Path.Combine("pokeldn", sub);
            return Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .FirstOrDefault(d => d.EndsWith(marker, StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }
}
