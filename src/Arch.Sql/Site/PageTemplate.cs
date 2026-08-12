namespace Arch.Sql.Site;

/// <summary>Shared page shell: sidebar navigation, breadcrumbs, theme toggle, local asset
/// references only (works from file:// with no network). Matches the vendored site.js/site.css
/// contract — same theme localStorage keys, same DOM ids, so those assets work
/// unmodified.</summary>
public static class PageTemplate
{
    public static readonly (string Section, (string Href, string Title, string Icon)[] Items)[] NavSections =
    [
        ("Start", [("index.html", "Overview", "◈"), ("guide.html", "Guide", "📖"), ("explore.html", "Explore", "🔎")]),
        ("Schema", [("objects.html", "Objects", "❖"), ("domains.html", "Domains", "🗂"), ("er.html", "ER Diagram", "⬡"), ("relationships.html", "Relationships", "🔗"), ("dependencies.html", "Dependencies", "⇄"), ("graph.html", "3D Graph", "🧊"), ("crud.html", "CRUD Matrix", "▦")]),
        ("Health", [("lint.html", "Lint", "◉"), ("scorecard.html", "Scorecard", "✔"), ("metrics.html", "Metrics", "📐"), ("impact.html", "Impact", "☢"), ("activity.html", "Activity", "🔥"), ("indexes.html", "Indexes", "🗄"), ("drift.html", "Schema Diff", "🕓")]),
        ("Reference", [("config.html", "Config & Secrets", "🔑")]),
    ];

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

    /// <param name="relRoot">"" for root pages, "../" for pages under files/.</param>
    /// <param name="searchIndexHtml">The Ctrl+K palette's window.ARCH_SEARCH_INDEX script tag
    /// (SearchIndex.ScriptTag); "" disables search on this page (kept optional so every existing
    /// caller keeps compiling).</param>
    public static string Render(string title, string siteName, string activeHref, string relRoot, string breadcrumbsHtml, string bodyHtml, string searchIndexHtml = "")
    {
        // Mermaid is a 3.3 MB vendored bundle, parsed on every navigation for nothing on pages
        // with no diagram at all (Impact, Lint, Config & Secrets, ...). bodyHtml is this page's
        // actual rendered markup, so sniffing it for a real diagram card is exact — every
        // diagram, including deferred ones like object.html's neighborhood card, passes through
        // this file's own DiagramBlock, which always emits this literal class. See site.js's
        // hasMermaid guard, required for pages that now omit the script.
        var needsMermaid = bodyHtml.Contains("class=\"diagram-card\"", StringComparison.Ordinal);
        var mermaidScript = needsMermaid ? $"<script src=\"{relRoot}assets/lib/mermaid.min.js\"></script>" : "";
        var shell = new ShellOptions
        {
            // See Arch.Code's PageTemplate for why this is "Arch SQL" and not "ArchSql".
            Brand = "Arch SQL",
            Nav = NavSections,
            SearchButtonTitle = "Search objects (Ctrl+K)",
            SearchInputPlaceholder = "Search objects…",
            ExtraHead = PrePaintScript,
            ExtraScripts = $"{searchIndexHtml}\n{mermaidScript}",
        };
        return PageShell.Render(shell, title, siteName, activeHref, relRoot, breadcrumbsHtml, bodyHtml);
    }

    /// <summary>One interactive diagram card: toolbar (zoom/reset/PNG), pan/zoom stage, the
    /// mermaid source. Adjacency/tooltips are omitted — there is no 3D graph or hover-trace data
    /// to feed them. <paramref name="trimNotice"/> renders a visible .diagram-trim banner (the
    /// Arch.Code precedent) when the diagram was capped for readability; null for an uncapped one.</summary>
    public static string DiagramBlock(string id, string mermaidSource, string? trimNotice = null)
    {
        var trimBanner = trimNotice is null ? "" : $"<p class=\"note diagram-trim\">{Html.Encode(trimNotice)}</p>";
        return $"""
<div class="diagram-card" id="{Html.Encode(id)}" data-png-name="{Html.Encode(id)}">
  {trimBanner}<div class="toolbar">
    <button class="btn" data-act="zoom-in" type="button" title="Zoom in">+</button>
    <button class="btn" data-act="zoom-out" type="button" title="Zoom out">&minus;</button>
    <button class="btn" data-act="zoom-reset" type="button" title="Reset view">Reset</button>
    <button class="btn" data-act="fit" type="button" title="Fit diagram to the visible area">Fit</button>
    <button class="btn btn-primary" data-act="png" type="button" title="Download this diagram as a PNG image">⬇ PNG</button>
    <button class="btn" data-act="svg" type="button" title="Download this diagram as a scalable SVG">⬇ SVG</button>
    <button class="btn" data-act="copy" type="button" title="Copy the Mermaid source of this diagram to the clipboard">Copy Mermaid</button>
    <span class="tb-hint">Scroll to zoom · drag to pan · click a node to open it</span>
  </div>
  <div class="stage"><pre class="mermaid-src" hidden>{Html.Encode(mermaidSource)}</pre><div class="render-target"></div></div>
</div>
""";
    }

    public static string Legend() => """
<details class="legend"><summary>What the shapes and colours mean</summary>
<div class="legend-grid">
  <span class="legend-item"><span class="legend-swatch" style="background:var(--accent-soft);border-color:var(--accent)"></span>Table</span>
  <span class="legend-item"><span class="legend-swatch round" style="background:var(--warn-soft);border-color:var(--warn)"></span>View</span>
  <span class="legend-item"><span class="legend-swatch hex" style="background:var(--bg-sunken);border-color:var(--text-soft)"></span>Procedure / function</span>
  <span class="legend-item"><span class="legend-line"></span>Foreign key / reference</span>
  <span class="legend-item"><span class="legend-line dashed"></span>Unresolved / external reference</span>
</div>
</details>
""";
}
