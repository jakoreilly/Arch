using Arch.Code.Graph;
using Arch.Code.Landscape;

namespace Arch.Code.Tests;

/// <summary>Phase 8 of plan.md: a SQL-only site (arch sql / archsql / arch connect) discovered
/// alongside code sites can VERIFY a database a code site only knows by connection-string name —
/// the same (Server, Catalog) join CrossLink.cs already does within one repo, applied across
/// repos in the landscape.</summary>
public class LandscapeSqlVerificationTests
{
    private static SiteRef Site(string id, ProjectModel m) => new(id, m, $"../{id}/index.html");

    private static readonly ProjectModel Orders = new()
    {
        RootName = "orders", SourcePath = "x",
        Databases = [new DbNode { Hash = "H1", Label = "Orders", Server = "sql-prod-01", Catalog = "Orders" }],
    };

    [Fact]
    public void A_sql_only_site_with_matching_server_and_catalog_verifies_the_database()
    {
        var sqlSites = new List<SqlSiteRef> { new("site-db", "sql-prod-01", "Orders", 42, "Orders", "../site-db/index.html") };

        var landscape = LandscapeModelBuilder.Build([Site("site-orders", Orders)], sqlSites);

        var db = Assert.Single(landscape.Databases);
        Assert.True(db.Verified);
        Assert.Equal(["site-db"], db.SqlSiteIds);
    }

    [Fact]
    public void A_sql_only_site_with_a_different_catalog_does_not_verify()
    {
        var sqlSites = new List<SqlSiteRef> { new("site-db", "sql-prod-01", "SomethingElse", 10, "SomethingElse", "../site-db/index.html") };

        var landscape = LandscapeModelBuilder.Build([Site("site-orders", Orders)], sqlSites);

        Assert.False(Assert.Single(landscape.Databases).Verified);
    }

    [Fact]
    public void A_file_scanned_sql_site_with_no_server_never_verifies_by_catalog_alone()
    {
        // Guards the false-positive this join must not produce: two databases with equally
        // empty Server would otherwise "verify" each other on catalog name alone.
        var noServerModel = Orders with { Databases = [new DbNode { Hash = "H2", Label = "Orders", Server = "", Catalog = "Orders" }] };
        var sqlSites = new List<SqlSiteRef> { new("site-db", "", "Orders", 5, "Orders", "../site-db/index.html") };

        var landscape = LandscapeModelBuilder.Build([Site("site-orders", noServerModel)], sqlSites);

        Assert.False(Assert.Single(landscape.Databases).Verified);
    }

    [Fact]
    public void Host_aliases_still_verify_through_the_same_canonicalization_crosslink_uses()
    {
        // "." and "localhost" are the same machine (ConnectionStringNormalizer.CanonicalizeHost).
        var localModel = Orders with { Databases = [new DbNode { Hash = "H3", Label = "Orders", Server = ".", Catalog = "Orders" }] };
        var sqlSites = new List<SqlSiteRef> { new("site-db", "localhost", "Orders", 5, "Orders", "../site-db/index.html") };

        var landscape = LandscapeModelBuilder.Build([Site("site-orders", localModel)], sqlSites);

        Assert.True(Assert.Single(landscape.Databases).Verified);
    }

    [Fact]
    public void No_sql_sites_leaves_every_database_unverified_same_as_before_phase_8()
    {
        var landscape = LandscapeModelBuilder.Build([Site("site-orders", Orders)]);

        Assert.False(Assert.Single(landscape.Databases).Verified);
        Assert.Empty(landscape.SqlSites);
    }
}
