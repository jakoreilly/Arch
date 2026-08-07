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
}
