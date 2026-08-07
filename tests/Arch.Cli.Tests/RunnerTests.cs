using System.Text.RegularExpressions;

namespace Arch.Cli.Tests;

/// <summary>Exercises Runner.Run in-process. All tests pass --no-open (Runner would
/// otherwise launch a real browser) and an explicit --out under a per-test temp dir, so
/// nothing here touches the repo or leaks into the working directory. Console.Error is
/// captured for the assertions that need it — this class runs its tests sequentially
/// (xunit's default within one class), which is what makes that safe. Every other class
/// that captures Console.Error joins the same collection, so they serialise against this
/// one too rather than interleaving on a process-global stream.</summary>
[Collection(ConsoleCaptureCollection.Name)]
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

    /// <summary>The hub's per-subsite cards carry that provider's own headline figures, so the
    /// landing page says what was found and not just that something was. Counts are pluralised
    /// against the value ("1 project", not "1 projects").</summary>
    [Fact]
    public void Combined_fixture_hub_cards_carry_each_providers_headline_figures()
    {
        RunCaptured(Path.Combine(FixturesRoot, "Combined"), "--out", _outDir, "--no-open");
        var hub = File.ReadAllText(Path.Combine(_outDir, "index.html"));

        Assert.Equal(2, Regex.Matches(hub, "class=\"hub-card\"").Count);
        Assert.Equal(2, Regex.Matches(hub, "class=\"hub-stats\"").Count);
        // Code-side nouns and sql-side nouns both appear, each from its own model.
        Assert.Matches(@"<b>\d+</b> files", hub);
        Assert.Matches(@"<b>\d+</b> (types|type)", hub);
        Assert.Matches(@"<b>\d+</b> (tables|table)", hub);
        Assert.Matches(@"<b>\d+</b> (foreign keys|foreign key)", hub);
        Assert.DoesNotContain("<b>1</b> projects", hub);
    }

    /// <summary>The hub reports the same cross-layer join the code site's Packages page shows,
    /// but with a hub-relative href — SqlCrossLink.Href is deliberately relative to the code
    /// site ("../sql/..."), which would be a broken link one level up.</summary>
    [Fact]
    public void Hub_reports_the_cross_layer_join_with_hub_relative_links()
    {
        RunCaptured(Path.Combine(FixturesRoot, "CrossLink", "ShopTest"), "--out", _outDir, "--no-open");
        var hub = File.ReadAllText(Path.Combine(_outDir, "index.html"));

        Assert.Contains("Code ↔ SQL", hub);
        Assert.Contains("1 of 2 matched", hub);
        Assert.Contains("href=\"sql/objects.html?catalog=SHOPTEST\"", hub);
        Assert.DoesNotContain("href=\"../sql/", hub);
        Assert.Contains("matched by name", hub);
        Assert.Contains("not in this scan", hub);
    }

    /// <summary>No databases in the code model means the join never ran, so the panel is absent
    /// entirely — an empty one would claim the join ran and found nothing.</summary>
    [Fact]
    public void Hub_omits_the_cross_layer_panel_when_the_code_side_references_no_database()
    {
        RunCaptured(Path.Combine(FixturesRoot, "Combined"), "--out", _outDir, "--no-open");
        var hub = File.ReadAllText(Path.Combine(_outDir, "index.html"));

        Assert.DoesNotContain("Code ↔ SQL", hub);
        Assert.DoesNotContain("hub-dbs", hub);
    }

    [Fact]
    public void Combined_fixture_hub_and_subsites_all_carry_the_shared_assets()
    {
        RunCaptured(Path.Combine(FixturesRoot, "Combined"), "--out", _outDir, "--no-open");

        Assert.True(File.Exists(Path.Combine(_outDir, "assets", "site.css")));
        Assert.True(File.Exists(Path.Combine(_outDir, "code", "assets", "site.css")));
        Assert.True(File.Exists(Path.Combine(_outDir, "sql", "assets", "site.css")));
    }

    /// <summary>End-to-end through the real CLI (Runner.Run, not CrossLink directly): the
    /// fixture's own folder is named "ShopTest", one detected database's catalog is "SHOPTEST"
    /// (same name, different case — the Phase 6 DoD's case-mismatch check) and the sql side is a
    /// plain file scan, so this can only ever be an unverified, catalog-name-only match. A second
    /// database ("OtherDb") has no matching catalog in this scan at all, exercising the "not in
    /// this scan" branch in the same run. A fake credential is embedded on purpose to prove the
    /// generated site never renders it.</summary>
    [Fact]
    public void Combined_fixture_with_databases_renders_both_join_outcomes_and_leaks_no_secret()
    {
        var (exitCode, _) = RunCaptured(Path.Combine(FixturesRoot, "CrossLink", "ShopTest"), "--out", _outDir, "--no-open");
        Assert.Equal(0, exitCode);

        var packages = File.ReadAllText(Path.Combine(_outDir, "code", "packages.html"));
        Assert.Contains("matched by name only (no server recorded in this connection string)", packages);
        Assert.Contains("objects →", packages);
        Assert.Contains("../sql/objects.html?catalog=SHOPTEST", packages);
        Assert.Contains("not in this scan", packages);
        Assert.Contains("arch connect", packages);

        var allText = string.Join('\n', Directory.EnumerateFiles(_outDir, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));
        Assert.DoesNotMatch("(?i)password=|pwd=", allText);
    }
}
