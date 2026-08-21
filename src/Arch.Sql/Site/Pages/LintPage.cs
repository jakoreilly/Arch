using System.Text;

namespace Arch.Sql.Site.Pages;

public static class LintPage
{
    private static readonly (string Label, string Cls)[] SeverityBand =
    [
        ("Critical", "badge danger"),
        ("High", "badge warn"),
        ("Medium", "badge"),
        ("Low", "badge ok"),
    ];

    public static string Body(SiteContext ctx)
    {
        var model = ctx.Model;
        var sb = new StringBuilder();
        sb.Append("<h1>Lint</h1>");
        sb.Append("""<p class="lede">SonarQube-style findings across security, correctness, performance and maintainability rules.</p>""");

        if (model.Findings.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">✓</div><p>No issues found. Every rule passed on the objects Arch could parse — scroll to the Scorecard for the overall grade.</p></div>""");
            return sb.ToString();
        }

        sb.Append("<div class=\"tiles\">");
        for (var sev = 0; sev < 4; sev++)
        {
            var count = model.Findings.Count(f => f.Severity == sev);
            var (label, _) = SeverityBand[sev];
            sb.Append($"""<div class="tile{(count == 0 ? " tile-zero" : "")}"><div class="num">{count}</div><div class="lbl">{label}</div></div>""");
        }
        sb.Append("</div>");

        // Lint is the longest table on this site and was the one table with no way to narrow or
        // reorder it: no <thead>/<tbody> (which the sorter requires), no filter, and every finding
        // rendered at once. A reader triaging a report wants "show me the security rule hits on
        // this one procedure", which took a browser Find before.
        sb.Append(Ui.FilterBox("#lint-rows", "Filter by rule, object or message…"));
        sb.Append("""<table class="grid sortable" data-page-size="50"><thead><tr><th>Severity</th><th>Rule</th><th>Object</th><th>Message</th></tr></thead><tbody id="lint-rows">""");
        foreach (var f in model.Findings.OrderBy(f => f.Severity).ThenBy(f => f.RuleId, StringComparer.Ordinal))
        {
            var (label, cls) = SeverityBand[f.Severity];
            var obj = ctx.ById.GetValueOrDefault(f.ObjectId);
            var objLabel = obj is not null ? $"{obj.Schema}.{obj.Name}" : ctx.BySlug.GetValueOrDefault(f.Slug)?.RelPath ?? "";
            var link = obj is not null ? $"""<a href="object.html?id={Uri.EscapeDataString(obj.Id)}">{Html.Encode(objLabel)}</a>"""
                : ctx.BySlug.TryGetValue(f.Slug, out var file) ? $"""<a href="files/{f.Slug}.html">{Html.Encode(file.RelPath)}</a>""" : "";
            var search = $"{f.RuleId} {f.Title} {objLabel} {f.Message}".ToLowerInvariant();
            // data-sort-value on Severity: the cell text is a word ("Critical", "High", "Medium",
            // "Low") and sorting it alphabetically puts Critical next to... nothing useful. The
            // numeric severity is the order a reader means when they click that header.
            sb.Append($"""
<tr class="filterable" data-search="{Html.Encode(search)}"><td data-sort-value="{f.Severity}"><span class="{cls}">{label}</span></td><td>{Html.Encode(f.RuleId)} — {Html.Encode(f.Title)}</td><td>{link}</td><td>{Html.Encode(f.Message)}</td></tr>
""");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }
}
