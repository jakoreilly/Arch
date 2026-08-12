using System.Text;
using Arch.Code.Graph;

namespace Arch.Code.Site.Pages;

/// <summary>Public API surface — the contract other code can depend on — grouped by namespace,
/// plus "critical paths": how execution/dependencies reach the most central files. Public types
/// (and, for classes, their public methods; interface members are public by definition) are what
/// a reviewer treats as the stable boundary of each module. First-party code only.</summary>
public static class ApiSurfacePage
{
    public static string Body(ProjectModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>API Surface</h1>");
        sb.Append("<p class=\"lede\">The <strong>public contract</strong> of the codebase: public types grouped by "
                + "namespace, and their public members. This is what other modules (and consumers) can depend on — the "
                + "surface you must keep stable. Private/internal detail is omitted. Tests, fixtures and vendored code are excluded.</p>");

        var files = model.Files.Where(Analysis.CodebaseStats.IsFirstParty).ToList();
        var bySlug = model.Files.ToDictionary(f => f.Slug, StringComparer.Ordinal);

        // Public types with the file they live in.
        var publicTypes = files
            .SelectMany(f => f.Types.Where(IsPublicType).Select(t => (File: f, Type: t)))
            .ToList();

        var publicMemberCount = publicTypes.Sum(x => PublicMethods(x.Type).Count);

        sb.Append("<div class=\"tiles\">");
        Tile(sb, publicTypes.Count.ToString("N0"), "Public types");
        Tile(sb, publicMemberCount.ToString("N0"), "Public methods");
        Tile(sb, publicTypes.Select(x => x.Type.Namespace).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).Count().ToString("N0"), "Namespaces");
        sb.Append("</div>");

        AppendCriticalPaths(sb, model, bySlug);
        AppendEndpoints(sb, model, bySlug);
        AppendDataAccess(sb, model, bySlug);

        // Public surface grouped by namespace.
        sb.Append("<h2>Public types by namespace</h2>");
        if (publicTypes.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">⧉</div>"
                    + "<p>No public types were detected. Either this codebase has no C#, or its types are all internal/private — "
                    + "there is no cross-module public surface to document.</p></div>");
            return sb.ToString();
        }

        var groups = publicTypes
            .GroupBy(x => x.Type.Namespace.Length > 0 ? x.Type.Namespace : "(no namespace)", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var g in groups)
        {
            sb.Append($"<h3>{Html.Encode(g.Key)} <span class=\"badge\">{g.Count()} type(s)</span></h3>");
            sb.Append("<div class=\"panel\"><ul class=\"member-list\" style=\"font-family:inherit\">");
            var first = true;
            foreach (var (file, type) in g.OrderBy(x => x.Type.Name, StringComparer.Ordinal))
            {
                var style = first ? " style=\"border-top:none\"" : "";
                first = false;
                var kind = Html.Encode(type.Kind);
                var bases = type.BaseTypes.Count > 0 ? " : " + Html.Encode(string.Join(", ", type.BaseTypes)) : "";
                var methods = PublicMethods(type);
                var memberSummary = methods.Count > 0
                    ? " <span class=\"badge\">" + methods.Count + " public method(s)</span>"
                    : "";
                sb.Append($"<li{style}><span class=\"badge accent\">{kind}</span> "
                        + $"<a href=\"files/{file.Slug}.html\"><strong>{Html.Encode(type.Name)}</strong></a>{Html.Encode(bases)}{memberSummary}");
                if (type.XmlSummary.Length > 0)
                {
                    sb.Append($"<div class=\"note\" style=\"margin:.2rem 0 0\">{Html.Encode(type.XmlSummary)}</div>");
                }
                if (methods.Count > 0)
                {
                    sb.Append("<div style=\"margin:.3rem 0 0;color:var(--text-soft);font-size:.85rem\">");
                    foreach (var m in methods.OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.Signature, StringComparer.Ordinal))
                    {
                        sb.Append($"<div><code>{Html.Encode(m.Signature)}</code></div>");
                    }
                    sb.Append("</div>");
                }
                sb.Append("</li>");
            }
            sb.Append("</ul></div>");
        }
        return sb.ToString();
    }

    /// <summary>Critical paths: for the most central files, the shortest MULTI-HOP chain from an
    /// entry point (a file nothing imports) that reaches it — the code path a reader follows to
    /// get there. A key file one hop from an entry point isn't a "chain"; it's said plainly
    /// instead of dressed up as a two-node path, and a file no entry point reaches at all is
    /// distinguished from a file that genuinely IS an entry point — both used to render the same
    /// "entry point / root" badge, which was simply wrong for the unreachable case.</summary>
    private static void AppendCriticalPaths(StringBuilder sb, ProjectModel model, Dictionary<string, FileNode> bySlug)
    {
        var key = Analysis.ImportanceScorer.Rank(model, 8).Where(s => Analysis.CodebaseStats.IsFirstParty(s.File)).ToList();
        // Build the dependency graph once (not per file) and look each key file's path up.
        var paths = Analysis.CriticalPaths.AllToKeyFiles(model, 8)
            .ToDictionary(p => p.TargetSlug, p => p.Nodes, StringComparer.Ordinal);
        var entryPoints = new HashSet<string>(Analysis.CriticalPaths.EntryPoints(model), StringComparer.Ordinal);
        sb.Append($"<h2>Critical paths {Glossary.Info("critical-path")} <span class=\"badge accent\">to key files</span></h2>");
        if (key.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">↝</div>"
                    + "<p>No dependency links were detected, so there are no code paths to trace yet.</p></div>");
            return;
        }
        sb.Append("<p class=\"lede\">How the code reaches the files that matter most: the shortest multi-hop chain of "
                + "imports from an entry point (a file nothing else imports) to each key file, when one exists. Read "
                + "left-to-right to follow the dependency path in. Want the full story behind a one-hop entry — "
                + "including calls and data access, not just imports? Open <a href=\"trace.html\">Trace</a>.</p>");
        sb.Append("<div class=\"panel\"><ul class=\"member-list\" style=\"font-family:inherit\">");
        var first = true;
        foreach (var s in key)
        {
            var style = first ? " style=\"border-top:none\"" : "";
            first = false;
            paths.TryGetValue(s.File.Slug, out var path);
            string body;
            if (path is { Count: >= 3 })
            {
                body = string.Join(" <span class=\"crumb-sep\">→</span> ",
                    path.Select(slug => bySlug.TryGetValue(slug, out var f)
                        ? $"<a href=\"files/{f.Slug}.html\">{Html.Encode(f.RelPath.Split('/')[^1])}</a>"
                        : Html.Encode(slug)));
            }
            else if (path is { Count: 2 })
            {
                // A direct entry->target edge is not a multi-hop chain — say so rather than
                // rendering a two-node "path" that reads as a diagram bug.
                var entryLink = bySlug.TryGetValue(path[0], out var ef)
                    ? $"<a href=\"files/{ef.Slug}.html\">{Html.Encode(ef.RelPath.Split('/')[^1])}</a>"
                    : Html.Encode(path[0]);
                var targetLink = $"<a href=\"files/{s.File.Slug}.html\">{Html.Encode(s.File.RelPath.Split('/')[^1])}</a>";
                body = $"{targetLink} <span class=\"badge\">direct import</span> — no multi-hop path; "
                     + $"imported directly by entry point {entryLink}.";
            }
            else if (entryPoints.Contains(s.File.Slug))
            {
                body = $"<a href=\"files/{s.File.Slug}.html\">{Html.Encode(s.File.RelPath.Split('/')[^1])}</a> "
                     + "<span class=\"badge\">entry point / root</span>";
            }
            else
            {
                body = $"<a href=\"files/{s.File.Slug}.html\">{Html.Encode(s.File.RelPath.Split('/')[^1])}</a> "
                     + "<span class=\"badge warn\">no entry point reaches this file</span>";
            }
            sb.Append($"<li{style}>{body}</li>");
        }
        sb.Append("</ul></div>");
    }

    private static void AppendEndpoints(StringBuilder sb, ProjectModel model, Dictionary<string, FileNode> bySlug)
    {
        var endpoints = model.Endpoints
            .Where(e => bySlug.TryGetValue(e.Slug, out var f) && Analysis.CodebaseStats.IsFirstParty(f))
            .ToList();
        sb.Append("<h2>HTTP endpoints</h2>");
        if (endpoints.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">⇥</div>"
                    + "<p>No HTTP endpoints were detected — either this codebase has no web layer, "
                    + "or its routing isn't attribute-based, minimal-API, or ASP.NET convention-routed.</p></div>");
            return;
        }
        sb.Append("<p class=\"lede\">Routes inferred from attribute routing, minimal APIs, and ASP.NET "
                + "convention. <span class=\"badge\">attribute</span>/<span class=\"badge\">minimal-api</span> "
                + "routes are read directly from source; <span class=\"badge warn\">convention</span> is a "
                + "guessed verb with no explicit route attribute; <span class=\"badge warn\">unresolved</span> "
                + "means evidence of a route exists but its template could not be read (built from a constant, "
                + "not a literal).</p>");
        sb.Append("<table class=\"grid sortable\" data-page-size=\"20\"><thead><tr>"
                + "<th>Verb</th><th>Route</th><th>Handler</th><th>Source</th></tr></thead><tbody>");
        foreach (var e in endpoints.OrderBy(e => e.Route, StringComparer.Ordinal).ThenBy(e => e.Verb, StringComparer.Ordinal))
        {
            var f = bySlug[e.Slug];
            var badgeClass = e.Source is "attribute" or "minimal-api" ? "" : "warn";
            sb.Append($"<tr><td><code>{Html.Encode(e.Verb.Length > 0 ? e.Verb : "?")}</code></td>"
                    + $"<td><code>{Html.Encode(e.Route.Length > 0 ? "/" + e.Route : "(unresolved)")}</code></td>"
                    + $"<td><a href=\"files/{f.Slug}.html\">{Html.Encode(e.TypeName)}.{Html.Encode(e.MethodName)}</a></td>"
                    + $"<td><span class=\"badge {badgeClass}\">{Html.Encode(e.Source)}</span></td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static void AppendDataAccess(StringBuilder sb, ProjectModel model, Dictionary<string, FileNode> bySlug)
    {
        var refs = model.DataAccess
            .Where(d => bySlug.TryGetValue(d.Slug, out var f) && Analysis.CodebaseStats.IsFirstParty(f))
            .ToList();
        sb.Append("<h2>Data access</h2>");
        if (refs.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">⛁</div>"
                    + "<p>No database reads or writes were detected — either this codebase has no data-access "
                    + "layer, or it doesn't use a pattern this scan recognises (raw SQL literals, Dapper, EF Core, "
                    + "stored procedures).</p></div>");
            return;
        }
        sb.Append("<p class=\"lede\">Places in the code that read or write a named database object, detected "
                + "by pattern — not by compiling or connecting to a database. A blind spot means the statement is "
                + "clearly SQL but the object name is built at runtime (string interpolation or concatenation) and "
                + "could not be read.</p>");
        sb.Append("<table class=\"grid sortable\" data-page-size=\"20\"><thead><tr>"
                + "<th>Object</th><th>Ops</th><th>Method</th><th>Evidence</th></tr></thead><tbody>");
        foreach (var d in refs.OrderBy(d => d.ObjectName, StringComparer.Ordinal).ThenBy(d => d.Slug, StringComparer.Ordinal))
        {
            var f = bySlug[d.Slug];
            var objectCell = d.ResolvedObjectId is not null
                ? $"<a href=\"../sql/object.html?id={Uri.EscapeDataString(d.ResolvedObjectId)}\">{Html.Encode(d.ObjectName)}</a>"
                : d.ObjectName.Length > 0 ? $"<code>{Html.Encode(d.ObjectName)}</code>" : "<code>(unknown)</code>";
            var blindSpot = d.IsBlindSpot ? " <span class=\"badge warn\">blind spot</span>" : "";
            sb.Append($"<tr><td>{objectCell}{blindSpot}</td><td><code>{Html.Encode(d.Ops)}</code></td>"
                    + $"<td><a href=\"files/{f.Slug}.html\">{Html.Encode(d.TypeName)}.{Html.Encode(d.MethodName)}</a></td>"
                    + $"<td><span class=\"badge\">{Html.Encode(d.Source)}</span></td></tr>");
        }
        sb.Append("</tbody></table>");
    }

    private static bool IsPublicType(TypeInfo t) =>
        t.Modifiers.Contains("public", StringComparison.Ordinal)
        || t.Kind.Equals("interface", StringComparison.Ordinal) && !t.Modifiers.Contains("internal", StringComparison.Ordinal) && !t.Modifiers.Contains("private", StringComparison.Ordinal);

    /// <summary>Members that form the public surface: for an interface every method is public;
    /// for other types, methods explicitly marked public.</summary>
    private static List<MethodInfo> PublicMethods(TypeInfo t)
    {
        var isInterface = t.Kind.Equals("interface", StringComparison.Ordinal);
        return t.Methods
            .Where(m => (isInterface || m.Modifiers.Contains("public", StringComparison.Ordinal)) && m.Signature.Length > 0)
            .ToList();
    }

    private static void Tile(StringBuilder sb, string num, string label) =>
        sb.Append($"<div class=\"tile\"><div class=\"num\">{num}</div><div class=\"lbl\">{Html.Encode(label)}</div></div>");
}
