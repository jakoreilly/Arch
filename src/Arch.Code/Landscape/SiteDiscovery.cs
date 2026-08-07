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
                var model = JsonSerializer.Deserialize<ProjectModel>(File.ReadAllText(jsonPath), ModelJson.Options);
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
}
