using System.Text;
using Arch.Code.Graph;

namespace Arch.Code.Site.Pages;

/// <summary>Pick a start and, optionally, an end; see the shortest honest chain of
/// hops between them. Client-side only (window.ARCH_TRACE) — see TraceDataWriter and
/// assets/site.js's ArchTrace IIFE.</summary>
public static class TracePage
{
    public static string Body(ProjectModel model, string traceJson)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Trace</h1>");
        sb.Append("<p class=\"lede\">Pick a starting point — an endpoint, a class, a method, or a "
                + "file — and, optionally, an end point. Trace shows the shortest chain between them: "
                + "every hop labelled with its evidence, and how many other things it could equally "
                + "have meant. Certain hops are preferred over ambiguous ones automatically.</p>");

        sb.Append("<div id=\"trace-console\">");
        sb.Append("<div class=\"select-row\">"
                + "<div class=\"ac-field\"><input class=\"filter-input\" id=\"trace-from\" type=\"search\" "
                + "autocomplete=\"off\" placeholder=\"Start: a route, class, method, or file…\" "
                + "role=\"combobox\" aria-expanded=\"false\" aria-autocomplete=\"list\" aria-controls=\"trace-from-list\">"
                + "<ul class=\"ac-list palette-results\" id=\"trace-from-list\" role=\"listbox\" hidden></ul></div>"
                + "<div class=\"ac-field\"><input class=\"filter-input\" id=\"trace-to\" type=\"search\" "
                + "autocomplete=\"off\" placeholder=\"End: leave blank to follow everything downstream\" "
                + "role=\"combobox\" aria-expanded=\"false\" aria-autocomplete=\"list\" aria-controls=\"trace-to-list\">"
                + "<ul class=\"ac-list palette-results\" id=\"trace-to-list\" role=\"listbox\" hidden></ul></div>"
                + "</div>");
        sb.Append("<div class=\"select-row\">"
                + "<label class=\"lf-check\"><input type=\"checkbox\" id=\"trace-imports\" checked> Follow imports</label>"
                + "<label class=\"lf-check\"><input type=\"checkbox\" id=\"trace-calls\" checked> Follow calls</label>"
                + "<label class=\"lf-check\"><input type=\"checkbox\" id=\"trace-data\" checked> Follow data access</label>"
                + "<span class=\"filter-count\" id=\"trace-count\"></span>"
                + "</div>");
        // Filled in by site.js from the embedded node data (real routes/classes from this
        // codebase), the same "land on a real example" idea as Explore's query-example chips —
        // static verbs don't apply here since Trace operates on named entities, not a fixed
        // query vocabulary, so the chips can't be pre-rendered server-side.
        sb.Append("<div class=\"lang-legend\" id=\"trace-examples\" style=\"gap:.4rem;margin:.2rem 0 .6rem\"></div>");

        // Path diagram: empty and hidden at build time — the chain itself isn't known until a
        // user runs a query in the browser. site.js's Trace IIFE fills in the Mermaid source
        // (and the tooltip/href/adjacency maps) and calls window.ArchViewer.rerenderCard on every
        // new from/to result, unhiding this card; it hides it again for the open-ended "everything
        // downstream" case, which can run into the hundreds of nodes — too many for a readable
        // flowchart (that case gets a link into the 3D graph's own flow-trace instead). Emitting
        // this literal diagram-card markup, rather than loading mermaid.min.js unconditionally, is
        // what makes PageTemplate.Render's needsMermaid sniff (which greps bodyHtml for exactly
        // this class) pick this page up despite the real diagram not existing yet. The seed
        // Mermaid text is "flowchart LR" alone (no nodes) rather than blank — a valid, empty
        // flowchart, since SiteSmokeTests asserts every .mermaid-src on the site starts with
        // "flowchart " even before site.js overwrites it with the real path.
        sb.Append(PageTemplate.DiagramBlock("trace-diagram", new Diagram("flowchart LR", new Dictionary<string, string>(), new Dictionary<string, string>()),
            "trace-path", hidden: true, deferred: true, forceSmall: true));

        // Pre-rendered so the "nothing typed yet" affordance is visible even without
        // JavaScript; site.js's ArchTrace re-renders the identical markup on load once the
        // inputs are wired up — a no-op visually, matching this repo's static-fallback
        // discipline elsewhere (e.g. the mermaid-src <pre> fallback in DiagramBlock).
        sb.Append("<div id=\"trace-results\"><div class=\"panel empty-state\"><div class=\"big\">🧭</div>"
                + "<p>Type a class, method, route, or file name above to trace from it.</p></div></div>");
        sb.Append("</div>");
        sb.Append("<script>window.ARCH_TRACE=").Append(traceJson).Append(";</script>");
        return sb.ToString();
    }
}
