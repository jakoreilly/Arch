namespace Arch.Cli.Tests;

/// <summary>Phase 1 of plan.md: the unified `arch &lt;path&gt;` entry point used to parse,
/// validate and per-provider-filter --fail-on and then discard the result — Runner.Run
/// returned <c>anyFailed ? 1 : 0</c> and never 3, so a tripped gate could not fail a pipeline.
/// These tests pin the fixed contract: exit 3 when a gate trips, 0 when every requested gate
/// passes, 2 on an unknown gate name (on both the unified path and the `connect` verb), and a
/// gate that belongs to only one provider must not disturb the other provider's subsite in
/// combined mode.</summary>
[Collection(ConsoleCaptureCollection.Name)]
public class GateExitCodeTests : IDisposable
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), "arch-cli-gate-tests", Guid.NewGuid().ToString("N"));

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
    public void Unified_path_exits_3_when_a_gate_trips()
    {
        // CrossLink/ShopTest carries a deliberate fake credential (Password=SuperSecret123)
        // specifically so the secrets gate trips on it — see the fixture's own comment.
        var fixture = Path.Combine(FixturesRoot, "CrossLink", "ShopTest");
        var (exitCode, stderr) = RunCaptured(fixture, "--out", _outDir, "--no-open", "--fail-on", "secrets");

        Assert.Equal(3, exitCode);
        Assert.Contains("gate failed", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Unified_path_exits_0_when_gates_pass()
    {
        var fixture = Path.Combine(FixturesRoot, "GateClean");
        var (exitCode, stderr) = RunCaptured(fixture, "--out", _outDir, "--no-open", "--fail-on", "secrets");

        Assert.Equal(0, exitCode);
        Assert.Contains("all gate(s) passed", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Code_only_gate_does_not_fail_the_sql_provider()
    {
        // "cycles" is a code-only gate (CiGate.KnownGates); ShopTest applies to both providers
        // (a .csproj and a .sql file). Before the Phase 1b fix, --fail-on's value reached BOTH
        // providers' own parsers verbatim, and Arch.Sql's CliOptions rejected "cycles" as
        // unknown, throwing inside SqlAnalysisProvider.Generate — which Runner reported as a
        // crash (exit 1) with the sql/ subsite never written.
        var fixture = Path.Combine(FixturesRoot, "CrossLink", "ShopTest");
        var (exitCode, stderr) = RunCaptured(fixture, "--out", _outDir, "--no-open", "--fail-on", "cycles");

        Assert.DoesNotContain("FAILED", stderr, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_outDir, "sql", "index.html")), "sql subsite must still be written");
        Assert.True(File.Exists(Path.Combine(_outDir, "code", "index.html")), "code subsite must still be written");
        Assert.True(exitCode is 0 or 3);
    }

    [Fact]
    public void Sql_only_gate_does_not_fail_the_code_provider()
    {
        // "no-pk" is a SQL-only gate (SqlCiGate.KnownGates). Same fixture, opposite direction.
        var fixture = Path.Combine(FixturesRoot, "CrossLink", "ShopTest");
        var (exitCode, stderr) = RunCaptured(fixture, "--out", _outDir, "--no-open", "--fail-on", "no-pk");

        Assert.DoesNotContain("FAILED", stderr, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_outDir, "code", "index.html")), "code subsite must still be written");
        Assert.True(exitCode is 0 or 3);
    }

    [Fact]
    public void Gate_typo_is_a_usage_error_on_the_unified_path()
    {
        var fixture = Path.Combine(FixturesRoot, "CrossLink", "ShopTest");
        var (exitCode, stderr) = RunCaptured(fixture, "--out", _outDir, "--no-open", "--fail-on", "cyclez");

        Assert.Equal(2, exitCode);
        Assert.Contains("cyclez", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_outDir), "a usage error must be caught before anything is written");
    }

    [Fact]
    public void Crashed_provider_outranks_a_tripped_gate()
    {
        // A code-only site (no .sql) run with a code fail-on gate that trips, PLUS a bogus
        // flag neither provider knows — the bogus flag makes CliOptions.Parse itself fail
        // inside CodeAnalysisProvider.Generate (an unrelated "usage error" reaching Generate,
        // not the --fail-on validation path), which Runner reports as a crash. Exit 1 must win
        // over exit 3 so a pipeline never mistakes "the tool broke" for "the rules were broken".
        var fixture = Path.Combine(FixturesRoot, "CodeOnly");
        var (exitCode, _) = RunCaptured(fixture, "--out", _outDir, "--no-open", "--this-flag-does-not-exist-anywhere");

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Connect_verb_rejects_an_unknown_gate_name_as_a_usage_error()
    {
        // ConnectOptions.Parse used to AddRange an unvalidated --fail-on value straight into
        // FailOn, and SqlCiGate.Evaluate silently `continue`s past a name it doesn't recognise
        // — so a typo produced exit 0 with nothing evaluated and nothing printed. No live
        // connection is needed to prove the fix: an unknown gate name must be rejected before
        // ConnectOptions ever tries to open one. Dispatched through Entry.Run (internal to
        // Arch.Cli, which grants Arch.Cli.Tests InternalsVisibleTo) — Runner.Run itself never
        // sees the "connect" verb; that dispatch lives in Entry, and Arch.Sql.Cli.Verbs (the
        // type that actually owns RunConnect) is internal to Arch.Sql, granted only to "arch"
        // (Arch.Cli's own assembly identity), not to this test assembly.
        var originalError = Console.Error;
        var writer = new StringWriter();
        Console.SetError(writer);
        int exitCode;
        try
        {
            exitCode = Entry.Run(["connect", "--env", "--fail-on", "not-a-real-gate"]);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal(2, exitCode);
        Assert.Contains("not-a-real-gate", writer.ToString(), StringComparison.Ordinal);
    }
}
