using Arch.Code.Graph;

namespace Arch.Code.Landscape;

/// <summary>One discovered site: its folder id, the loaded model, and a relative
/// href (from the landscape output dir) to that site's index.html.</summary>
public sealed record SiteRef(string Id, ProjectModel Model, string IndexHref);

/// <summary>A database seen in one or more sites, with the site ids that use it.</summary>
public sealed record SharedDb(string Hash, string Label, string Server, string Catalog, List<string> SiteIds)
{
    /// <summary>True when this database also matches a SQL-only site's Server+Catalog (an
    /// arch sql / archsql / arch connect run elsewhere in the estate) by the same canonicalized
    /// join CrossLink.cs already uses within one repo — the authoritative inventory of what the
    /// database actually contains, not just a connection string naming it. Additive; a database
    /// with no matching SQL-only site keeps this false, same as before Phase 8.</summary>
    public bool Verified { get; init; }

    /// <summary>Ids of the SQL-only sites (see <see cref="Verified"/>) that verified this
    /// database — distinct from <see cref="SiteIds"/>, which are code sites in
    /// <see cref="LandscapeModel.Sites"/>; these are <see cref="LandscapeModel.SqlSites"/>.</summary>
    public List<string> SqlSiteIds { get; init; } = [];
}

/// <summary>A SQL-only site (arch sql / archsql / arch connect output) discovered alongside the
/// code sites — never rendered as one of <see cref="LandscapeModel.Sites"/>'s columns (it holds
/// no ProjectModel), but its Server/Catalog can VERIFY a code-side database found by
/// connection-string name alone. Phase 8.</summary>
public sealed record SqlSiteRef(string Id, string Server, string Catalog, int ObjectCount, string RootName, string IndexHref);

/// <summary>A directed "consumer site references a package produced by producer site"
/// edge. Package is the matched project name.</summary>
public sealed record PackageEdge(string FromSiteId, string ToSiteId, string Package);

/// <summary>An external package (produced by no discovered site) shared by ≥2 sites.</summary>
public sealed record SharedPackage(string Name, List<string> SiteIds);

/// <summary>A heuristic cross-site call edge, aggregated with a sample + count.</summary>
public sealed record ServiceCallEdge(string FromSiteId, string ToSiteId, int Count, string Sample);

/// <summary>The whole federated view. All lists are pre-sorted for deterministic output.</summary>
public sealed record LandscapeModel
{
    public required List<SiteRef> Sites { get; init; }
    /// <summary>SQL-only sites discovered alongside the code sites above — see
    /// <see cref="SqlSiteRef"/>. Empty unless the estate has at least one arch sql / archsql /
    /// arch connect output sitting beside the code sites. Additive, Phase 8.</summary>
    public List<SqlSiteRef> SqlSites { get; init; } = [];
    public List<SharedDb> Databases { get; init; } = [];        // every DB, matrix uses SiteIds
    public List<PackageEdge> PackageEdges { get; init; } = [];
    public List<SharedPackage> SharedPackages { get; init; } = [];
    public List<ServiceCallEdge> ServiceCalls { get; init; } = [];
    public List<string> Diagnostics { get; init; } = [];
}
