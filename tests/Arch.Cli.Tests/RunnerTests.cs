namespace Arch.Cli.Tests;

/// <summary>Exercises Runner.Run in-process. All tests pass --no-open (Runner would
/// otherwise launch a real browser) and an explicit --out under a per-test temp dir, so
/// nothing here touches the repo or leaks into the working directory. Console.Error is
/// captured for the assertions that need it — this class runs its tests sequentially
/// (xunit's default within one class), which is what makes that safe; a second class
/// capturing Console.Error in this assembly would race with this one.</summary>
public class RunnerTests : IDisposable
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), "arch-cli-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_outDir, recursive: true); } catch { }
    }

    private (int ExitCode, string Stderr) RunCaptured(params string[] args)
    {
        var originalError = Console.Error;
        var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            var exitCode = Runner.Run(args);
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void No_args_exits_2()
    {
        var (exitCode, stderr) = RunCaptured();
        Assert.Equal(2, exitCode);
        Assert.Contains("Usage: arch", stderr);
    }

    [Fact]
    public void Help_flag_exits_0()
    {
        var (exitCode, _) = RunCaptured("-h");
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Nonexistent_directory_exits_2()
    {
        var (exitCode, stderr) = RunCaptured(Path.Combine(_outDir, "does-not-exist"));
        Assert.Equal(2, exitCode);
        Assert.Contains("is not a directory", stderr);
    }

    [Fact]
    public void Unknown_flag_as_first_argument_exits_2()
    {
        var (exitCode, _) = RunCaptured("--bogus");
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Empty_folder_prints_the_empty_state_verbatim_and_exits_2()
    {
        var (exitCode, stderr) = RunCaptured(Path.Combine(FixturesRoot, "Empty"), "--no-open");

        Assert.Equal(2, exitCode);
        Assert.Contains("Nothing to analyze here.", stderr);
        Assert.Contains("Arch looks for source files (C#, TypeScript, Python, Go, Java, Rust, …) and", stderr);
        Assert.Contains("SQL scripts (*.sql). This folder has neither.", stderr);
        Assert.Contains("Pointing at the right folder?   arch c:\\path\\to\\your\\project", stderr);
        Assert.Contains("Analyzing a live database?      arch connect --conn-file db.json", stderr);
        Assert.Contains("Just want to see it work?       arch --demo", stderr);
        // Three next-steps, each led by the bullet glyph.
        Assert.Equal(3, stderr.Split('\u2022').Length - 1);
    }

    [Fact]
    public void Code_only_fixture_is_a_pass_through_with_no_subfolder()
    {
        var (exitCode, _) = RunCaptured(Path.Combine(FixturesRoot, "CodeOnly"), "--out", _outDir, "--no-open");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(_outDir, "index.html")), "expected outDir/index.html directly, no code/ subfolder");
        Assert.False(Directory.Exists(Path.Combine(_outDir, "code")), "single-provider mode must not nest into a subfolder");
        Assert.False(Directory.Exists(Path.Combine(_outDir, "sql")));
    }

    [Fact]
    public void Sql_only_fixture_is_a_pass_through_with_no_subfolder()
    {
        var (exitCode, _) = RunCaptured(Path.Combine(FixturesRoot, "SqlOnly"), "--out", _outDir, "--no-open");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(_outDir, "index.html")), "expected outDir/index.html directly, no sql/ subfolder");
        Assert.False(Directory.Exists(Path.Combine(_outDir, "code")));
        Assert.False(Directory.Exists(Path.Combine(_outDir, "sql")), "single-provider mode must not nest into a subfolder");
    }

    [Fact]
    public void Combined_fixture_produces_a_hub_page_linking_both_subsites()
    {
        var (exitCode, stderr) = RunCaptured(Path.Combine(FixturesRoot, "Combined"), "--out", _outDir, "--no-open");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(_outDir, "index.html")), "hub page missing");
        Assert.True(File.Exists(Path.Combine(_outDir, "code", "index.html")), "nested code site missing");
        Assert.True(File.Exists(Path.Combine(_outDir, "sql", "index.html")), "nested sql site missing");

        var hub = File.ReadAllText(Path.Combine(_outDir, "index.html"));
        Assert.Contains("href=\"code/index.html\"", hub);
        Assert.Contains("href=\"sql/index.html\"", hub);
        Assert.Contains("→", stderr); // final "location of the site" line
    }

    [Fact]
    public void Combined_fixture_hub_and_subsites_all_carry_the_shared_assets()
    {
        RunCaptured(Path.Combine(FixturesRoot, "Combined"), "--out", _outDir, "--no-open");

        Assert.True(File.Exists(Path.Combine(_outDir, "assets", "site.css")));
        Assert.True(File.Exists(Path.Combine(_outDir, "code", "assets", "site.css")));
        Assert.True(File.Exists(Path.Combine(_outDir, "sql", "assets", "site.css")));
    }
}
