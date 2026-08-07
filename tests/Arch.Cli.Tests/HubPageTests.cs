using Arch.Cli;

namespace Arch.Cli.Tests;

/// <summary>The hub is the only page `golden.sh` structurally cannot see — it runs each analyser's
/// own exe, and neither of those ever writes a hub. These cover the rule every panel follows:
/// render nothing at all rather than an empty shell.</summary>
public class HubPageTests : IDisposable
{
    private readonly string _dir;

    public HubPageTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "arch-hub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteHub(
        IReadOnlyList<HubPage.Link>? links = null,
        IReadOnlyList<HubPage.Action>? actions = null,
        string owner = "",
        IReadOnlyList<string>? capabilities = null)
    {
        HubPage.Write(_dir, "Demo",
            links ?? [new HubPage.Link("code", "Code Analysis", "◈", "12 C# files")],
            "C:/src/demo", "2026-01-01", [],
            actions ?? [], owner, capabilities ?? []);
        return File.ReadAllText(Path.Combine(_dir, "index.html"));
    }

    [Fact]
    public void A_minimal_run_renders_no_empty_panels()
    {
        var html = WriteHub();

        Assert.DoesNotContain("Health at a glance", html, StringComparison.Ordinal);
        Assert.DoesNotContain("What to do first", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Jump straight to", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Owned by", html, StringComparison.Ordinal);
        // ...but the parts that are always true are still there.
        Assert.Contains("Code Analysis", html, StringComparison.Ordinal);
        Assert.Contains("C:/src/demo", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_graded_link_renders_the_badge_once_per_surface_and_links_to_its_scorecard()
    {
        var html = WriteHub([
            new HubPage.Link("code", "Code Analysis", "◈", "12 C# files", [],
                             "AT RISK", "danger", [new HubPage.Page("Scorecard", "scorecard.html")],
                             "4 of 7 signals need attention"),
        ]);

        Assert.Contains("Health at a glance", html, StringComparison.Ordinal);
        Assert.Contains("4 of 7 signals need attention", html, StringComparison.Ordinal);
        Assert.Contains("code/scorecard.html", html, StringComparison.Ordinal);
        Assert.Contains("Jump straight to", html, StringComparison.Ordinal);
    }

    /// <summary>`.note` is a bordered callout with its own background and margins. Inlining it as a
    /// trailing attribution rendered a grey box mid-sentence — the same trap the Ops page hit with
    /// .note inside a table cell. This pins the muted-text class instead.</summary>
    [Fact]
    public void Action_attribution_uses_the_inline_class_not_the_callout()
    {
        var html = WriteHub(actions:
        [
            new HubPage.Action("high", "danger", "Dependency cycle in Foo", "code/modules.html", "Code Analysis"),
        ]);

        Assert.Contains("hub-action-src\">Code Analysis", html, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"note\" style=\"font-size:.8rem\"", html, StringComparison.Ordinal);
        Assert.Contains("code/modules.html", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Owner_and_capabilities_render_when_a_sidecar_supplied_them()
    {
        var html = WriteHub(owner: "Platform Team", capabilities: ["Billing", "Reporting"]);

        Assert.Contains("Owned by Platform Team", html, StringComparison.Ordinal);
        Assert.Contains("Billing", html, StringComparison.Ordinal);
        Assert.Contains("Reporting", html, StringComparison.Ordinal);
    }

    /// <summary>The hub links out to subsites and must never assume one exists. A provider that
    /// failed is simply absent from links, and nothing on the page may reference it.</summary>
    [Fact]
    public void A_provider_that_did_not_run_is_referenced_nowhere()
    {
        var html = WriteHub([new HubPage.Link("code", "Code Analysis", "◈", "12 C# files")]);

        Assert.DoesNotContain("sql/", html, StringComparison.Ordinal);
    }
}
