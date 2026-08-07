using System.Text;
using Arch.Core.Web;

namespace Arch.Cli;

/// <summary>The landing page written at outDir/index.html only in combined mode (both
/// providers applied) — links into outDir/code/ and outDir/sql/, each a complete,
/// unmodified site from its own product. Never merges either product's own nav; see
/// plan.md's Phase 5 findings for why (confirmed with the user: nesting over merging).</summary>
public static class HubPage
{
    /// <summary>One headline figure on a provider's card. Value is pre-formatted by the
    /// caller (thousands separators, invariant culture) since only the caller knows the
    /// model; Label is plain text and is encoded here.</summary>
    public readonly record struct Stat(string Value, string Label);

    /// <summary>One link the hub offers — a provider that generated successfully.
    /// Providers that failed are omitted; "partial site beats no site," but a hub link
    /// to a subsite that doesn't exist would be worse than no link at all.</summary>
    /// <param name="Grade">Health grade for this subsite ("HEALTHY"/"AT RISK"/…), "" when the
    /// provider has no scorecard. Both analysers grade on the same Ok/Watch/Fail scale but print
    /// it differently on their own pages; the hub normalises the wording so two grades sitting
    /// side by side are actually comparable.</param>
    /// <param name="GradeClass">Badge class for <paramref name="Grade"/> ("ok"/"warn"/"danger").</param>
    /// <param name="Pages">Key pages inside this subsite, for the "Jump straight to" panel. These
    /// cannot live inside the card — the card IS an anchor, and anchors do not nest.</param>
    public readonly record struct Link(
        string Id,
        string Title,
        string Icon,
        string Summary,
        IReadOnlyList<Stat> Stats,
        string Grade = "",
        string GradeClass = "",
        IReadOnlyList<Page>? Pages = null,
        string GradeDetail = "")
    {
        public Link(string id, string title, string icon, string summary)
            : this(id, title, icon, summary, []) { }
    }

    /// <summary>One deep link into a subsite: a page name and its path relative to the subsite.</summary>
    public readonly record struct Page(string Title, string Href);

    /// <summary>One item on the merged "what to do first" list. <paramref name="Href"/> is already
    /// hub-relative.</summary>
    public readonly record struct Action(string Severity, string SeverityClass, string Text, string Href, string Source);

    /// <summary>One row of the cross-layer join panel: a database the code connects to, and
    /// what the join against the SQL model found. <paramref name="Href"/> is "" when there is
    /// nothing to link to (no match), in which case only the label and status render.</summary>
    public readonly record struct DbLink(string Label, string Status, string BadgeClass, string Href);

    private const string PrePaintScript = """
<script>
(function () {
  var t = null;
  try { t = localStorage.getItem("archdiagram-theme"); } catch (e) { }
  if (!t) { t = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"; }
  document.documentElement.setAttribute("data-theme", t);
})();
</script>
""";

    public static void Write(string outDir, string siteName, IReadOnlyList<Link> links)
        => Write(outDir, siteName, links, "", "", []);

    public static void Write(
        string outDir, string siteName, IReadOnlyList<Link> links,
        string sourcePath, string generatedOn, IReadOnlyList<DbLink> dbLinks)
        => Write(outDir, siteName, links, sourcePath, generatedOn, dbLinks, [], "", []);

    /// <param name="sourcePath">The scanned folder, shown so the page says what it was built
    /// from; "" omits that clause.</param>
    /// <param name="generatedOn">Pre-formatted date, invariant culture; "" omits it.</param>
    /// <param name="dbLinks">Cross-layer join outcomes, empty when the code side referenced no
    /// databases (or only one provider ran) — the panel is skipped entirely then rather than
    /// rendering an empty shell.</param>
    /// <param name="actions">Merged "what to do first" across every subsite, already ordered and
    /// capped by the caller.</param>
    /// <param name="owner">Owning team from the descriptions sidecar; "" omits the panel.</param>
    /// <param name="capabilities">Capability names from the sidecar rollup.</param>
    public static void Write(
        string outDir,
        string siteName,
        IReadOnlyList<Link> links,
        string sourcePath,
        string generatedOn,
        IReadOnlyList<DbLink> dbLinks,
        IReadOnlyList<Action> actions,
        string owner,
        IReadOnlyList<string> capabilities)
    {
        Directory.CreateDirectory(outDir);
        SiteAssets.CopyTo(outDir);

        var nav = links.Select(l => (Href: $"{l.Id}/index.html", Title: l.Title, Icon: l.Icon)).ToArray();
        var shell = new ShellOptions
        {
            Brand = "Arch",
            Nav = [("", nav)],
            // Every provider's own shell always supplies a pre-paint theme script — an empty
            // ExtraHead would flash unthemed on load. The hub is no exception.
            ExtraHead = PrePaintScript,
        };

        var sb = new StringBuilder();
        // "Arch — Arch" is what the naive form produced whenever the scanned folder happened to
        // be called Arch, and it read as a rendering bug rather than a title. Collapse to the
        // bare brand when the folder name adds nothing.
        var heading = string.Equals(siteName, "Arch", StringComparison.OrdinalIgnoreCase)
            ? "Arch"
            : $"Arch — {Html.Encode(siteName)}";
        sb.Append($"<h1>{heading}</h1>");
        sb.Append("<p class=\"lede\">This folder holds more than one kind of content, so Arch generated a "
                + "complete site for each and left them side by side. Pick one to explore; the sidebar in "
                + "either site has its own full navigation.</p>");
        sb.Append(Provenance(sourcePath, generatedOn));
        sb.Append(OwnerPanel(owner, capabilities));
        sb.Append(Cards(links));
        // Health before actions before detail: the hub answers "is this alright?" first, then
        // "what do I do?", then "where do I go?". Every panel below omits itself entirely when it
        // has nothing to say, so a minimal run still produces a clean page rather than empty shells.
        sb.Append(HealthPanel(links));
        sb.Append(ActionsPanel(actions));
        sb.Append(CrossLinkPanel(dbLinks));
        sb.Append(JumpPanel(links));
        sb.Append("<p class=\"note\">Everything here is a static, read-only snapshot: no page needs a network "
                + "connection, and neither analyser wrote anything to the folder it scanned. Re-run "
                + "<code>arch</code> against the same folder to refresh it.</p>");

        var html = PageShell.Render(shell, "Arch", siteName, "", "", Html.Crumbs((null, "Arch")), sb.ToString());
        File.WriteAllText(Path.Combine(outDir, "index.html"), html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>What this site was built from. Both clauses are optional so the older
    /// three-argument <see cref="Write(string, string, IReadOnlyList{Link})"/> overload still
    /// produces a sensible page.</summary>
    private static string Provenance(string sourcePath, string generatedOn)
    {
        if (sourcePath.Length == 0 && generatedOn.Length == 0) { return ""; }
        var from = sourcePath.Length > 0 ? $" of <code>{Html.Encode(sourcePath)}</code>" : "";
        var on = generatedOn.Length > 0 ? $" on {Html.Encode(generatedOn)}" : "";
        return $"<p class=\"lede\">Generated from a static scan{from}{on}.</p>";
    }

    /// <summary>One card per generated subsite: what it covers, its headline figures, and a
    /// whole-card link into it. The card is the &lt;a&gt; so the entire tile is the click
    /// target and the shared :focus-visible ring applies without extra CSS.</summary>
    private static string Cards(IReadOnlyList<Link> links)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"hub-cards\">");
        foreach (var l in links)
        {
            sb.Append($"<a class=\"hub-card\" href=\"{l.Id}/index.html\">");
            sb.Append("<div class=\"hub-card-head\">");
            sb.Append($"<span class=\"hub-mark\" aria-hidden=\"true\">{l.Icon}</span>");
            sb.Append($"<span class=\"hub-title\">{Html.Encode(l.Title)}</span>");
            if (l.Grade.Length > 0)
            {
                sb.Append($"<span class=\"badge {l.GradeClass}\">{Html.Encode(l.Grade)}</span>");
            }
            sb.Append("<span class=\"hub-go\">Open →</span>");
            sb.Append("</div>");
            sb.Append($"<p>{Html.Encode(l.Summary)}</p>");
            // Stats is null on a default-constructed Link (record struct), not just empty.
            var stats = l.Stats ?? [];
            if (stats.Count > 0)
            {
                sb.Append("<div class=\"hub-stats\">");
                foreach (var s in stats)
                {
                    sb.Append($"<span><b>{Html.Encode(s.Value)}</b> {Html.Encode(s.Label)}</span>");
                }
                sb.Append("</div>");
            }
            sb.Append("</a>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>Who owns this and what it does, from the authored descriptions sidecar. First on
    /// the page when present, because a stakeholder opening this needs "whose is it" before any
    /// number on it means anything. Omitted entirely when no sidecar was authored.</summary>
    private static string OwnerPanel(string owner, IReadOnlyList<string> capabilities)
    {
        if (owner.Length == 0 && capabilities.Count == 0) { return ""; }

        var sb = new StringBuilder();
        sb.Append("<div class=\"panel\">");
        if (owner.Length > 0)
        {
            sb.Append($"<h2>Owned by {Html.Encode(owner)}</h2>");
        }
        else
        {
            sb.Append("<h2>What this does</h2>");
        }
        if (capabilities.Count > 0)
        {
            sb.Append("<div class=\"chip-row\">");
            foreach (var c in capabilities)
            {
                sb.Append($"<span class=\"badge accent\">{Html.Encode(c)}</span>");
            }
            sb.Append("</div>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>Both grades side by side. The hub is the only page that can show them together,
    /// and "code is healthy but its database is failing" is exactly the reading a combined-mode
    /// site exists to make possible. Skipped when no provider graded itself.</summary>
    private static string HealthPanel(IReadOnlyList<Link> links)
    {
        var graded = links.Where(l => l.Grade.Length > 0).ToList();
        if (graded.Count == 0) { return ""; }

        var sb = new StringBuilder();
        sb.Append("<div class=\"panel\"><h2>Health at a glance</h2>");
        // .hub-rows, not .tiles: the grade badge is already on each card above, so this panel earns
        // its space by adding the SIGNAL COUNTS and the scorecard link, not by repeating the badge
        // in a big tile. Not .hub-dbs either, though they look identical — RunnerTests asserts on
        // that class to prove the Code<->SQL panel is absent, and sharing it would break the proof.
        sb.Append("<ul class=\"hub-rows\">");
        foreach (var l in graded)
        {
            sb.Append($"<li><span class=\"badge {l.GradeClass}\">{Html.Encode(l.Grade)}</span> "
                    + $"<strong>{Html.Encode(l.Title)}</strong> "
                    + $"<span class=\"hub-action-src\">{Html.Encode(l.GradeDetail)}</span> "
                    + $"<a href=\"{l.Id}/scorecard.html\" class=\"row-end\">scorecard →</a></li>");
        }
        sb.Append("</ul>");
        sb.Append("<p class=\"note\">Each grade is the worst single signal on that "
                + "subsite's scorecard, so read the failing rows rather than the headline. Grades are heuristic "
                + "and syntax-only — a conversation starter for a review, not a certification.</p>");
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>The merged backlog: what to fix first, across both analysers, in one list. Each
    /// item says which subsite it came from, because "dependency cycle" and "table without a
    /// primary key" are not the same kind of work and land on different people.</summary>
    private static string ActionsPanel(IReadOnlyList<Action> actions)
    {
        if (actions.Count == 0) { return ""; }

        var sb = new StringBuilder();
        sb.Append("<div class=\"panel\"><h2>What to do first</h2>");
        sb.Append("<ol class=\"hub-actions\">");
        foreach (var a in actions)
        {
            sb.Append("<li>");
            sb.Append($"<span class=\"badge {a.SeverityClass}\">{Html.Encode(a.Severity)}</span> ");
            sb.Append($"<a href=\"{Html.Encode(a.Href)}\">{Html.Encode(a.Text)}</a>");
            // NOT .note — that is a bordered callout with its own background and margins, and
            // inlining it here rendered a grey box mid-sentence. Same trap the Ops page hit with
            // .note inside a table cell (continue.md, audience-documentation pass).
            sb.Append($" <span class=\"hub-action-src\">{Html.Encode(a.Source)}</span>");
            sb.Append("</li>");
        }
        sb.Append("</ol></div>");
        return sb.ToString();
    }

    /// <summary>Deep links into each subsite. These exist because the cards cannot hold them: a
    /// card is itself an anchor so the whole tile is clickable, and anchors do not nest. Without
    /// this the hub is a two-click detour to every page anyone actually wants.</summary>
    private static string JumpPanel(IReadOnlyList<Link> links)
    {
        var withPages = links.Where(l => l.Pages is { Count: > 0 }).ToList();
        if (withPages.Count == 0) { return ""; }

        var sb = new StringBuilder();
        sb.Append("<div class=\"panel\"><h2>Jump straight to</h2>");
        foreach (var l in withPages)
        {
            sb.Append($"<p class=\"jump-row\"><strong>{Html.Encode(l.Title)}</strong> ");
            sb.Append(string.Join(" · ", l.Pages!.Select(p =>
                $"<a href=\"{l.Id}/{Html.Encode(p.Href)}\">{Html.Encode(p.Title)}</a>")));
            sb.Append("</p>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>The one thing only the hub can say: which databases the code connects to, and
    /// whether the SQL side of this same run covers them. Skipped entirely when there are none
    /// — an empty panel would imply the join ran and found nothing, which is a different claim.</summary>
    private static string CrossLinkPanel(IReadOnlyList<DbLink> dbLinks)
    {
        if (dbLinks.Count == 0) { return ""; }

        var matched = dbLinks.Count(d => d.Href.Length > 0);
        var sb = new StringBuilder();
        sb.Append("<div class=\"panel\">");
        sb.Append($"<h2>Code ↔ SQL <span class=\"badge accent\">{matched} of {dbLinks.Count} matched</span></h2>");
        sb.Append("<p class=\"lede tight\">Databases the scanned code connects to, "
                + "matched against the SQL model from this same run. A match links straight to that catalog's objects.</p>");
        sb.Append("<ul class=\"hub-dbs\">");
        foreach (var d in dbLinks)
        {
            var label = d.Href.Length > 0
                ? $"<a href=\"{Html.Encode(d.Href)}\">{Html.Encode(d.Label)}</a>"
                : Html.Encode(d.Label);
            sb.Append($"<li><span class=\"badge {d.BadgeClass}\">{Html.Encode(d.Status)}</span> {label}</li>");
        }
        sb.Append("</ul></div>");
        return sb.ToString();
    }
}
