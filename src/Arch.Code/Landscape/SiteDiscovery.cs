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
}
