using System.Text;

namespace Arch.Sql.Site.Pages;

/// <summary>Bounded contexts inferred from object name prefixes, with cross-domain coupling. Makes
/// a large flat single-schema database navigable by domain and shows where domains bleed into each
/// other.</summary>
public static class DomainsPage
{
    public static string Body(SiteContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Domains</h1>");
        sb.Append("""
<p class="lede">Likely bounded contexts, grouped by object name prefix (e.g. <code>Shop_</code>,
<code>Int_</code>, <code>_Maintenance_</code>). In a flat single-schema database the domains live in
naming; this groups them and measures how much each domain reaches into others. High cross-domain
coupling is a candidate for a clearer module boundary.</p>
""");

        var result = Analysis.DomainGrouping.Compute(ctx.Model);
        if (result.Domains.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">◇</div><p>No objects to group.</p></div>""");
            return sb.ToString();
        }

        // The headline the table cannot give at a glance: how many domains there are, and how much
        // of the dependency traffic crosses between them rather than staying inside one. A high
        // cross-domain share is the whole reason to read the rest of this page.
        var internalEdges = result.Domains.Sum(d => d.InternalEdges);
        var crossEdges = result.CrossEdges.Sum(e => e.Count);
        var totalEdges = internalEdges + crossEdges;
        var crossPct = totalEdges == 0 ? "0%"
            : (100.0 * crossEdges / totalEdges).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        sb.Append(Ui.Tiles(
            (result.Domains.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Domains"),
            (result.Domains.Max(d => d.ObjectCount).ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Largest domain (objects)"),
            (internalEdges.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Edges within a domain"),
            (crossEdges.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Edges across domains"),
            (crossPct, "Cross-domain share")));

        sb.Append(Ui.FilterBox("#domain-rows", "Filter domains…"));
        sb.Append("""<table class="grid sortable" data-page-size="30"><thead><tr><th>Domain</th><th>Objects</th><th>Tables</th><th>Procs/fns/triggers</th><th>Internal edges</th><th>Outgoing (cross-domain)</th><th>Incoming (cross-domain)</th></tr></thead><tbody id="domain-rows">""");
        foreach (var d in result.Domains)
        {
            sb.Append($"""
<tr class="filterable" data-search="{Html.Encode(d.Name.ToLowerInvariant())}">
<td>{Html.Encode(d.Name)}</td><td>{d.ObjectCount}</td><td>{d.Tables}</td><td>{d.Programmable}</td>
<td>{d.InternalEdges}</td><td>{d.OutgoingEdges}</td><td>{d.IncomingEdges}</td></tr>
""");
        }
        sb.Append("</tbody></table>");

        if (result.CrossEdges.Count > 0)
        {
            sb.Append("<h2>Strongest cross-domain coupling</h2>");
            sb.Append("""<p class="note">Directed dependency counts between domains (from → to). The heaviest pairs are where a refactor into real modules would cost the most — or pay off the most.</p>""");
            sb.Append("""<table class="grid sortable" data-page-size="25"><thead><tr><th>From domain</th><th>To domain</th><th>Edges</th></tr></thead><tbody>""");
            foreach (var e in result.CrossEdges.Take(100))
            {
                sb.Append($"""<tr><td>{Html.Encode(e.From)}</td><td>{Html.Encode(e.To)}</td><td>{e.Count}</td></tr>""");
            }
            sb.Append("</tbody></table>");
        }

        return sb.ToString();
    }
}
