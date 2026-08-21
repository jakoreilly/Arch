using System.Text;
using Arch.Sql.Analysis;

namespace Arch.Sql.Site.Pages;

public static class ScorecardPage
{
    public static string Body(SiteContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Scorecard</h1>");
        sb.Append("""<p class="lede">A worst-wins health grade: the overall grade is the worst status among every metric below (a metric with no data doesn't worsen the grade).</p>""");

        // Summary before detail: how many metrics landed in each band, before the metric-by-metric
        // table. On a worst-wins grade the headline alone hides whether one metric failed or nine
        // did, which is the difference between a fix and a rewrite.
        var rows = ctx.Scorecard.Rows;
        sb.Append(Ui.Tiles(
            (rows.Count(r => r.Status == SqlScorecard.Status.Fail).ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Failing"),
            (rows.Count(r => r.Status == SqlScorecard.Status.Watch).ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Watch"),
            (rows.Count(r => r.Status == SqlScorecard.Status.Ok).ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Passing"),
            (rows.Count(r => r.Status == SqlScorecard.Status.NA).ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "No data")));

        sb.Append($"<h2>Overall: {Badge(ctx.Scorecard.Overall)}</h2>");
        sb.Append("""<table class="grid sortable"><thead><tr><th>Metric</th><th>Value</th><th>Status</th><th>Note</th><th>Action</th></tr></thead><tbody>""");
        foreach (var row in rows)
        {
            var link = row.Link.Length > 0 ? $"""<a href="{row.Link}">{Html.Encode(row.Metric)}</a>""" : Html.Encode(row.Metric);
            // Sorting Status by its badge text would order Fail/Ok/Watch alphabetically, which
            // interleaves the bands a reader is trying to separate. Rank worst-first instead.
            var statusRank = row.Status switch
            {
                SqlScorecard.Status.Fail => 0,
                SqlScorecard.Status.Watch => 1,
                SqlScorecard.Status.Ok => 2,
                _ => 3,
            };
            sb.Append($"""
<tr><td>{link}</td><td>{Html.Encode(row.Value)}</td><td data-sort-value="{statusRank}">{Badge(row.Status)}</td><td>{Html.Encode(row.Note)}</td><td>{Html.Encode(row.Action)}</td></tr>
""");
        }
        sb.Append("</tbody></table>");
        sb.Append("""<p class="note">The overall grade is the worst single row above, so read the failing rows rather than the headline. Every signal is heuristic and syntax-only — a conversation starter for a review, not a certification.</p>""");
        return sb.ToString();
    }

    private static string Badge(SqlScorecard.Status status) => status switch
    {
        SqlScorecard.Status.Ok => """<span class="badge ok">Ok</span>""",
        SqlScorecard.Status.Watch => """<span class="badge warn">Watch</span>""",
        SqlScorecard.Status.Fail => """<span class="badge danger">Fail</span>""",
        _ => """<span class="badge">N/A</span>""",
    };
}
