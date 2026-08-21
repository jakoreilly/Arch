namespace Arch.Sql.Site.Pages;

public static class DependenciesPage
{
    public static string Body(SiteContext ctx, int maxNodes)
    {
        var hasDeps = ctx.Model.Dependencies.Any(d => d.ToObjectId.Length > 0);
        if (!hasDeps)
        {
            return """
<h1>Dependencies</h1>
<div class="panel empty-state"><div class="big">◇</div>
<p>No resolved object-to-object dependencies were found. Views, procedures and triggers that
reference tables or other objects will show up here once the scan can resolve those references.</p>
</div>
""";
        }

        var deps = MermaidRenderer.BuildDependencies(ctx.Model, maxNodes);
        var trimNotice = deps.Trimmed ? $"Showing {deps.Shown} of {deps.Total} objects — the diagram is capped at --max-nodes." : null;
        // Unresolved references (ToObjectId "") are the honesty figure on this page: they are edges
        // the analysis saw but could not land, so the diagram below is a floor. Stating the count
        // beats leaving a reader to assume the graph is complete.
        var resolved = ctx.Model.Dependencies.Count(d => d.ToObjectId.Length > 0);
        var unresolved = ctx.Model.Dependencies.Count - resolved;
        var maxFanIn = ctx.FanIn.Count == 0 ? 0 : ctx.FanIn.Values.Max();
        var maxFanOut = ctx.FanOut.Count == 0 ? 0 : ctx.FanOut.Values.Max();
        var tiles = Ui.Tiles(
            (resolved.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Resolved references"),
            (unresolved.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Unresolved references"),
            (maxFanIn.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Highest fan-in"),
            (maxFanOut.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Highest fan-out"));
        return $"""
<h1>Dependencies</h1>
<p class="lede">Which objects reference which — procedures calling other procedures, views
selecting from tables, foreign keys between tables. High fan-in objects are risky to change;
high fan-out objects know too much.</p>
{tiles}
{PageTemplate.DiagramBlock("deps-diagram", deps.Mermaid, trimNotice)}
{PageTemplate.Legend()}
<p class="note">This diagram is capped for readability. Open <a href="graph.html">Graph (3D)</a> to
explore every object at once, or <a href="metrics.html">Metrics</a> for the fan-in/fan-out rankings
behind the figures above.</p>
""";
    }
}
