namespace Arch.Code.Tests;

public static class FixturePaths
{
    public static string SampleRepo =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "SampleRepo");

    /// <summary>A deployment-shaped tree — appsettings overlays, launchSettings, a Dockerfile and
    /// a compose file — for the ops/network scan. Deliberately separate from SampleRepo, which
    /// tools/golden.sh baselines byte-for-byte; adding these files there would churn the golden
    /// tree for every unrelated future change.</summary>
    public static string OpsSample =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "OpsSample");

    /// <summary>Attribute-routed, convention-routed and data-access-shaped source for
    /// RouteScanner/DataAccessScanner. Deliberately separate from SampleRepo, which
    /// tools/golden.sh baselines byte-for-byte.</summary>
    public static string RouteSample =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "RouteSample");

    /// <summary>A high-complexity method carrying a hardcoded password and an API-key-shaped
    /// literal, for SecretScrub's end-to-end check (FilePage.AppendSnippet is the one place raw
    /// source reaches the site). Deliberately separate from SampleRepo, which tools/golden.sh
    /// baselines byte-for-byte.</summary>
    public static string SecretSample =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "SecretSample");
}
