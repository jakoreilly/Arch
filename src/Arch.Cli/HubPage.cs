using System.Text;
using Arch.Core.Web;

namespace Arch.Cli;

/// <summary>The landing page written at outDir/index.html only in combined mode (both
/// providers applied) — links into outDir/code/ and outDir/sql/, each a complete,
/// unmodified site from its own product. Never merges either product's own nav; see
/// plan.md's Phase 5 findings for why (confirmed with the user: nesting over merging).</summary>
public static class HubPage
{
    /// <summary>One link the hub offers — a provider that generated successfully.
    /// Providers that failed are omitted; "partial site beats no site," but a hub link
    /// to a subsite that doesn't exist would be worse than no link at all.</summary>
    public readonly record struct Link(string Id, string Title, string Icon, string Summary);

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

        var items = string.Join("\n", links.Select(l =>
            $"<li><a href=\"{l.Id}/index.html\">{l.Icon} {Html.Encode(l.Title)}</a> — {Html.Encode(l.Summary)}</li>"));
        var body = $"""
<h1>Arch — {Html.Encode(siteName)}</h1>
<p class="note">This folder has more than one kind of content. Pick one to explore, or use the sidebar once you're inside either site.</p>
<ul>
{items}
</ul>
""";

        var html = PageShell.Render(shell, "Arch", siteName, "", "", Html.Crumbs((null, "Arch")), body);
        File.WriteAllText(Path.Combine(outDir, "index.html"), html, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
