using System.Text;
using System.Text.Json;
using Arch.Code.Graph;
using Arch.Code.Rendering;
using Arch.Core.Serialization;

namespace Arch.Code.Site;

/// <summary>Emits graph.json for the 3D viewer: file nodes with the five data
/// channels precomputed (folder, language, size, fan-in, fan-out), plus import
/// and heuristic-call edges. Deterministic ordering (model file order).</summary>
public static class GraphDataWriter
{
    // Public: GraphPage and ExplorePage both need the same cap to warn a reader when the graph
    // or the Explore console answers from a truncated subset of the codebase, not the whole thing.
    public const int MaxNodes = 2000, MaxEdges = 8000;

    public static void Write(ProjectModel model, string path) => WriteJson(BuildJson(model), path);

    /// <summary>Writes an already-built payload (see <see cref="SiteContext.GraphJson"/>) so the
    /// full-site generation path serializes the graph once instead of once per consumer.</summary>
    public static void WriteJson(string json, string path) =>
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>Builds the viewer payload as a JSON string. Written to graph.json for
    /// external tooling and embedded inline in graph.html so the graph works from
    /// file:// (where fetch() is blocked).</summary>
    public static string BuildJson(ProjectModel model)
    {
        var fanIn = new Dictionary<string, int>(StringComparer.Ordinal);
        var fanOut = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in model.FileDependencies.Where(e => e.ToSlug.Length > 0))
        {
            fanOut[e.FromSlug] = fanOut.GetValueOrDefault(e.FromSlug) + 1;
            fanIn[e.ToSlug] = fanIn.GetValueOrDefault(e.ToSlug) + 1;
        }

        // Truncation, when the repo has more files than the cap, keeps the most-CONNECTED files
        // rather than the first N in file order — a blind Take() dropped whatever came late in
        // path-sort order, so "orphans"/"importedby:" on a large repo silently answered from an
        // arbitrary slice instead of the interesting half. Selection is by connectivity; the kept
        // set is then re-filtered in original file order so the payload stays diff-stable run to
        // run instead of reshuffling every time a fan-in count changes slightly.
        var filesForGraph = model.Files;
        if (model.Files.Count > MaxNodes)
        {
            var keep = new HashSet<string>(
                model.Files
                    .OrderByDescending(f => fanIn.GetValueOrDefault(f.Slug) + fanOut.GetValueOrDefault(f.Slug))
                    .ThenBy(f => f.RelPath, StringComparer.Ordinal)
                    .Take(MaxNodes)
                    .Select(f => f.Slug),
                StringComparer.Ordinal);
            filesForGraph = model.Files.Where(f => keep.Contains(f.Slug)).ToList();
        }

        var nodes = filesForGraph.Select(f => new
        {
            id = f.Slug,
            label = f.RelPath.Split('/')[^1],
            path = f.RelPath,
            folder = f.RelPath.Contains('/') ? f.RelPath[..f.RelPath.IndexOf('/')] : "(root)",
            lang = f.Language,
            loc = f.Loc,
            cog = f.Types.SelectMany(t => t.Methods).Select(m => m.Cognitive).DefaultIfEmpty(0).Max(),
            fanIn = fanIn.GetValueOrDefault(f.Slug),
            fanOut = fanOut.GetValueOrDefault(f.Slug),
            test = IsTestFile(f.RelPath),
            href = $"files/{f.Slug}.html",
        }).ToList();

        var shown = new HashSet<string>(nodes.Select(n => n.id), StringComparer.Ordinal);
        var edges = model.FileDependencies
            .Where(e => e.ToSlug.Length > 0 && shown.Contains(e.FromSlug) && shown.Contains(e.ToSlug))
            .Select(e => new { source = e.FromSlug, target = e.ToSlug, kind = "import" })
            .Concat(model.Calls
                .Where(c => c.CallerSlug != c.CalleeSlug && shown.Contains(c.CallerSlug) && shown.Contains(c.CalleeSlug))
                .Select(c => new { source = c.CallerSlug, target = c.CalleeSlug, kind = "call" }))
            .DistinctBy(e => (e.source, e.target, e.kind))
            .Take(MaxEdges).ToList();

        var payload = new
        {
            rootName = model.RootName,
            sourceLink = model.SourceLink,
            totalFiles = model.Files.Count,
            shownNodes = nodes.Count,
            nodes,
            edges,
        };
        return JsonSerializer.Serialize(payload, ModelJson.Options);
    }

    /// <summary>Does this file look like automated-test code? Delegates to the single
    /// site-wide detector so the 3D graph hides exactly the same files as the tree,
    /// tables, treemap and call graph.</summary>
    public static bool IsTestFile(string relPath) => Analysis.TestDetection.IsTest(relPath);
}
