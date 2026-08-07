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
    public readonly record struct Link(string Id, string Title, string Icon, string Summary, IReadOnlyList<Stat> Stats)
    {
        public Link(string id, string title, string icon, string summary)
            : this(id, title, icon, summary, []) { }
    }

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

    /// <param name="sourcePath">The scanned folder, shown so the page says what it was built
    /// from; "" omits that clause.</param>
    /// <param name="generatedOn">Pre-formatted date, invariant culture; "" omits it.</param>
    /// <param name="dbLinks">Cross-layer join outcomes, empty when the code side referenced no
    /// databases (or only one provider ran) — the panel is skipped entirely then rather than
    /// rendering an empty shell.</param>
    public static void Write(
        string outDir,
        string siteName,
        IReadOnlyList<Link> links,
        string sourcePath,
        string generatedOn,
        IReadOnlyList<DbLink> dbLinks)
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
        sb.Append($"<h1>Arch — {Html.Encode(siteName)}</h1>");
        sb.Append("<p class=\"lede\">This folder holds more than one kind of content, so Arch generated a "
                + "complete site for each and left them side by side. Pick one to explore; the sidebar in "
                + "either site has its own full navigation.</p>");
        sb.Append(Provenance(sourcePath, generatedOn));
        sb.Append(Cards(links));
        sb.Append(CrossLinkPanel(dbLinks));
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

    /// <summary>The one thing only the hub can say: which databases the code connects to, and
    /// whether the SQL side of this same run covers them. Skipped entirely when there are none
    /// — an empty panel would imply the join ran and found nothing, which is a different claim.</summary>
    private static string CrossLinkPanel(IReadOnlyList<DbLink> dbLinks)
    {
        if (dbLinks.Count == 0) { return ""; }

        var matched = dbLinks.Count(d => d.Href.Length > 0);
        var sb = new StringBuilder();
        sb.Append("<div class=\"panel\">");
        sb.Append($"<h2 style=\"margin-top:0\">Code ↔ SQL <span class=\"badge accent\">{matched} of {dbLinks.Count} matched</span></h2>");
        sb.Append("<p class=\"lede\" style=\"margin:.2rem 0 .6rem\">Databases the scanned code connects to, "
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
