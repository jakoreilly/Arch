using System.Text;
using Arch.Sql.Analysis;

namespace Arch.Sql.Site.Pages;

public static class ObjectsPage
{
    public static string Body(SiteContext ctx)
    {
        var model = ctx.Model;
        var sb = new StringBuilder();
        sb.Append("<h1>Objects</h1>");
        sb.Append("""<p class="lede">Every table, view, procedure, function and trigger found in this scan.</p>""");

        if (model.Objects.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">◇</div><p>No schema objects were found. Point Arch at a folder containing CREATE TABLE/VIEW/PROCEDURE statements.</p></div>""");
            return sb.ToString();
        }

        // The inventory this page IS, as figures, before the row-by-row list of it. Tables without
        // a primary key and shallow-parsed objects are called out because both change how much of
        // the rest of the site can be trusted for those objects.
        var tables = model.Objects.Where(o => o.Kind == "table").ToList();
        var noPk = tables.Count(o => o.PrimaryKey.Count == 0);
        var shallowCount = model.Objects.Count(o => ctx.BySlug.GetValueOrDefault(o.DefinedInSlug) is { ParsedCleanly: false });
        sb.Append(Ui.Tiles(
            (model.Objects.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Objects"),
            (tables.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Tables"),
            (model.Objects.Count(o => o.Kind == "view").ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Views"),
            (model.Objects.Count(o => o.Kind is "procedure" or "function" or "trigger").ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Procs / fns / triggers"),
            (noPk.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Tables without a PK"),
            (shallowCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Shallow-parsed")));

        sb.Append(Ui.FilterBox("#objects-tbody", "Filter by schema, name or kind…"));
        sb.Append("""<table class="grid sortable" id="objects-table" data-page-size="100"><thead><tr><th>Schema</th><th>Name</th><th>Kind</th><th>PK?</th><th>Columns</th><th>Fan-in</th><th>Fan-out</th><th>Purpose</th><th>File</th></tr></thead><tbody id="objects-tbody">""");
        foreach (var o in model.Objects.OrderBy(o => o.Schema, StringComparer.OrdinalIgnoreCase).ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase))
        {
            var pkBadge = o.Kind != "table" ? "" : o.PrimaryKey.Count > 0 ? """<span class="badge ok">Yes</span>""" : """<span class="badge warn">No</span>""";
            var file = ctx.BySlug.GetValueOrDefault(o.DefinedInSlug);
            var shallow = file is { ParsedCleanly: false } ? $""" <span class="badge" title="Shallow parse">shallow</span>{Glossary.Info("shallow-parse")}""" : "";
            var search = $"{o.Schema}.{o.Name} {o.Kind}".ToLowerInvariant();
            sb.Append($"""
<tr class="filterable" data-search="{Html.Encode(search)}">
<td>{Html.Encode(o.Schema)}</td>
<td><a href="object.html?id={Uri.EscapeDataString(o.Id)}">{Html.Encode(o.Name)}</a>{shallow}</td>
<td>{Html.Encode(o.Kind)}</td>
<td>{pkBadge}</td>
<td>{o.Columns.Count}</td>
<td>{ctx.FanIn.GetValueOrDefault(o.Id)}</td>
<td>{ctx.FanOut.GetValueOrDefault(o.Id)}</td>
<td>{Html.Encode(SqlPurpose.ForObject(o))}</td>
<td><a href="files/{o.DefinedInSlug}.html">{Html.Encode(file?.RelPath ?? "")}</a></td>
</tr>
""");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }
}
