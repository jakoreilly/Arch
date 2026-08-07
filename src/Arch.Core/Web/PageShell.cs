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
                // aria-hidden on the icon: these glyphs are decoration duplicating the label
                // beside them. Without it a screen reader announces "black diamond Overview",
                // "telephone Call Graph" on every page. HubPage already does this for .hub-mark.
                var current = href == activeHref ? " aria-current=\"page\"" : "";
                nav.Append($"<a href=\"{relRoot}{href}\"{active}{current}><span class=\"nav-icon\" aria-hidden=\"true\">{icon}</span>{Html.Encode(navTitle)}</a>\n");
            }
        }

        // "" degrades to no search UI at all (see ShellOptions.SearchInputPlaceholder) rather
        // than a button that opens an empty palette. Both fragments carry their own trailing/
        // leading newline so the "" case leaves no blank line behind.
        var hasSearch = shell.SearchInputPlaceholder.Length > 0;
        var searchButtonHtml = hasSearch
            ? $"    <button class=\"btn search-open\" id=\"search-open\" type=\"button\" title=\"{Html.Encode(shell.SearchButtonTitle)}\">🔍 Search <kbd>Ctrl K</kbd></button>\n"
            : "";
        // The palette is a modal: role/aria-modal so assistive tech announces it as one and
        // treats the page behind as inert, and the combobox/listbox pair so the highlighted
        // row is actually announced (site.js keeps aria-activedescendant in step). Without
        // these it was a plain div of unlabelled <li>, silent to a screen reader.
        var paletteHtml = hasSearch
            ? "<div class=\"palette-overlay\" id=\"palette\" hidden data-rel-root=\"" + relRoot + "\" role=\"dialog\" aria-modal=\"true\" aria-label=\"Search\">\n"
              + "  <div class=\"palette\">\n"
              + "    <input type=\"text\" id=\"palette-input\" placeholder=\"" + Html.Encode(shell.SearchInputPlaceholder) + "\" autocomplete=\"off\" spellcheck=\"false\""
              + " role=\"combobox\" aria-expanded=\"true\" aria-controls=\"palette-results\" aria-autocomplete=\"list\" aria-label=\"" + Html.Encode(shell.SearchInputPlaceholder) + "\">\n"
              + "    <ul class=\"palette-results\" id=\"palette-results\" role=\"listbox\" aria-label=\"Search results\"></ul>\n"
              + "    <div class=\"palette-foot\">↑↓ navigate · Enter open · Esc close</div>\n"
              + "  </div>\n"
              + "</div>\n"
            : "";

        // "Page — Site" everywhere except when the two are the same word, which produced titles
        // like "Arch — Arch" on the hub of a folder named Arch. One word is the correct title
        // there; the suffix exists to disambiguate, and it cannot disambiguate itself.
        var docTitle = string.Equals(title, siteName, StringComparison.Ordinal)
            ? Html.Encode(title)
            : $"{Html.Encode(title)} — {Html.Encode(siteName)}";

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{{docTitle}}</title>
<link rel="stylesheet" href="{{relRoot}}assets/site.css">
{{shell.ExtraHead}}
</head>
<body>
<div class="layout">
  <button class="nav-toggle" id="nav-toggle" type="button" aria-label="Open menu" aria-expanded="false" aria-controls="sidebar">☰</button>
  <div class="nav-overlay" id="nav-overlay" hidden></div>
  <aside class="sidebar" id="sidebar">
    <div class="brand"><span class="brand-mark" aria-hidden="true">◆</span><div><div class="brand-name">{{shell.Brand}}</div><div class="brand-sub">{{Html.Encode(siteName)}}</div></div></div>
{{searchButtonHtml}}    <nav aria-label="Main">
{{nav}}    </nav>
    <div class="sidebar-foot">
      <button class="btn theme-toggle" id="theme-toggle" type="button" title="Switch between light and dark theme">◐ Theme</button>{{shell.ExtraFooterButtons}}
      <span class="sr-only" id="theme-status" role="status"></span>
    </div>
  </aside>
  <main class="content">
    <nav class="breadcrumbs" aria-label="Breadcrumb">{{breadcrumbsHtml}}</nav>
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
