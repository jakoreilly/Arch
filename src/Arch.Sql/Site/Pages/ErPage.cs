namespace Arch.Sql.Site.Pages;

public static class ErPage
{
    public static string Body(SiteContext ctx, int maxNodes)
    {
        // Named for what it counts. It was `tablesWithFk`, which is not what a count of every
        // table is — the FK guard is the separate clause beside it.
        var tableCount = ctx.Model.Objects.Count(o => o.Kind == "table");
        if (ctx.Model.ForeignKeys.Count == 0 || tableCount == 0)
        {
            return """
<h1>ER Diagram</h1>
<div class="panel empty-state"><div class="big">◇</div>
<p>No tables with foreign keys were found in this scan. Add schema files containing
CREATE TABLE … FOREIGN KEY statements, or check the Diagnostics on the Overview if parsing failed.</p>
</div>
""";
        }

        var er = MermaidRenderer.BuildEr(ctx.Model, maxNodes);
        var trimNotice = er.Trimmed ? $"Showing {er.Shown} of {er.Total} tables — the diagram is capped at --max-nodes." : null;
        // "How much of the schema is actually wired up" is not readable off the diagram, especially
        // once it is capped: an isolated table looks the same as one whose edges were trimmed away.
        // ToObjectId is "" when the reference did not resolve, so it is filtered out rather than
        // counted as a table in its own right.
        var connected = ctx.Model.ForeignKeys
            .SelectMany(fk => new[] { fk.FromObjectId, fk.ToObjectId })
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var tiles = Ui.Tiles(
            (tableCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Tables"),
            (ctx.Model.ForeignKeys.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Foreign keys"),
            (connected.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Tables in a relationship"),
            (Math.Max(0, tableCount - connected).ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Standalone tables"));
        return $"""
<h1>ER Diagram</h1>
<p class="lede">Every table and its foreign-key relationships to other tables in this scan.</p>
{tiles}
{PageTemplate.DiagramBlock("er-diagram", er.Mermaid, trimNotice)}
{PageTemplate.Legend()}
""";
    }
}
