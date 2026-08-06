namespace Arch.Sql.Site;

/// <summary>Shared page shell: sidebar navigation, breadcrumbs, theme toggle, local asset
/// references only (works from file:// with no network). Matches the vendored site.js/site.css
/// contract — same theme localStorage keys, same DOM ids, so those assets work
/// unmodified.</summary>
public static class PageTemplate
{
    public static readonly (string Section, (string Href, string Title, string Icon)[] Items)[] NavSections =
    [
        ("Start", [("index.html", "Overview", "◈"), ("guide.html", "Guide", "❓"), ("explore.html", "Explore", "🔎")]),
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
        var shell = new ShellOptions
        {
            Brand = "ArchSql",
            Nav = NavSections,
            SearchButtonTitle = "Search objects (Ctrl+K)",
            SearchInputPlaceholder = "Search objects…",
            ExtraHead = PrePaintScript,
            ExtraScripts = $"{searchIndexHtml}\n<script src=\"{relRoot}assets/lib/mermaid.min.js\"></script>",
        };
        return PageShell.Render(shell, title, siteName, activeHref, relRoot, breadcrumbsHtml, bodyHtml);
    }

    /// <summary>One interactive diagram card: toolbar (zoom/reset/PNG), pan/zoom stage, the
    /// mermaid source. Adjacency/tooltips are omitted — there is no 3D graph or hover-trace data
    /// to feed them.</summary>
    public static string DiagramBlock(string id, string mermaidSource)
    {
        return $"""
<div class="diagram-card" id="{Html.Encode(id)}" data-png-name="{Html.Encode(id)}">
  <div class="toolbar">
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
  <span class="legend-item"><span class="legend-swatch" style="background:#dcecf9;border-color:#2f6fab"></span>Table</span>
  <span class="legend-item"><span class="legend-swatch round" style="background:#fdf1dc;border-color:#b7791f"></span>View</span>
  <span class="legend-item"><span class="legend-swatch hex" style="background:#f0f0f0;border-color:#8a8a8a"></span>Procedure / function</span>
  <span class="legend-item"><span class="legend-line"></span>Foreign key / reference</span>
  <span class="legend-item"><span class="legend-line dashed"></span>Unresolved / external reference</span>
</div>
</details>
""";
}
