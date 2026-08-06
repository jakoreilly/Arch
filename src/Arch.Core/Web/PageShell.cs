using System.Text;

namespace Arch.Core.Web;

/// <summary>Everything the shared page shell needs that differs between products.
/// Every field has a default, so a caller specifies only what it changes.</summary>
public sealed record ShellOptions
{
    /// <summary>Product name in the sidebar brand block ("ArchDiagram", "ArchSql", "Arch").</summary>
    public required string Brand { get; init; }

    /// <summary>Sidebar navigation, grouped into labelled sections (order = display order).
    /// A section whose <c>Section</c> is "" renders its items with no header div — this is
    /// how a caller that wants a flat, unlabelled nav (Landscape, the standalone Diff report)
    /// reproduces that shape without a second code path in <see cref="PageShell"/>.</summary>
    public required IReadOnlyList<(string Section, (string Href, string Title, string Icon)[] Items)> Nav { get; init; }

    /// <summary>The Search button's tooltip text, e.g. "Search files, types and methods (Ctrl+K)".
    /// "" hides the search button and the Ctrl+K palette entirely (a product with no index) —
    /// see <see cref="SearchInputPlaceholder"/>, which is the field actually tested for that.</summary>
    public string SearchButtonTitle { get; init; } = "";

    /// <summary>The Ctrl+K palette input's placeholder text, e.g. "Search files, types, methods…".
    /// "" is the degradation signal: neither the search button nor the palette overlay render.</summary>
    public string SearchInputPlaceholder { get; init; } = "";

    /// <summary>Raw HTML injected just before &lt;/head&gt; — the pre-paint theme script (and,
    /// for a product that has one, anything else that must run before first paint). Trusted,
    /// product-authored markup only; never user data.</summary>
    public string ExtraHead { get; init; } = "";

    /// <summary>Raw HTML injected after the glossary payload and before assets/site.js.
    /// Load-bearing ordering: site.js's IIFEs read the globals these tags define, so
    /// anything they need (search index, source-link config, graph data) must come first.
    /// Trusted, product-authored markup only; never user data.</summary>
    public string ExtraScripts { get; init; } = "";

    /// <summary>Extra buttons in the sidebar footer beside the theme toggle (ArchDiagram's
    /// 🧪 tests toggle). Must include its own leading "\n" plus indentation when non-empty,
    /// so an empty "" leaves no blank line — see the footer-button line in <see cref="Render"/>.</summary>
    public string ExtraFooterButtons { get; init; } = "";
}

/// <summary>The page shell shared by every generated site: sidebar navigation, breadcrumbs,
/// theme toggle, local asset references only (works from file:// with no network). Was a
/// character-identical copy in both products except for the handful of differences
/// <see cref="ShellOptions"/> now carries explicitly.</summary>
public static class PageShell
{
    /// <param name="relRoot">"" for root pages, "../" for pages under files/.</param>
    public static string Render(
        ShellOptions shell,
        string title,
        string siteName,
        string activeHref,
        string relRoot,
        string breadcrumbsHtml,
        string bodyHtml)
    {
        var nav = new StringBuilder();
        foreach (var (section, items) in shell.Nav)
        {
            if (section.Length > 0)
            {
                nav.Append($"<div class=\"nav-section\">{Html.Encode(section)}</div>\n");
            }
            foreach (var (href, navTitle, icon) in items)
            {
                var active = href == activeHref ? " class=\"active\"" : "";
                nav.Append($"<a href=\"{relRoot}{href}\"{active}><span class=\"nav-icon\">{icon}</span>{Html.Encode(navTitle)}</a>\n");
            }
        }

        // "" degrades to no search UI at all (see ShellOptions.SearchInputPlaceholder) rather
        // than a button that opens an empty palette. Both fragments carry their own trailing/
        // leading newline so the "" case leaves no blank line behind.
        var hasSearch = shell.SearchInputPlaceholder.Length > 0;
        var searchButtonHtml = hasSearch
            ? $"    <button class=\"btn search-open\" id=\"search-open\" type=\"button\" title=\"{shell.SearchButtonTitle}\">🔍 Search <kbd>Ctrl K</kbd></button>\n"
            : "";
        var paletteHtml = hasSearch
            ? "<div class=\"palette-overlay\" id=\"palette\" hidden data-rel-root=\"" + relRoot + "\">\n"
              + "  <div class=\"palette\">\n"
              + "    <input type=\"text\" id=\"palette-input\" placeholder=\"" + shell.SearchInputPlaceholder + "\" autocomplete=\"off\" spellcheck=\"false\">\n"
              + "    <ul class=\"palette-results\" id=\"palette-results\"></ul>\n"
              + "    <div class=\"palette-foot\">↑↓ navigate · Enter open · Esc close</div>\n"
              + "  </div>\n"
              + "</div>\n"
            : "";

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{Html.Encode(title)}} — {{Html.Encode(siteName)}}</title>
<link rel="stylesheet" href="{{relRoot}}assets/site.css">
{{shell.ExtraHead}}
</head>
<body>
<div class="layout">
  <button class="nav-toggle" id="nav-toggle" type="button" aria-label="Open menu" aria-expanded="false">☰</button>
  <div class="nav-overlay" id="nav-overlay" hidden></div>
  <aside class="sidebar" id="sidebar">
    <div class="brand"><span class="brand-mark">◆</span><div><div class="brand-name">{{shell.Brand}}</div><div class="brand-sub">{{Html.Encode(siteName)}}</div></div></div>
{{searchButtonHtml}}    <nav>
{{nav}}    </nav>
    <div class="sidebar-foot">
      <button class="btn theme-toggle" id="theme-toggle" type="button" title="Switch between light and dark theme">◐ Theme</button>{{shell.ExtraFooterButtons}}
    </div>
  </aside>
  <main class="content">
    <div class="breadcrumbs">{{breadcrumbsHtml}}</div>
{{bodyHtml}}
  </main>
</div>
<div class="hover-tip" id="hover-tip" hidden></div>
<div class="explain-pop" id="explain-pop" hidden role="dialog" aria-label="Explanation"></div>
<script type="application/json" id="arch-glossary">{{Glossary.Json()}}</script>
{{paletteHtml}}{{shell.ExtraScripts}}
<script src="{{relRoot}}assets/site.js"></script>
</body>
</html>
""";
    }
}
