using Arch.Code.Cli;
using Arch.Code.Graph;
using Arch.Code.Scanning;
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
    /// ...) the first write used — this second write changes only Databases.
    /// Returns the joined databases (empty when there were none) so the caller can report the
    /// same outcome on the hub page without re-running the join.</summary>
    public static IReadOnlyList<DbNode> Apply(ProjectModel codeModel, SqlModel sqlModel, string[] codeArgs, string sqlRelativeHrefPrefix)
    {
        if (codeModel.Databases.Count == 0 && codeModel.DataAccess.Count == 0) { return []; }

        var joined = codeModel.Databases.Select(db => db with { SqlLink = Join(db, sqlModel, sqlRelativeHrefPrefix) }).ToList();
        var joinedDataAccess = codeModel.DataAccess.Select(d => d with { ResolvedObjectId = JoinObject(d, sqlModel) }).ToList();
        var updated = codeModel with { Databases = joined, DataAccess = joinedDataAccess };

        var options = CliOptions.Parse(codeArgs, out _)
            ?? throw new InvalidOperationException("Arch.Cli: cross-link re-parse of the code provider's own args failed.");
        var generatedOn = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        // Fully qualified: Arch.Cli's namespace does not enclose Arch.Code, so the short name
        // does not resolve here. This re-render is the SECOND write of the code site in a combined
        // run — Runner has already written it once — so it must go through the same generator, or
        // the two passes disagree about what the site contains.
        Arch.Code.SiteGenerator.Generate(updated, options.OutDir, options.MaxNodes, generatedOn,
            options.ShowComplexity, options.ShowSnippets, options.Wiki);
        return joined;
    }

    /// <summary>Best-effort schema.name split ("dbo.Orders" -&gt; ("dbo","Orders"); "Orders"
    /// -&gt; ("", "Orders")) then normalised through Arch.Sql's own identifier rules (the
    /// only place cross-dialect identifier case/delimiter handling exists — Arch.Code must
    /// not duplicate it) and looked up by id. Null when there is no object name to join on
    /// at all (a blind spot) or no SQL model object matches.</summary>
    private static string? JoinObject(DataAccessRef d, SqlModel sqlModel)
    {
        if (d.ObjectName.Length == 0) { return null; }
        var dot = d.ObjectName.IndexOf('.');
        var schema = dot >= 0 ? d.ObjectName[..dot] : "";
        var name = dot >= 0 ? d.ObjectName[(dot + 1)..] : d.ObjectName;
        var id = Arch.Sql.Analysis.IdentifierRules.NormalizeId(schema, name, sqlModel.Dialect);
        return sqlModel.Objects.Any(o => o.Id == id) ? id : null;
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
