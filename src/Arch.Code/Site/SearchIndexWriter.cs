using System.Text;
using System.Text.Json;
using Arch.Code.Graph;

namespace Arch.Code.Site;

/// <summary>Writes assets/search-index.js — a plain JS assignment (not JSON fetched
/// at runtime, which fails on file://) holding every file, type and method so the
/// Ctrl+K palette can search offline. Entries: [kind, name, detail, href].</summary>
public static class SearchIndexWriter
{
    private const int MaxEntries = 30000;

    public static void Write(ProjectModel model, string path)
    {
        var entries = new List<string[]>();

        // Files first (the smoke test asserts an entry per file), then types, then methods,
        // stopping the instant the cap is hit so a huge codebase can't bloat every page load.
        // Within each tier, files are visited in IMPORTANCE order (ImportanceScorer's fan-in-led
        // ranking; unranked files — tests and zero-score files — keep their original relative
        // order, appended last) rather than raw scan order. Two consequences, both intended:
        // the Ctrl+K palette's empty-query "browse" view (its first 12 "file" entries, in array
        // order) leads with the files most worth knowing about instead of whatever sorted first
        // alphabetically; and on a codebase big enough to hit MaxEntries, what gets dropped is the
        // long tail of peripheral files, not an arbitrary suffix of scan order.
        var orderedFiles = OrderByImportance(model);
        AddFileEntries(entries, orderedFiles);
        AddTypeEntries(entries, orderedFiles);
        AddMethodEntries(entries, orderedFiles);

        var json = JsonSerializer.Serialize(entries);
        File.WriteAllText(path, "window.ARCH_SEARCH_INDEX = " + json + ";\n", new UTF8Encoding(false));
    }

    private static List<FileNode> OrderByImportance(ProjectModel model)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        var i = 0;
        foreach (var s in Analysis.ImportanceScorer.Rank(model, model.Files.Count)) { rank[s.File.Slug] = i++; }
        return model.Files
            .OrderBy(f => rank.TryGetValue(f.Slug, out var r) ? r : int.MaxValue)
            .ToList();
    }

    private static bool AtCap(List<string[]> entries) => entries.Count >= MaxEntries;

    private static void AddFileEntries(List<string[]> entries, IReadOnlyList<FileNode> files)
    {
        foreach (var f in files)
        {
            if (AtCap(entries)) { break; }
            entries.Add(["file", f.RelPath, f.Purpose, $"files/{f.Slug}.html"]);
        }
    }

    private static void AddTypeEntries(List<string[]> entries, IReadOnlyList<FileNode> files)
    {
        foreach (var f in files)
        {
            if (AtCap(entries)) { break; }
            foreach (var t in f.Types)
            {
                if (AtCap(entries)) { break; }
                var full = t.Namespace.Length > 0 ? $"{t.Namespace}.{t.Name}" : t.Name;
                entries.Add([t.Kind, full, f.RelPath, $"files/{f.Slug}.html"]);
            }
        }
    }

    private static void AddMethodEntries(List<string[]> entries, IReadOnlyList<FileNode> files)
    {
        foreach (var f in files)
        {
            if (AtCap(entries)) { break; }
            foreach (var t in f.Types)
            {
                if (AtCap(entries)) { break; }
                foreach (var m in t.Methods)
                {
                    if (AtCap(entries)) { break; }
                    entries.Add(["method", $"{t.Name}.{m.Name}", m.Signature, $"files/{f.Slug}.html"]);
                }
            }
        }
    }
}
