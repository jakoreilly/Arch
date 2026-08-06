using Arch.Code.Graph;
using Arch.Sql.Model;

namespace Arch.Cli.Tests;

/// <summary>Exercises CrossLink.Apply's verified (Server+Catalog) branch directly. Nothing
/// reachable through Runner.Run produces this case today: `arch connect` (the only path that
/// builds a SqlModel with Server/Catalog populated) never also scans code, and `arch &lt;path&gt;`
/// never connects live — they are separate, mutually exclusive entry points (see continue.md's
/// Phase 6 findings). CrossLink is internal; this project has InternalsVisibleTo for exactly
/// this test (Arch.Cli.csproj).</summary>
public class CrossLinkTests : IDisposable
{
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), "arch-cli-tests", "crosslink-" + Guid.NewGuid().ToString("N"));
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public void Dispose()
    {
        try { Directory.Delete(_outDir, recursive: true); } catch { }
    }

    private static ProjectModel MinimalCodeModel(params DbNode[] databases) => new()
    {
        RootName = "fixture",
        SourcePath = "fixture",
        Databases = [.. databases],
    };

    private static SqlModel MinimalSqlModel(string server, string catalog, int objectCount) => new()
    {
        RootName = catalog,
        SourcePath = "fixture-db",
        Server = server,
        Catalog = catalog,
        Objects = [.. Enumerable.Range(0, objectCount).Select(i => new DbObject
        {
            Id = $"dbo.T{i}",
            Schema = "dbo",
            Name = $"T{i}",
            Kind = "table",
            Dialect = "tsql",
        })],
    };

    [Fact]
    public void Verified_join_links_and_reports_the_real_object_count()
    {
        var codeModel = MinimalCodeModel(new DbNode { Hash = "h1", Label = "ShopDb", Server = "sql-prod-01", Catalog = "ShopDb" });
        var sqlModel = MinimalSqlModel("sql-prod-01", "ShopDb", objectCount: 12);
        var codeArgs = new[] { FixturesRoot, "--out", _outDir };

        CrossLink.Apply(codeModel, sqlModel, codeArgs, "../sql");

        var packages = File.ReadAllText(Path.Combine(_outDir, "packages.html"));
        Assert.Contains("12 objects in this catalog", packages);
        Assert.Contains("../sql/objects.html?catalog=ShopDb", packages);
        Assert.DoesNotContain("matched by name only", packages);
        Assert.DoesNotContain("not in this scan", packages);
    }

    /// <summary>Same Server/Catalog values a real `arch connect` run against this machine's
    /// AdventureWorks2022 instance actually produces (confirmed by hand: `dotnet run --project
    /// src/Arch.Sql -- connect --conn-file &lt;scratch-file&gt; --out ... --no-open`, then
    /// `grep '"server"\|"catalog"' model.json` → "localhost" / "AdventureWorks2022", no
    /// credential in the output). This test closes the loop between that real connection and the
    /// join logic without a live network call in the test itself.</summary>
    [Fact]
    public void Verified_join_matches_a_real_local_sql_server_connections_server_and_catalog()
    {
        var codeModel = MinimalCodeModel(new DbNode { Hash = "h1", Label = "AdventureWorks2022", Server = "localhost", Catalog = "AdventureWorks2022" });
        var sqlModel = MinimalSqlModel("localhost", "AdventureWorks2022", objectCount: 71);
        var codeArgs = new[] { FixturesRoot, "--out", _outDir };

        CrossLink.Apply(codeModel, sqlModel, codeArgs, "../sql");

        var packages = File.ReadAllText(Path.Combine(_outDir, "packages.html"));
        Assert.Contains("71 objects in this catalog", packages);
        Assert.Contains("../sql/objects.html?catalog=AdventureWorks2022", packages);
    }

    [Fact]
    public void Verified_join_is_case_insensitive_on_both_server_and_catalog()
    {
        var codeModel = MinimalCodeModel(new DbNode { Hash = "h1", Label = "ShopDb", Server = "SQL-PROD-01", Catalog = "SHOPDB" });
        var sqlModel = MinimalSqlModel("sql-prod-01", "ShopDb", objectCount: 3);
        var codeArgs = new[] { FixturesRoot, "--out", _outDir };

        CrossLink.Apply(codeModel, sqlModel, codeArgs, "../sql");

        var packages = File.ReadAllText(Path.Combine(_outDir, "packages.html"));
        Assert.Contains("3 objects in this catalog", packages);
        Assert.DoesNotContain("matched by name only", packages);
    }

    [Fact]
    public void Mismatched_server_falls_back_to_unverified_name_only_match()
    {
        // Same catalog, different server: not a confirmed same-database join.
        var codeModel = MinimalCodeModel(new DbNode { Hash = "h1", Label = "ShopDb", Server = "dev-box", Catalog = "ShopDb" });
        var sqlModel = MinimalSqlModel("sql-prod-01", "ShopDb", objectCount: 5);
        var codeArgs = new[] { FixturesRoot, "--out", _outDir };

        CrossLink.Apply(codeModel, sqlModel, codeArgs, "../sql");

        var packages = File.ReadAllText(Path.Combine(_outDir, "packages.html"));
        Assert.Contains("matched by name only", packages);
        Assert.Contains("5 objects", packages);
    }

    [Fact]
    public void Generated_site_never_renders_a_password_or_pwd_value()
    {
        var codeModel = MinimalCodeModel(
            new DbNode { Hash = "h1", Label = "ShopDb", Server = "sql-prod-01", Catalog = "ShopDb" },
            new DbNode { Hash = "h2", Label = "OtherDb", Server = "other-01", Catalog = "OtherDb" });
        var sqlModel = MinimalSqlModel("sql-prod-01", "ShopDb", objectCount: 1);
        var codeArgs = new[] { FixturesRoot, "--out", _outDir };

        CrossLink.Apply(codeModel, sqlModel, codeArgs, "../sql");

        var allText = string.Join('\n', Directory.EnumerateFiles(_outDir, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText));
        Assert.DoesNotMatch("(?i)password=|pwd=", allText);
    }
}
