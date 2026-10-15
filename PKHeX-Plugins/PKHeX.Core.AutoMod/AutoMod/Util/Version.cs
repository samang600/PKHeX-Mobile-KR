using System;
using System.Diagnostics;
using System.Reflection;

namespace PKHeX.Core.AutoMod;

public static class ALMVersion
{
    private static readonly Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
    public static readonly AssemblyVersions Versions = new();

    public record AssemblyVersions
    {
        public readonly Version? AlmVersionCurrent = GetCurrentVersion("PKHeX.Core.AutoMod");

        public readonly Version? CoreVersionCurrent = GetCurrentVersion("PKHeX.Core");
        // PKHeXKR: 최신 PKHeX 버전 확인(인터넷)은 하지 않음 — 앱·커스텀 빌드는 PKHeX.Core와 ALM을 같은 소스로 함께 빌드
        public readonly Version? CoreVersionLatest = null;
    }

    /// <summary>
    /// Checks for plugin mismatch. If "EnableDevMode" is enabled it will allow a user to skip update warnings until the next release. Will otherwise check plugin mismatch for current versions.
    /// </summary>
    /// <returns>True if a plugin mismatch is found. False if any of the versions are null or no mismatch found.</returns>
    /// <remarks>PKHeXKR: PKHeX.Core 개발 버전(예: 26.10.13)과 ALM(26.8.26)을 함께 빌드해 쓰므로 버전 번호가 달라도 불일치로 보지 않음
    /// (원래는 "PKHeX and Plugins have a version mismatch" 오류로 자동 합법화가 전부 막힘)</remarks>
    public static bool GetIsMismatch() => false;

    public static bool GetIsMismatch(Version? currentCore, Version? currentALM, Version? latestCore)
    {
        if (currentCore is null || currentALM is null || latestCore is null)
            return false;

        var latestAllowed = new Version(APILegality.LatestAllowedVersion);
        return (APILegality.EnableDevMode && (latestCore > latestAllowed) && (latestCore > currentCore)) || (!APILegality.EnableDevMode && (currentCore > currentALM));
    }

    /// <summary>
    /// Gets the current version of the specified assembly.
    /// </summary>
    /// <returns>A version representing the current version of the specified assembly, or null if the assembly cannot be found or has no version available.</returns>
    private static Version? GetCurrentVersion(string assemblyName)
    {
        var assembly = Array.Find(assemblies, x => x.GetName().Name == assemblyName);
        return assembly?.GetName().Version;
    }

    private static Version? GetLatestCoreVersion()
    {
        try
        {
            return UpdateUtil.GetLatestPKHeXVersion();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            return null;
        }
    }
}
