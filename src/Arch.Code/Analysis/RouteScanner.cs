using System.Text.RegularExpressions;
using Arch.Code.Graph;

namespace Arch.Code.Analysis;

/// <summary>Composes HTTP endpoints from the raw attribute text CSharpSyntaxAnalyzer
/// captured on TypeInfo/MethodInfo. Interprets ASP.NET convention (route-template
/// composition, [controller] substitution, verb-from-attribute-name); does no
/// semantic resolution — a route built from anything but a string literal is reported
/// as unresolved with its evidence, never silently dropped or guessed at.</summary>
public static class RouteScanner
{
    private static readonly (string Attr, string Verb)[] VerbAttributes =
    [
        ("HttpGet", "GET"), ("HttpPost", "POST"), ("HttpPut", "PUT"),
        ("HttpDelete", "DELETE"), ("HttpPatch", "PATCH"), ("HttpHead", "HEAD"),
    ];

    private static readonly Regex AttrArg = new(@"^\w+\(""(?<val>[^""]*)""\)$", RegexOptions.Compiled);

    public static List<HttpEndpoint> Build(IReadOnlyList<FileNode> files)
    {
        var endpoints = new List<HttpEndpoint>();
        foreach (var file in files)
        {
            foreach (var type in file.Types)
            {
                var typePrefix = RoutePrefix(type);
                var isController = type.Attributes.Any(a => a.StartsWith("ApiController", StringComparison.Ordinal))
                    || type.Name.EndsWith("Controller", StringComparison.Ordinal);
                if (!isController) { continue; }

                foreach (var m in type.Methods)
                {
                    var endpoint = BuildFromAttributes(file, type, typePrefix, m);
                    if (endpoint is not null) { endpoints.Add(endpoint); }
                }
            }
            // Minimal-API registrations (app.MapGet/MapPost/...) live in top-level
            // statements or Program.cs's <main> — CSharpSyntaxAnalyzer's CombinedWalker
            // recognises them during the same tree-walk as invocations/data-access and
            // records them on the synthesized "<top-level>" type's "<main>" method.
            var topLevel = file.Types.SingleOrDefault(t => t.Kind == "top-level");
            var main = topLevel?.Methods.SingleOrDefault(m => m.Name == "<main>");
            if (main is not null)
            {
                foreach (var map in main.MapCalls)
                {
                    endpoints.Add(new HttpEndpoint
                    {
                        Verb = map.Verb, Route = map.Route.TrimStart('/'), Slug = file.Slug,
                        TypeName = topLevel!.Name, MethodName = main.Name, Line = map.Line,
                        Source = "minimal-api",
                    });
                }
            }
        }
        return endpoints
            .OrderBy(e => e.Slug, StringComparer.Ordinal).ThenBy(e => e.Line)
            .ToList();
    }

    private static HttpEndpoint? BuildFromAttributes(FileNode file, TypeInfo type, string typePrefix, MethodInfo m)
    {
        foreach (var (attrName, verb) in VerbAttributes)
        {
            var attr = m.Attributes.FirstOrDefault(a => a.StartsWith(attrName, StringComparison.Ordinal));
            if (attr is null) { continue; }
            var hasArgs = attr.Contains('(', StringComparison.Ordinal);
            var template = ExtractLiteralArg(attr);
            if (hasArgs && template is null)
            {
                // Evidence of a route exists (a verb attribute with an argument), but the
                // argument isn't a bare string literal — reported as unresolved, never
                // silently dropped or guessed at.
                return new HttpEndpoint
                {
                    Verb = verb, Route = "", Slug = file.Slug, TypeName = type.Name,
                    MethodName = m.Name, Line = m.StartLine, Source = "unresolved",
                };
            }
            var route = ComposeRoute(typePrefix, template, type.Name);
            return new HttpEndpoint
            {
                Verb = verb, Route = route, Slug = file.Slug, TypeName = type.Name,
                MethodName = m.Name, Line = m.StartLine, Source = "attribute",
            };
        }
        // [ApiController] without an explicit verb attribute: convention route from
        // the method-name prefix (Get.../Post.../Put.../Delete...), same rule ASP.NET
        // API-controller conventions use for a same-named default action.
        if (type.Attributes.Any(a => a.StartsWith("ApiController", StringComparison.Ordinal)))
        {
            var convention = VerbAttributes.FirstOrDefault(v => m.Name.StartsWith(v.Verb[..1] + v.Verb[1..].ToLowerInvariant(), StringComparison.Ordinal));
            if (convention != default)
            {
                return new HttpEndpoint
                {
                    Verb = convention.Verb, Route = typePrefix, Slug = file.Slug,
                    TypeName = type.Name, MethodName = m.Name, Line = m.StartLine,
                    Source = "convention",
                };
            }
        }
        return null;
    }

    /// <summary>Route text from a type's own [Route("...")] attribute, with the
    /// "[controller]" token substituted for the type name minus a trailing "Controller"
    /// — the one ASP.NET routing convention common enough to be worth composing.</summary>
    private static string RoutePrefix(TypeInfo type)
    {
        var routeAttr = type.Attributes.FirstOrDefault(a => a.StartsWith("Route", StringComparison.Ordinal));
        if (routeAttr is null) { return ""; }
        var template = ExtractLiteralArg(routeAttr) ?? "";
        var controllerName = type.Name.EndsWith("Controller", StringComparison.Ordinal)
            ? type.Name[..^"Controller".Length] : type.Name;
        // ASP.NET's [controller] token convention is paired with lowercase URLs in the vast
        // majority of real setups (RouteOptions.LowercaseUrls); matching that here rather
        // than substituting the type name's own casing verbatim.
        return template.Replace("[controller]", controllerName.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);
    }

    private static string ComposeRoute(string typePrefix, string? methodTemplate, string typeName)
    {
        if (methodTemplate is null) { return typePrefix; }
        if (methodTemplate.StartsWith('/')) { return methodTemplate.TrimStart('/'); } // absolute overrides the prefix
        return typePrefix.Length == 0 ? methodTemplate : $"{typePrefix.TrimEnd('/')}/{methodTemplate}";
    }

    /// <summary>The single string-literal argument of a one-argument attribute
    /// ("Route(\"api/orders\")" -&gt; "api/orders"), or null when the argument isn't a
    /// bare string literal (a constant, an interpolation, no argument at all) — the
    /// exact boundary of what this scanner can resolve without a semantic model.</summary>
    private static string? ExtractLiteralArg(string attrText)
    {
        var m = AttrArg.Match(attrText);
        return m.Success ? m.Groups["val"].Value : null;
    }
}
