using System.Text.RegularExpressions;

namespace Arch.Code.Analysis;

/// <summary>Blanks secret-shaped string literals in source text before it is copied into the
/// generated site. The inline-snippet feature (FilePage.AppendSnippet) is the one place raw
/// source reaches the output, so the "no secrets in the output" guarantee README.md and
/// HOW-TO-USE.md both make has to hold here — the connection-string scanner
/// (CsprojScanner/ConnectionStringNormalizer) only matches "k=v;k=v"-shaped literals and cannot
/// cover a bare <c>var pwd = "…"</c> or an API key.
///
/// <para>Deliberately not a secret <i>detector</i>: it does not decide whether a value is real,
/// it blanks the value of anything whose IDENTIFIER says secret. Over-redaction of a variable
/// innocently named <c>token</c> costs a reader nothing — the snippet is a complexity
/// illustration, not a source browser, and the source-link button goes to the real file.
/// Under-redaction publishes a credential.</para></summary>
public static class SecretScrub
{
    /// <summary>200 ms, matching SourceTextScanner's own budget — the input is at most
    /// FilePage.MaxSnippetLines lines, so this can only fire on pathological input.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    /// <summary>An identifier that names a secret (the <c>\w*</c> suffix lives INSIDE the
    /// capture, so <c>passwordHash</c> keeps its "Hash" instead of losing it to an unconsumed
    /// remainder), then assignment/colon punctuation and the opening quote, then the value,
    /// then the closing quote. Anchored on the identifier rather than on value shape (entropy,
    /// base64, key prefixes) because shape heuristics both miss real secrets and hit real code,
    /// and a wrong answer here is either a published credential or a silently mangled snippet.</summary>
    private static readonly Regex Assigned = new(
        """(?i)\b((?:password|passwd|pwd|secret|apikey|api_key|accesskey|accountkey|sas|token|credential)\w*)(\s*[:=]{1,2}\s*")([^"\r\n]+)(")""",
        RegexOptions.Compiled, Timeout);

    /// <summary>Same, for a connection-string-style pair inside a longer literal
    /// ("Server=x;Password=y;") — the identifier there is inside the string, not before it, and
    /// there is no closing quote to preserve since the value ends at the next <c>;</c> or EOL.
    /// The value group requires a non-whitespace, non-quote FIRST character
    /// (<c>(?!")\S[^";\r\n]*</c>): without the <c>(?!")</c>, this regex re-matches text
    /// <see cref="Assigned"/> already scrubbed on the same pass — its trailing <c>\s*</c>
    /// backtracks onto the single space before an opening quote and treats that quote as the
    /// start of "the value", mangling an already-correct <c>pwd = "&lt;redacted&gt;"</c> into
    /// <c>pwd = &lt;redacted&gt;"...</c>. Without the leading <c>\S</c> requirement at all, the
    /// same backtrack can match a bare space as a zero-content "value".</summary>
    private static readonly Regex InlinePair = new(
        """(?i)\b(password|pwd|user id|uid|accountkey)(\s*=\s*)((?!")\S[^";\r\n]*)""",
        RegexOptions.Compiled, Timeout);

    private const string Redacted = "<redacted>";

    /// <summary>Returns <paramref name="source"/> with every secret-shaped value replaced by
    /// <c>&lt;redacted&gt;</c>. Line count and line boundaries are preserved so the snippet's
    /// "lines N–M" header stays truthful. On a regex timeout the whole snippet is dropped
    /// (returns null) rather than emitted unscrubbed — failing closed is the only safe
    /// direction for a guarantee the product's own docs state as absolute.</summary>
    public static string? Text(string source)
    {
        try
        {
            var scrubbed = Assigned.Replace(source, $"$1$2{Redacted}$4");
            return InlinePair.Replace(scrubbed, $"$1$2{Redacted}");
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }
}
