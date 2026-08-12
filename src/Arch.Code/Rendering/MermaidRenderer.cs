using System.Text;
using Arch.Code.Graph;

namespace Arch.Code.Rendering;

// DiagramNode/DiagramEdge/Diagram/NodeShape are the diagram data model and live in
// Arch.Code.Graph (Graph/DiagramModel.cs) so the Graph layer (GraphReducer) can use
// them without depending on Rendering — keeping the module dependency one-way.

/// <summary>Pure model -> mermaid flowchart text. Deterministic aliases (n001... in
/// input order), full label escaping (mermaid entity codes), dashed edges for
/// ambiguous/heuristic links.</summary>
public static class MermaidRenderer
{
    public const string ClassDefs =
        "classDef service fill:var(--accent-soft),stroke:var(--accent),color:var(--text);\n" +
        "classDef database fill:var(--diagram-db-soft),stroke:var(--diagram-db),color:var(--diagram-db-ink);\n" +
        "classDef file fill:var(--ok-soft),stroke:var(--ok),color:var(--ok-ink);\n" +
        "classDef external fill:var(--bg-sunken),stroke:var(--text-soft),color:var(--text-soft),stroke-dasharray: 4 3;\n" +
        "classDef folder fill:var(--warn-soft),stroke:var(--warn),color:var(--warn-ink);\n" +
        "classDef typenode fill:var(--accent-soft),stroke:var(--accent),color:var(--text);\n" +
        "classDef aggregate fill:var(--bg-sunken),stroke:var(--text-soft),color:var(--text-soft),stroke-dasharray: 2 2;";

    /// <param name="totalNodes">Pre-trim count of real nodes (0 = unknown/not trimmed).
    /// When greater than the rendered real-node count, the diagram carries a trim banner.</param>
    public static Diagram Render(IReadOnlyList<DiagramNode> nodes, IReadOnlyList<DiagramEdge> edges, string direction = "LR", int totalNodes = 0)
    {
        var sb = new StringBuilder();
        sb.Append("flowchart ").Append(direction).Append('\n');
        sb.Append(ClassDefs).Append('\n');

        var aliasById = new Dictionary<string, string>(StringComparer.Ordinal);
        var tooltips = new Dictionary<string, string>(StringComparer.Ordinal);
        var hrefs = new Dictionary<string, string>(StringComparer.Ordinal);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var i = 0;
        foreach (var node in nodes)
        {
            if (aliasById.ContainsKey(node.Id)) { continue; }
            var alias = "n" + (++i).ToString("D3");
            aliasById[node.Id] = alias;

            var label = Escape(node.Label);
            var (open, close) = node.Shape switch
            {
                NodeShape.Database => ("[(\"", "\")]"),
                NodeShape.Rounded => ("(\"", "\")"),
                NodeShape.Hexagon => ("{{\"", "\"}}"),
                _ => ("[\"", "\"]"),
            };
            sb.Append(alias).Append(open).Append(label).Append(close);
            if (node.Css.Length > 0) { sb.Append(":::").Append(node.Css); }
            sb.Append('\n');

            if (node.Tooltip.Length > 0) { tooltips[alias] = node.Tooltip; }
            if (node.Href.Length > 0) { hrefs[alias] = node.Href; }
        }

        foreach (var edge in edges)
        {
            if (!aliasById.TryGetValue(edge.FromId, out var from) || !aliasById.TryGetValue(edge.ToId, out var to)) { continue; }
            var arrow = edge.Dashed ? "-.->" : "-->";
            if (edge.Label.Length > 0)
            {
                sb.Append(from).Append(' ').Append(arrow).Append("|\"").Append(Escape(edge.Label)).Append("\"| ").Append(to).Append('\n');
            }
            else
            {
                sb.Append(from).Append(' ').Append(arrow).Append(' ').Append(to).Append('\n');
            }

            // Undirected adjacency for the hover flow-path highlight (site.js): a node's
            // "connections" for highlighting purposes run both ways along an edge.
            if (from != to)
            {
                (adjacency.TryGetValue(from, out var af) ? af : adjacency[from] = []).Add(to);
                (adjacency.TryGetValue(to, out var at) ? at : adjacency[to] = []).Add(from);
            }
        }

        // Real (non-aggregate) nodes actually rendered; the collapsed aggregate node
        // (added by GraphReducer) is excluded so the banner counts real content only.
        var shown = nodes.Where(n => n.Css != "aggregate").Select(n => n.Id).Distinct(StringComparer.Ordinal).Count();
        return new Diagram(sb.ToString(), tooltips, hrefs)
        {
            ShownNodes = shown,
            TotalNodes = totalNodes > shown ? totalNodes : shown,
            Adjacency = adjacency,
        };
    }

    /// <summary>Escapes a label for use inside a quoted mermaid node/edge label.
    /// Mermaid entity codes keep generics like List&lt;Foo&gt; readable.</summary>
    public static string Escape(string s) => s
        .Replace("&", "&amp;")
        .Replace("\"", "#quot;")
        .Replace("<", "#lt;")
        .Replace(">", "#gt;")
        .Replace("{", "#123;")
        .Replace("}", "#125;")
        .Replace("|", "#124;")
        .Replace("`", "'")
        .Replace("\r", " ")
        .Replace("\n", " ");
}
