using Arch.Code.Cli;
using Arch.Code.Graph;
using Arch.Code.Scanning;
using Arch.Code.Site;
using Arch.Sql.Model;

namespace Arch.Cli;

/// <summary>Phase 6 — Goal C: joins a code-side DbNode to the SQL model it references, when a
/// code and a sql provider both ran in the same combined-mode invocation. Join rules and UX
/// copy are specified in plan.md, "# Phase 6".</summary>
internal static class CrossLink
{
    /// <summary>Re-renders the code site with each DbNode's join outcome attached — a no-op
    /// when codeModel found no databases at all, which keeps every combined-mode run with
    /// nothing to join exactly as fast (and as byte-identical) as before this phase. codeArgs
    /// must be the SAME adjusted argv Runner already built for the code provider's own Generate
    /// call, so re-parsing it here reproduces the identical CliOptions (MaxNodes, ShowComplexity,
    /// ...) the first write used — this second write changes only Databases.</summary>
    public static void Apply(ProjectModel codeModel, SqlModel sqlModel, string[] codeArgs, string sqlRelativeHrefPrefix)
    {
        if (codeModel.Databases.Count == 0) { return; }

        var joined = codeModel.Databases.Select(db => db with { SqlLink = Join(db, sqlModel, sqlRelativeHrefPrefix) }).ToList();
        var updated = codeModel with { Databases = joined };

        var options = CliOptions.Parse(codeArgs, out _)
            ?? throw new InvalidOperationException("Arch.Cli: cross-link re-parse of the code provider's own args failed.");
        var generatedOn = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        SiteGenerator.Generate(updated, options.OutDir, options.MaxNodes, generatedOn,
            options.ShowComplexity, options.ShowSnippets, options.Wiki);
    }

    /// <summary>Join on (Server, Catalog), case-insensitively — SQL Server instance and database
    /// names are case-insensitive in every default collation. Verified when the sql model came
    /// from a live connection (Server is known there) and both Server and Catalog match; falls
    /// back to catalog-name-only against the sql model's own catalog (connect mode) or its
    /// RootName (a file scan — the folder name is the only catalog-shaped label available) when
    /// the verified check doesn't hold. Null when the code side found no catalog to join on at
    /// all — "not examined" is distinct from "examined, no match" (Matched = false).</summary>
    private static SqlCrossLink? Join(DbNode db, SqlModel sqlModel, string sqlRelativeHrefPrefix)
    {
        var catalog = db.Catalog.Trim();
        if (catalog.Length == 0) { return null; }
        var catalogLower = catalog.ToLowerInvariant();

        var verified = sqlModel.Server.Length > 0
            && ConnectionStringNormalizer.CanonicalizeHost(db.Server) == ConnectionStringNormalizer.CanonicalizeHost(sqlModel.Server)
            && catalogLower == sqlModel.Catalog.Trim().ToLowerInvariant();

        var fallbackCatalog = (sqlModel.Catalog.Length > 0 ? sqlModel.Catalog : sqlModel.RootName).Trim().ToLowerInvariant();
        var matched = verified || catalogLower == fallbackCatalog;

        return new SqlCrossLink
        {
            Href = matched ? $"{sqlRelativeHrefPrefix}/objects.html?catalog={Uri.EscapeDataString(catalog)}" : "",
            ObjectCount = sqlModel.Objects.Count,
            Matched = matched,
            Verified = verified,
        };
    }
}
