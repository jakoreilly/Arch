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

    /// <summary>A SQL-only site writes a SqlModel to the same root path a code site uses, and both
    /// call their top-level array "files". Before this was named explicitly it failed deep inside
    /// deserialization with "FileNode was missing required properties including 'language'" — a
    /// message that reads like a corrupt file rather than the ordinary situation it is. `arch group`
    /// makes it common, since one SQL-only repo in a set of ten is unremarkable.</summary>
    [Fact]
    public void Discover_skips_a_sql_only_site_with_an_explanatory_diagnostic()
    {
        WriteSite("site-a", "a");
        Directory.CreateDirectory(Path.Combine(_root, "site-db"));
        File.WriteAllText(Path.Combine(_root, "site-db", "model.json"), """
            { "rootName": "db", "files": [ { "relPath": "x.sql", "slug": "x_sql" } ], "objects": [], "foreignKeys": [] }
            """);

        var diagnostics = new List<string>();
        var sites = SiteDiscovery.Discover(_root, Path.Combine(_root, "site-landscape"), diagnostics);

        Assert.Equal(new[] { "site-a" }, sites.Select(s => s.Id));
        var note = Assert.Single(diagnostics);
        Assert.Contains("site-db", note, StringComparison.Ordinal);
        Assert.Contains("SQL-only", note, StringComparison.Ordinal);
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

    /// <summary>Phase 6 of plan.md: a model.json from a NEWER Arch than this one may have added a
    /// required property — deserializing it directly would fail deep inside with a missing-
    /// property exception that reads like file corruption, not the ordinary version-skew
    /// situation it is. The check must happen BEFORE JsonSerializer.Deserialize ever runs.</summary>
    [Fact]
    public void Discover_skips_a_too_new_schema_version_with_an_explanatory_diagnostic()
    {
        WriteSite("site-a", "a");
        Directory.CreateDirectory(Path.Combine(_root, "site-future"));
        File.WriteAllText(Path.Combine(_root, "site-future", "model.json"),
            $$"""{"rootName":"future","sourcePath":"x","schemaVersion":{{ProjectModel.CurrentSchemaVersion + 1}}}""");

        var diagnostics = new List<string>();
        var sites = SiteDiscovery.Discover(_root, Path.Combine(_root, "site-landscape"), diagnostics);

        Assert.Equal(new[] { "site-a" }, sites.Select(s => s.Id));
        var note = Assert.Single(diagnostics);
        Assert.Contains("site-future", note, StringComparison.Ordinal);
        Assert.Contains("schema v", note, StringComparison.Ordinal);
    }

    /// <summary>A model.json with no "schemaVersion" key at all (every file written before the
    /// field existed) must be treated as v1 — never rejected, and never silently upgraded to
    /// claim the current version either.</summary>
    [Fact]
    public void Discover_accepts_a_model_with_no_schema_version_field()
    {
        WriteSite("site-a", "a"); // WriteModel serializes a real ProjectModel — schemaVersion IS present
        Directory.CreateDirectory(Path.Combine(_root, "site-old"));
        File.WriteAllText(Path.Combine(_root, "site-old", "model.json"), """{"rootName":"old","sourcePath":"x"}""");

        var diagnostics = new List<string>();
        var sites = SiteDiscovery.Discover(_root, Path.Combine(_root, "site-landscape"), diagnostics);

        Assert.Equal(new[] { "site-a", "site-old" }, sites.Select(s => s.Id).OrderBy(x => x));
        Assert.Empty(diagnostics);
    }

    /// <summary>Phase 8 of plan.md: DiscoverSqlSites finds exactly the sites Discover itself
    /// skips (SQL-only, no ProjectModel) and extracts Server/Catalog/ObjectCount/RootName from
    /// the model.json fields Arch.Sql already writes — no new sidecar file, no Arch.Code
    /// reference to Arch.Sql.</summary>
    [Fact]
    public void DiscoverSqlSites_finds_only_the_sql_only_sites_and_reads_their_facts()
    {
        WriteSite("site-a", "a");
        Directory.CreateDirectory(Path.Combine(_root, "site-db"));
        File.WriteAllText(Path.Combine(_root, "site-db", "model.json"), """
            { "rootName": "Orders", "server": "sql-prod-01", "catalog": "Orders",
              "objects": [ {}, {}, {} ], "foreignKeys": [] }
            """);

        var sqlSites = SiteDiscovery.DiscoverSqlSites(_root, Path.Combine(_root, "site-landscape"));

        var found = Assert.Single(sqlSites);
        Assert.Equal("site-db", found.Id);
        Assert.Equal("sql-prod-01", found.Server);
        Assert.Equal("Orders", found.Catalog);
        Assert.Equal(3, found.ObjectCount);
    }

    [Fact]
    public void DiscoverSqlSites_ignores_ordinary_code_sites()
    {
        WriteSite("site-a", "a");

        var sqlSites = SiteDiscovery.DiscoverSqlSites(_root, Path.Combine(_root, "site-landscape"));

        Assert.Empty(sqlSites);
    }
}
