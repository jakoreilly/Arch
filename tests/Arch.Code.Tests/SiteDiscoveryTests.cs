using System.Text.Json;
using Arch.Code.Graph;
using Arch.Code.Landscape;

namespace Arch.Code.Tests;

public class SiteDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "archdiagram-discovery-tests", Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions WriteOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private void WriteSite(string folder, string rootName) => WriteModel(Path.Combine(_root, folder), rootName);

    /// <summary>An `arch` combined-mode site: the code model one level down under code/, a
    /// SqlModel-shaped sibling under sql/ that must never be picked up as a ProjectModel, and
    /// the hub at the folder root.</summary>
    private void WriteCombinedSite(string folder, string rootName)
    {
        var dir = Path.Combine(_root, folder);
        WriteModel(Path.Combine(dir, "code"), rootName);
        Directory.CreateDirectory(Path.Combine(dir, "sql"));
        File.WriteAllText(Path.Combine(dir, "sql", "model.json"), """{"objects":[],"schemaVersion":1}""");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "index.html"), "<html></html>");
    }

    private static void WriteModel(string dir, string rootName)
    {
        Directory.CreateDirectory(dir);
        var model = new ProjectModel { RootName = rootName, SourcePath = "x" };
        File.WriteAllText(Path.Combine(dir, "model.json"), JsonSerializer.Serialize(model, WriteOptions));
    }

    [Fact]
    public void Discover_without_filter_returns_all_sites()
    {
        WriteSite("site-a", "a");
        WriteSite("site-b", "b");
        WriteSite("site-c", "c");

        var sites = SiteDiscovery.Discover(_root, Path.Combine(_root, "site-landscape"), new List<string>());

        Assert.Equal(new[] { "site-a", "site-b", "site-c" }, sites.Select(s => s.Id).OrderBy(x => x));
    }

    [Fact]
    public void Discover_with_only_filters_to_named_subset()
    {
        WriteSite("site-a", "a");
        WriteSite("site-b", "b");
        WriteSite("site-c", "c");

        var only = new HashSet<string>(new[] { "site-a", "site-c" }, StringComparer.OrdinalIgnoreCase);
        var sites = SiteDiscovery.Discover(_root, Path.Combine(_root, "site-landscape"), new List<string>(), only);

        Assert.Equal(new[] { "site-a", "site-c" }, sites.Select(s => s.Id).OrderBy(x => x));
    }

    [Fact]
    public void Discover_finds_the_code_model_of_an_arch_combined_mode_site()
    {
        WriteSite("site-a", "a");
        WriteCombinedSite("site-both", "both");

        var sites = SiteDiscovery.Discover(_root, Path.Combine(_root, "site-landscape"), new List<string>());

        Assert.Equal(new[] { "site-a", "site-both" }, sites.Select(s => s.Id).OrderBy(x => x));
        Assert.Equal("both", sites.Single(s => s.Id == "site-both").Model.RootName);
    }

    [Fact]
    public void Discover_points_a_combined_mode_site_at_its_hub_not_its_code_subsite()
    {
        WriteCombinedSite("site-both", "both");

        var sites = SiteDiscovery.Discover(_root, Path.Combine(_root, "site-landscape"), new List<string>());

        Assert.Equal("../site-both/index.html", sites.Single().IndexHref);
    }

    [Fact]
    public void Discover_prefers_a_root_model_over_a_nested_one()
    {
        // A folder that is both: a root model.json wins, so an existing single-provider site
        // that happens to have a code/ subfolder keeps behaving exactly as it did before.
        WriteSite("site-a", "root-wins");
        WriteModel(Path.Combine(_root, "site-a", "code"), "nested-loses");

        var sites = SiteDiscovery.Discover(_root, Path.Combine(_root, "site-landscape"), new List<string>());

        Assert.Equal("root-wins", sites.Single().Model.RootName);
    }
}
