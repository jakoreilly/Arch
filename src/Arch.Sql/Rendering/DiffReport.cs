using System.Text;
using Arch.Sql.Analysis;
using Arch.Sql.Site;

namespace Arch.Sql.Rendering;

/// <summary>Renders a SchemaDiff result as a Markdown report (for PR comments) and a themed HTML
/// page (reusing PageTemplate/site.css so it's legible in both themes without new CSS).</summary>
public static class DiffReport
{
    public static string Markdown(IReadOnlyList<SchemaChange> changes, IReadOnlySet<string> suppressed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Schema diff");
        sb.AppendLine();
        if (changes.Count == 0) { sb.AppendLine("No schema changes detected."); return sb.ToString(); }

        foreach (var risk in new[] { ChangeRisk.Breaking, ChangeRisk.Degrading, ChangeRisk.Safe })
        {
            var group = changes.Where(c => c.Risk == risk).ToList();
            if (group.Count == 0) { continue; }
            sb.AppendLine($"## {risk}");
            sb.AppendLine();
            foreach (var c in group)
            {
                var isSuppressed = suppressed.Contains(DiffBaseline.Key(c));
                var line = $"- `{c.Kind}` **{c.Target}** — {c.Detail}";
                sb.AppendLine(isSuppressed ? $"- ~~`{c.Kind}` **{c.Target}** — {c.Detail}~~ (baselined)" : line);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string RenderHtml(IReadOnlyList<SchemaChange> changes, IReadOnlySet<string> suppressed) =>
        PageTemplate.Render("Schema Diff", "Arch", "", "", Html.Crumbs((null, "Schema Diff")), Body(changes, suppressed));

    /// <summary>The report's inner body markup, without the page shell — reused by the standalone
    /// diff verb (wrapped by RenderHtml) and by the site's Drift page (wrapped by SiteGenerator).</summary>
    public static string Body(IReadOnlyList<SchemaChange> changes, IReadOnlySet<string> suppressed)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Schema Diff</h1>");
        sb.Append("""
<p class="lede">What changed between two Arch scans, classified by risk. Breaking = drops,
narrowing type changes, NULL to NOT NULL, or new NOT NULL columns. Degrading = dropped indexes/FKs.
Safe = additive. Baselined changes are shown struck-through and don't fail the gate.</p>
""");
        if (changes.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">✓</div><p>No schema changes detected between these two scans.</p></div>""");
        }
        else
        {
            // Summary before detail (the house rule for every page): a release reviewer needs the
            // breaking count before the row-by-row list, and the baselined count to know how much
            // of it is already accepted.
            var breaking = changes.Count(c => c.Risk == ChangeRisk.Breaking);
            var degrading = changes.Count(c => c.Risk == ChangeRisk.Degrading);
            var safe = changes.Count(c => c.Risk == ChangeRisk.Safe);
            var baselined = changes.Count(c => suppressed.Contains(DiffBaseline.Key(c)));
            sb.Append(Ui.Tiles(
                (breaking.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Breaking"),
                (degrading.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Degrading"),
                (safe.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Safe"),
                (baselined.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Baselined")));

            sb.Append("""<input class="filter-input" type="search" data-filter-target="#diff-rows" placeholder="Filter by target, kind or detail…" autocomplete="off" spellcheck="false"> <span class="filter-count"></span>""");
            sb.Append("""<table class="grid sortable" data-page-size="50"><thead><tr><th>Risk</th><th>Kind</th><th>Target</th><th>Detail</th></tr></thead><tbody id="diff-rows">""");
            foreach (var c in changes)
            {
                var isSuppressed = suppressed.Contains(DiffBaseline.Key(c));
                // Breaking is the worst class this report has, so it takes the danger hue and
                // Degrading the warning one. Breaking used to render amber and Degrading in the
                // brand blue, which left the severity order unreadable by colour and never used
                // .badge.danger at all. Mirrors LintPage's Critical/High/Medium/Low mapping.
                var badge = c.Risk switch
                {
                    ChangeRisk.Breaking => "badge danger",
                    ChangeRisk.Degrading => "badge warn",
                    _ => "badge ok",
                };
                var rowStyle = isSuppressed ? "text-decoration:line-through;color:var(--text-soft)" : "";
                var search = $"{c.Kind} {c.Target} {c.Detail}".ToLowerInvariant();
                sb.Append($"""
<tr class="filterable" data-search="{Html.Encode(search)}" style="{rowStyle}">
<td><span class="{badge}">{Html.Encode(c.Risk.ToString())}</span></td>
<td>{Html.Encode(c.Kind)}</td>
<td>{Html.Encode(c.Target)}</td>
<td>{Html.Encode(c.Detail)}{(isSuppressed ? " (baselined)" : "")}</td>
</tr>
""");
            }
            sb.Append("</tbody></table>");
        }
        return sb.ToString();
    }
}
