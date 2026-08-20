using System.Text.Json;
using Arch.Code.Graph;
using Arch.Core.Serialization;

namespace Arch.Code.Landscape;

/// <summary>Finds every immediate subfolder of the parent dir that contains a
/// model.json, loads it, and returns a SiteRef with a relative href from the
/// landscape output dir. Unreadable/unparseable sites are skipped with a diagnostic.</summary>
public static class SiteDiscovery
{
    /// <summary>Where a code model.json sits inside a generated site folder, in probe order:
    /// at the root for a single-provider site (what <c>archdiagram</c> and single-provider
    /// <c>arch</c> write), then under <c>code/</c> for an <c>arch</c> combined-mode site,
    /// which nests each provider's complete site one level down and puts a hub at the root.
    /// The sql sibling is deliberately not probed — it holds a SqlModel, not a ProjectModel.</summary>
    private static readonly string[] ModelProbePaths = ["model.json", Path.Combine("code", "model.json")];

    public static List<SiteRef> Discover(string parentDir, string landscapeOutDir, List<string> diagnostics, ISet<string>? onlyFolderNames = null)
    {
        var sites = new List<SiteRef>();
        var outFull = Path.GetFullPath(landscapeOutDir);
        foreach (var dir in Directory.EnumerateDirectories(parentDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (Path.GetFullPath(dir).Equals(outFull, StringComparison.OrdinalIgnoreCase)) { continue; } // skip our own output
            var id = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (onlyFolderNames is not null && !onlyFolderNames.Contains(id)) { continue; } // scope to a group's subset
            var jsonPath = ModelProbePaths
                .Select(p => Path.Combine(dir, p))
                .FirstOrDefault(File.Exists);
            if (jsonPath is null) { continue; }
            try
            {
                var json = File.ReadAllText(jsonPath);
                // A SQL-ONLY site writes a SqlModel to the same root path a code site uses, and
                // both call their top-level array "files" — so deserializing it as a ProjectModel
                // fails deep inside with "FileNode was missing required properties including
                // 'language'", which reads like a corrupt file rather than the ordinary situation
                // it is. Named explicitly here because a group run makes it common: point `arch
                // group` at a set of repos and one of them being SQL-only is unremarkable.
                if (IsSqlModel(json))
                {
                    diagnostics.Add($"Skipped {id}: it is a SQL-only site, which has no code model to "
                                  + "federate. Its own site is still complete and linked from wherever you generated it.");
                    continue;
                }
                // Checked before deserializing, same property-peek shape as IsSqlModel above:
                // a model.json from a NEWER Arch than this one may have added a required
                // property, and deserializing it here would fail deep inside with a missing-
                // property error that reads like corruption rather than the ordinary version-
                // skew situation it is.
                if (TryGetSchemaVersion(json) is int found && found > ProjectModel.CurrentSchemaVersion)
                {
                    diagnostics.Add($"Skipped {id}: its model.json is schema v{found}, and this Arch "
                                  + $"understands up to v{ProjectModel.CurrentSchemaVersion}. Regenerate "
                                  + "the estate with a matching Arch version, or upgrade this one.");
                    continue;
                }
                var model = JsonSerializer.Deserialize<ProjectModel>(json, ModelJson.Options);
                if (model is null) { diagnostics.Add($"Skipped {jsonPath}: deserialized to null."); continue; }
                // The folder root, not the model's own folder: that is the site's front door in
                // both shapes — the code site's index for a single-provider site, the hub (which
                // links onward to code/ and sql/) for a combined one.
                var href = Path.GetRelativePath(landscapeOutDir, Path.Combine(dir, "index.html")).Replace('\\', '/');
                sites.Add(new SiteRef(id, model, href));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                diagnostics.Add($"Skipped {jsonPath}: {ex.Message}");
            }
        }
        return sites;
    }

    /// <summary>True when the JSON is a SqlModel rather than a ProjectModel. Keyed on "objects",
    /// which every SqlModel has at its root and no ProjectModel does — a property peek rather than
    /// a type reference, because Arch.Code cannot see Arch.Sql (and should not learn to).</summary>
    private static bool IsSqlModel(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("objects", out var objects)
                && objects.ValueKind == JsonValueKind.Array
                && !doc.RootElement.TryGetProperty("projects", out _);
        }
        catch (JsonException)
        {
            return false; // let the real deserialization report it
        }
    }

    /// <summary>Discovers every immediate subfolder that is a SQL-ONLY site (see
    /// <see cref="IsSqlModel"/>) — the ones <see cref="Discover"/> itself skips with a
    /// diagnostic, since they hold a SqlModel and Arch.Code cannot deserialize one (and should
    /// not learn to). A separate method rather than folding into Discover: every existing caller
    /// of Discover keeps its exact List&lt;SiteRef&gt; return shape, and a landscape federating
    /// nothing but code sites (the common case today) pays nothing extra. Phase 8.</summary>
    public static List<SqlSiteRef> DiscoverSqlSites(string parentDir, string landscapeOutDir, ISet<string>? onlyFolderNames = null)
    {
        var sqlSites = new List<SqlSiteRef>();
        var outFull = Path.GetFullPath(landscapeOutDir);
        foreach (var dir in Directory.EnumerateDirectories(parentDir).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (Path.GetFullPath(dir).Equals(outFull, StringComparison.OrdinalIgnoreCase)) { continue; }
            var id = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (onlyFolderNames is not null && !onlyFolderNames.Contains(id)) { continue; }
            var jsonPath = Path.Combine(dir, "model.json");
            if (!File.Exists(jsonPath)) { continue; }
            try
            {
                var json = File.ReadAllText(jsonPath);
                if (!IsSqlModel(json)) { continue; }
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var server = root.TryGetProperty("server", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
                var catalog = root.TryGetProperty("catalog", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
                var rootName = root.TryGetProperty("rootName", out var rn) && rn.ValueKind == JsonValueKind.String ? rn.GetString() ?? "" : "";
                var objectCount = root.TryGetProperty("objects", out var objs) && objs.ValueKind == JsonValueKind.Array ? objs.GetArrayLength() : 0;
                var href = Path.GetRelativePath(landscapeOutDir, Path.Combine(dir, "index.html")).Replace('\\', '/');
                sqlSites.Add(new SqlSiteRef(id, server, catalog, objectCount, rootName, href));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Discover's own pass over the same folder already reports read/parse failures
                // for anything that isn't cleanly one shape or the other; staying silent here
                // avoids a duplicate diagnostic for the same file.
            }
        }
        return sqlSites;
    }

    /// <summary>The raw "schemaVersion" number if present, or null — never throws. Absent means
    /// a model.json written before the field existed (schema v1, per ProjectModel.SchemaVersion's
    /// own doc comment); that is always <c>&lt;= CurrentSchemaVersion</c>, so absent never trips
    /// the too-new check above regardless of how many versions have shipped since.</summary>
    private static int? TryGetSchemaVersion(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("schemaVersion", out var v)
                && v.ValueKind == JsonValueKind.Number
                ? v.GetInt32() : null;
        }
        catch (JsonException)
        {
            return null; // let the real deserialization report it
        }
    }
}
