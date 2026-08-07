using Arch.Code;
using Arch.Code.Analysis;
using Arch.Code.Cli;
using Arch.Code.Graph;
using Arch.Code.Site.Pages;

namespace Arch.Code.Tests;

/// <summary>The capability map is the one part of the site that is asserted rather than inferred,
/// so the tests are about two things: that an author's claim survives into the model intact, and
/// that the scan checks it rather than repeating it back.</summary>
public class CapabilityTests
{
    private static readonly ProjectModel Model =
        Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.OpsSample, Open = false });

    private static readonly string Brief = BriefPage.Body(Model, "2026-01-01");

    // ---- Path matching ----

    [Theory]
    [InlineData("src/Claims/Intake.cs", "src/Claims/", true)]
    [InlineData("src/Claims/Intake.cs", "src/Claims", true)]
    [InlineData("src/Claims.cs", "src/Claims.cs", true)]
    // The trap: a prefix that is not a path segment must not match, or one capability quietly
    // swallows another's code and the unattributed figure — the honesty check — goes wrong.
    [InlineData("src/Claims/Intake.cs", "src/Claim", false)]
    [InlineData("src/ClaimsExtra/X.cs", "src/Claims", false)]
    [InlineData("src/Claims/Intake.cs", "", false)]
    public void Capability_paths_match_on_whole_segments(string relPath, string capPath, bool expected) =>
        Assert.Equal(expected, CapabilityRollup.PathMatches(relPath, capPath));

    // ---- Loading ----

    [Fact]
    public void The_sidecars_owner_and_capabilities_reach_the_model()
    {
        Assert.Equal("Commerce Platform", Model.Owner);
        Assert.Equal(["Checkout", "Reporting"], Model.Capabilities.Select(c => c.Name));

        var checkout = Model.Capabilities[0];
        Assert.Equal("Payments Squad", checkout.Owner);
        Assert.Equal("critical", checkout.Criticality);
        Assert.Equal("PCI", checkout.DataClassification);
    }

    /// <summary>Declaration order is preserved: authors list the important capability first, and
    /// re-sorting alphabetically would throw that signal away.</summary>
    [Fact]
    public void Capabilities_keep_their_declared_order()
    {
        var doc = """
        {"capabilities":[{"name":"Zulu","paths":["z/"]},{"name":"Alpha","paths":["a/"]}]}
        """;
        var authored = LoadInline(doc, out _);
        Assert.Equal(["Zulu", "Alpha"], authored.Capabilities.Select(c => c.Name));
    }

    [Fact]
    public void A_capability_with_no_name_is_skipped_with_a_diagnostic()
    {
        var authored = LoadInline("""{"capabilities":[{"paths":["a/"]},{"name":"Real","paths":["b/"]}]}""", out var diags);

        Assert.Equal(["Real"], authored.Capabilities.Select(c => c.Name));
        Assert.Contains(diags, d => d.Contains("capability #1 has no name"));
    }

    [Fact]
    public void An_unrecognised_criticality_is_kept_verbatim_and_diagnosed()
    {
        var authored = LoadInline("""{"capabilities":[{"name":"X","criticality":"URGENT","paths":["a/"]}]}""", out var diags);

        // Kept, not dropped — the sidecar is a human document and a typo must not erase intent.
        Assert.Equal("urgent", authored.Capabilities[0].Criticality);
        Assert.Contains(diags, d => d.Contains("is not one of"));
    }

    [Fact]
    public void Author_supplied_paths_are_normalised_like_file_keys()
    {
        var authored = LoadInline("""{"capabilities":[{"name":"X","paths":["./src\\Claims/","src/Other"]}]}""", out _);
        Assert.Equal(["src/Claims/", "src/Other"], authored.Capabilities[0].Paths);
    }

    [Fact]
    public void A_sidecar_with_no_capabilities_leaves_the_model_empty_rather_than_inventing_a_map()
    {
        var model = Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.SampleRepo, Open = false });

        Assert.Empty(model.Capabilities);
        Assert.Equal(0, model.UnattributedFileCount);
        Assert.DoesNotContain("Capabilities", BriefPage.Body(model, "2026-01-01"));
    }

    // ---- Roll-up ----

    [Fact]
    public void Scanned_figures_are_rolled_up_against_the_authored_paths()
    {
        var checkout = Model.Capabilities.Single(c => c.Name == "Checkout");
        Assert.True(checkout.FileCount > 0, "the Api/ path should have matched real fixture files");
        Assert.True(checkout.Loc > 0);
    }

    /// <summary>A capability whose paths match nothing is a stale map, and saying so is the most
    /// useful thing this table does — the fixture declares "Reporting" over a folder that does
    /// not exist precisely to hold that behaviour in place.</summary>
    [Fact]
    public void A_capability_matching_no_code_is_reported_rather_than_shown_as_zero()
    {
        var reporting = Model.Capabilities.Single(c => c.Name == "Reporting");
        Assert.Equal(0, reporting.FileCount);
        Assert.Contains("no code matched", Brief);
    }

    [Fact]
    public void Coverage_is_stated_so_a_partial_map_cannot_read_as_complete()
    {
        Assert.True(Model.UnattributedFileCount > 0, "the fixture has first-party files outside Api/");
        Assert.Contains("unattributed", Brief);
        Assert.Contains("attributed to a capability", Brief);
    }

    // ---- Rendering ----

    [Fact]
    public void The_brief_renders_the_capability_table_and_marks_it_authored()
    {
        Assert.Contains("Capabilities", Brief);
        Assert.Contains("authored", Brief);
        Assert.Contains("Payments Squad", Brief);
        Assert.Contains("Commerce Platform", Brief);
        Assert.Contains("Takes a basket to a paid order.", Brief);
    }

    [Fact]
    public void Criticality_maps_onto_the_existing_badge_classes_with_no_new_css()
    {
        Assert.Contains("<span class=\"badge danger\">critical</span>", Brief);
        Assert.Contains("<span class=\"badge ok\">low</span>", Brief);
    }

    private static AuthoredDescriptions LoadInline(string json, out List<string> diagnostics)
    {
        diagnostics = [];
        var path = Path.Combine(Path.GetTempPath(), "arch-caps-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, json);
            return DescriptionsLoader.Load(path, Path.GetTempPath(), diagnostics);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
