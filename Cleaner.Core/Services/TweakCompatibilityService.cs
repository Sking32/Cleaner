using Cleaner.Core.Models;

namespace Cleaner.Core.Services;

public readonly record struct TweakCompatibilityResult(bool CanApply, bool Verified, string MessageKey);

/// <summary>Declared OS constraints, not a promise that a tweak improves performance.</summary>
public static class TweakCompatibilityService
{
    public static TweakCompatibilityResult Evaluate(TweakDefinition tweak, int? windowsBuild = null, bool? isWindows = null, bool expertMode = false)
    {
        var build = windowsBuild ?? Environment.OSVersion.Version.Build;
        if (!(isWindows ?? OperatingSystem.IsWindows())) return new(false, false, "compatibility.windows_required");
        var compatibility = tweak.Compatibility;
        var min = compatibility?.MinWindowsBuild;
        if (min.HasValue && build < min.Value || compatibility?.MaxWindowsBuild is int max && build > max)
            return new(false, false, "compatibility.unsupported");
        var verified = compatibility?.VerifiedWindowsBuilds.Contains(build) == true;
        if (RequiresExpert(tweak) && !expertMode) return new(false, verified, "compatibility.expert_required");
        return new(true, verified, verified ? "compatibility.verified" : "compatibility.unverified");
    }

    public static bool RequiresExpert(TweakDefinition tweak) => tweak.ExpertOnly || tweak.Level is "caution" or "danger";
}
