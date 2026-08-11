using System.Text.Json;
using Arch.Code.Graph;
using Arch.Core.Serialization;

namespace Arch.Code.Site;

/// <summary>Trace payload: the SAME file-level nodes/edges GraphDataWriter already
/// builds (import + call edges, already capped and connectivity-ranked — see
/// GraphDataWriter.BuildJson), plus one node per HttpEndpoint and one node per distinct
/// resolved data-access object name, connected to their declaring/touching files by
/// synthetic edges. Deliberately NOT a method-level graph — the existing file-level
/// backbone already does the "middle" of the trace at a fraction of the node count a
/// method-level graph would need.</summary>
public static class TraceDataWriter
{
    public static string BuildJson(ProjectModel model)
    {
        var fileGraph = JsonSerializer.Deserialize<JsonElement>(GraphDataWriter.BuildJson(model));
        var nodes = new List<Dictionary<string, object>>();
        foreach (var n in fileGraph.GetProperty("nodes").EnumerateArray())
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, object>>(n.GetRawText())!;
            d["layer"] = "file";
            nodes.Add(d);
        }
        // GraphDataWriter's file-level "call" edges carry no confidence at all ({source,
        // target, kind} only) — every method-level CallEdge between a file pair collapses
        // into one deduped edge with the ambiguity info dropped. Trace's two-pass BFS (Hard
        // Constraint 7) needs a real signal to prefer, so it's reattached here: the LOWEST
        // CandidateCount among the method-level calls that produced this file pair (one
        // unambiguous call between A and B is good evidence the file-level edge is real,
        // even if other calls between the same two files are ambiguous).
        var minCandidates = model.Calls
            .GroupBy(c => (c.CallerSlug, c.CalleeSlug))
            .ToDictionary(g => g.Key, g => g.Min(c => c.CandidateCount > 0 ? c.CandidateCount : 1));

        var edges = new List<Dictionary<string, object>>();
        foreach (var e in fileGraph.GetProperty("edges").EnumerateArray())
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, object>>(e.GetRawText())!;
            if (Str(d["kind"]) == "call"
                && minCandidates.TryGetValue((Str(d["source"]), Str(d["target"])), out var candidates))
            {
                d["candidates"] = candidates;
            }
            edges.Add(d);
        }

        var shownFileIds = new HashSet<string>(nodes.Select(n => Str(n["id"])), StringComparer.Ordinal);
        // Hard Constraint 4: RouteScanner/DataAccessScanner keep test/vendored data in
        // model.json for round-trip fidelity, but the route:/table: nodes this page adds
        // follow api.html's own tables — first-party only.
        var bySlug = model.Files.ToDictionary(f => f.Slug, StringComparer.Ordinal);
        bool IsFirstParty(string slug) => bySlug.TryGetValue(slug, out var f) && Analysis.CodebaseStats.IsFirstParty(f);

        foreach (var ep in model.Endpoints)
        {
            if (!shownFileIds.Contains(ep.Slug) || !IsFirstParty(ep.Slug)) { continue; } // declaring file was truncated out of the capped set, or is a test/vendored file
            var id = $"route:{ep.Slug}:{ep.TypeName}.{ep.MethodName}";
            nodes.Add(new Dictionary<string, object>
            {
                ["id"] = id, ["layer"] = "route",
                ["label"] = $"{ep.Verb} /{ep.Route}",
                ["path"] = $"{ep.TypeName}.{ep.MethodName}",
                ["href"] = $"files/{ep.Slug}.html",
            });
            edges.Add(new Dictionary<string, object> { ["source"] = id, ["target"] = ep.Slug, ["kind"] = "route" });
        }

        var byTable = model.DataAccess.Where(d => d.ObjectName.Length > 0 && shownFileIds.Contains(d.Slug) && IsFirstParty(d.Slug))
            .GroupBy(d => d.ObjectName, StringComparer.Ordinal);
        foreach (var g in byTable)
        {
            var id = $"table:{g.Key}";
            nodes.Add(new Dictionary<string, object> { ["id"] = id, ["layer"] = "table", ["label"] = g.Key, ["path"] = g.Key });
            foreach (var d in g)
            {
                edges.Add(new Dictionary<string, object>
                {
                    ["source"] = d.Slug, ["target"] = id, ["kind"] = "data-access",
                    ["ops"] = d.Ops, ["confidence"] = d.IsBlindSpot ? 0 : 1,
                });
            }
        }

        var payload = new { rootName = model.RootName, nodes, edges };
        return JsonSerializer.Serialize(payload, ModelJson.Options);
    }

    /// <summary>Deserializing GraphDataWriter's own JSON back into Dictionary&lt;string,
    /// object&gt; (so "layer" can be added uniformly without redefining its anonymous node
    /// shape here) boxes every value as a JsonElement, not a native string — a direct
    /// (string) cast throws. This is the one place that unwraps it.</summary>
    private static string Str(object value) => value is JsonElement je ? je.GetString()! : (string)value;
}
