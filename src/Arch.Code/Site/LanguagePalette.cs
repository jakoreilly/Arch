namespace Arch.Code.Site;

/// <summary>Deterministic per-language colours, shared by the Overview language bar and the
/// Structure treemap so a language reads the same colour everywhere. Unknown languages cycle
/// a small fallback palette via a caller-held index.
///
/// <see cref="Colors"/>/<see cref="Fallback"/> are literal hex — kept ONLY as the input to
/// <see cref="Arch.Code.Site.TreemapRenderer.TextColorFor"/>'s black-vs-white label contrast
/// decision, which needs a resolvable numeric colour and cannot see a CSS variable's
/// theme-dependent value. Every caller that emits an actual page colour must use
/// <see cref="TokenFor(string, ref int)"/>/<see cref="TokenFor(string)"/> instead, which
/// return a <c>var(--lang-*)</c> reference bound to token pairs in site.css — see the
/// "Never emit a raw hex colour from C#" rule in CLAUDE.md.</summary>
public static class LanguagePalette
{
    internal static readonly IReadOnlyDictionary<string, string> Colors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["C#"] = "#2f6fab", ["TypeScript/JavaScript"] = "#e8b73a", ["Python"] = "#3572A5",
        ["PowerShell"] = "#6b46c1", ["SQL"] = "#c0392b", ["HTML"] = "#e34c26", ["CSS"] = "#563d7c",
        ["JSON"] = "#8a8a8a", ["YAML"] = "#6fbf73", ["XML"] = "#0060ac", ["Markdown"] = "#4a4a4a",
        ["MSBuild"] = "#68217a", ["Razor"] = "#512bd4", ["Protobuf"] = "#4d7e65",
    };

    internal static readonly string[] Fallback = ["#1f8a8a", "#b7791f", "#7a5195", "#ef5675", "#488f31"];

    private static readonly IReadOnlyDictionary<string, string> Tokens = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["C#"] = "--lang-csharp", ["TypeScript/JavaScript"] = "--lang-tsjs", ["Python"] = "--lang-python",
        ["PowerShell"] = "--lang-powershell", ["SQL"] = "--lang-sql", ["HTML"] = "--lang-html", ["CSS"] = "--lang-css",
        ["JSON"] = "--lang-json", ["YAML"] = "--lang-yaml", ["XML"] = "--lang-xml", ["Markdown"] = "--lang-markdown",
        ["MSBuild"] = "--lang-msbuild", ["Razor"] = "--lang-razor", ["Protobuf"] = "--lang-protobuf",
    };

    private static readonly string[] FallbackTokens =
        ["--lang-fallback-1", "--lang-fallback-2", "--lang-fallback-3", "--lang-fallback-4", "--lang-fallback-5"];

    /// <summary>CSS <c>var(--lang-*)</c> reference for a language; unknowns take the next
    /// fallback token (index advanced). Use for the Overview language bar.</summary>
    public static string TokenFor(string language, ref int fallbackIndex) =>
        $"var({(Tokens.TryGetValue(language, out var t) ? t : FallbackTokens[fallbackIndex++ % FallbackTokens.Length])})";

    /// <summary>Stable <c>var(--lang-*)</c> reference for a language with no shared fallback
    /// index (treemap use): unknown languages hash to a fallback slot so the choice is
    /// deterministic per language.</summary>
    public static string TokenFor(string language)
    {
        if (Tokens.TryGetValue(language, out var t)) { return $"var({t})"; }
        var h = 0;
        foreach (var ch in language) { h = (h * 31 + ch) & 0x7fffffff; }
        return $"var({FallbackTokens[h % FallbackTokens.Length]})";
    }

    /// <summary>Literal hex for a language — ONLY for luminance/contrast math
    /// (<see cref="Arch.Code.Site.TreemapRenderer.TextColorFor"/>). Never emit this into a
    /// generated page; use <see cref="TokenFor(string)"/> for that.</summary>
    internal static string HexFor(string language)
    {
        if (Colors.TryGetValue(language, out var c)) { return c; }
        var h = 0;
        foreach (var ch in language) { h = (h * 31 + ch) & 0x7fffffff; }
        return Fallback[h % Fallback.Length];
    }
}
