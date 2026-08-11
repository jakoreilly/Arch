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
                + "<input class=\"filter-input\" id=\"trace-from\" type=\"search\" autocomplete=\"off\" "
                + "placeholder=\"Start: a route, class, method, or file…\">"
                + "<input class=\"filter-input\" id=\"trace-to\" type=\"search\" autocomplete=\"off\" "
                + "placeholder=\"End: leave blank to follow everything downstream\">"
                + "</div>");
        sb.Append("<div class=\"select-row\">"
                + "<label class=\"lf-check\"><input type=\"checkbox\" id=\"trace-imports\" checked> Follow imports</label>"
                + "<label class=\"lf-check\"><input type=\"checkbox\" id=\"trace-calls\" checked> Follow calls</label>"
                + "<label class=\"lf-check\"><input type=\"checkbox\" id=\"trace-data\" checked> Follow data access</label>"
                + "</div>");

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
