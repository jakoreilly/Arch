using System.Text;

namespace Arch.Sql.Site.Pages;

/// <summary>Object x actor CRUD projection: which procedures/triggers/views Create, Read, Update or
/// Delete each table. Answers "what writes to this table?" — the most common question when
/// debugging data issues.</summary>
public static class CrudPage
{
    public static string Body(SiteContext ctx)
    {
        var model = ctx.Model;
        var sb = new StringBuilder();
        sb.Append("<h1>CRUD Matrix</h1>");
        sb.Append("""
<p class="lede">Which procedures, triggers and views Create, Read, Update or Delete each table.
Answers "what writes to this table?" — the single most common question when debugging data issues.
Reads are R, inserts C, updates U, deletes D. Rows are targets; columns list the actors that touch
them.</p>
""");

        var entries = model.Crud.Where(e => !e.IsBlindSpot).ToList();
        var blindSpots = model.Crud.Where(e => e.IsBlindSpot).ToList();

        if (entries.Count == 0 && blindSpots.Count == 0)
        {
            sb.Append("""
<div class="panel empty-state"><div class="big">◇</div>
<p>No CRUD relationships were found. Once the scan parses procedures, views or triggers that read or
write tables, their operations appear here.</p>
</div>
""");
            return sb.ToString();
        }

        var byId = ctx.ById;

        // "What writes to this table" is the question this page exists for, so the count of tables
        // that are written at all — and of actors whose targets could not be resolved — belongs
        // above the matrix, not inferred from scrolling it.
        sb.Append(Ui.Tiles(
            (entries.Select(e => e.Target).Distinct(StringComparer.Ordinal).Count().ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Tables touched"),
            (entries.Select(e => e.Actor).Distinct(StringComparer.Ordinal).Count().ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Actors"),
            (entries.Count(e => e.Ops.Contains('C') || e.Ops.Contains('U') || e.Ops.Contains('D')).ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Write relationships"),
            (blindSpots.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Unresolvable actors")));

        sb.Append(Ui.FilterBox("#crud-rows", "Filter by table or actor…"));

        if (entries.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">◇</div><p>No resolvable CRUD relationships were found (see the analysis blind spots below).</p></div>""");
        }
        else
        {
            // Was <table class="grid"> with a bare header <tr> and no <thead>, which is what kept
            // this table unsortable: the sorter requires a thead/tbody pair and bails without one.
            sb.Append("""<table class="grid sortable" data-page-size="100"><thead><tr><th>Table</th><th>Actor</th><th>Ops</th><th>File</th></tr></thead><tbody id="crud-rows">""");
            foreach (var e in entries.OrderBy(e => e.Target, StringComparer.Ordinal).ThenBy(e => e.Actor, StringComparer.Ordinal))
            {
                var target = byId.GetValueOrDefault(e.Target);
                var actor = byId.GetValueOrDefault(e.Actor);
                var opsDisplay = Normalize(e.Ops);
                var search = $"{target?.Schema}.{target?.Name} {actor?.Schema}.{actor?.Name}".ToLowerInvariant();
                var actorFile = "";
                if (actor is not null)
                {
                    var file = ctx.BySlug.GetValueOrDefault(actor.DefinedInSlug);
                    var linkText = file is not null ? Html.Encode(file.RelPath) : Html.Encode(actor.DefinedInSlug);
                    actorFile = $"""<a href="files/{actor.DefinedInSlug}.html">{linkText}</a>""";
                }
                sb.Append($"""
<tr class="filterable" data-search="{Html.Encode(search)}">
<td>{(target is null ? Html.Encode(e.Target) : $"""<a href="object.html?id={Uri.EscapeDataString(target.Id)}">{Html.Encode(target.Schema)}.{Html.Encode(target.Name)}</a>""")}</td>
<td>{(actor is null ? Html.Encode(e.Actor) : $"{Html.Encode(actor.Schema)}.{Html.Encode(actor.Name)}")}</td>
<td><span class="badge">{Html.Encode(opsDisplay)}</span></td>
<td>{actorFile}</td>
</tr>
""");
            }
            sb.Append("</tbody></table>");
        }

        if (blindSpots.Count > 0)
        {
            sb.Append($"""
<p class="note">{blindSpots.Count} actor(s) build SQL dynamically (EXEC of a concatenated string).
Their targets can't be determined statically and are listed as analysis blind spots (?) below —
they are not missing, just unresolvable from the scripts.</p>
<ul class="diag-list">
""");
            foreach (var b in blindSpots)
            {
                var actor = byId.GetValueOrDefault(b.Actor);
                sb.Append($"""<li><span class="badge warn">?</span> {(actor is null ? Html.Encode(b.Actor) : $"{Html.Encode(actor.Schema)}.{Html.Encode(actor.Name)}")}</li>""");
            }
            sb.Append("</ul>");
        }

        sb.Append("""<p class="note">Only cleanly-parsed files contribute to this matrix.</p>""");
        return sb.ToString();
    }

    /// <summary>Normalizes the ops set (built as a sorted-by-char-value set, e.g. "CDRU") to the
    /// conventional CRUD display order.</summary>
    private static string Normalize(string ops)
    {
        const string order = "CRUD";
        return new string(order.Where(ops.Contains).ToArray());
    }
}
