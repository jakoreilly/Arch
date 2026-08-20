using Arch.Code.Analysis;

namespace Arch.Code.Tests;

/// <summary>Phase 2 of plan.md: FilePage.AppendSnippet is the one place raw source reaches the
/// generated site, and it published a plaintext hardcoded password and an AWS-shaped key
/// verbatim while --fail-on secrets reported "all gate(s) passed" — the connection-string
/// scanner only matches "k=v;k=v" shapes, not a bare `var pwd = "…"`. These tests exercise
/// SecretScrub directly; SiteSmokeTests / FilePage tests cover the end-to-end wiring.</summary>
public class SecretScrubTests
{
    [Theory]
    [InlineData("""var pwd = "hunter2";""")]
    [InlineData("""passwordHash = "abc";""")]
    [InlineData("""apiKey: "AKIAIOSFODNN7EXAMPLE";""")]
    [InlineData("""var token = "x";""")]
    public void Secret_shaped_assignment_is_redacted(string line)
    {
        var scrubbed = SecretScrub.Text(line);

        Assert.NotNull(scrubbed);
        Assert.Contains("<redacted>", scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void Suffix_after_the_keyword_is_preserved_not_swallowed()
    {
        // The \w* suffix must live INSIDE the capture group, or "Hash" is consumed by the
        // alternation but never re-emitted, silently dropping it from the output.
        var scrubbed = SecretScrub.Text("""passwordHash = "abc";""");

        Assert.NotNull(scrubbed);
        Assert.Contains("passwordHash", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void Closing_quote_survives_the_substitution()
    {
        // Group numbering ($1$2<redacted>$4) must keep the closing quote, or the snippet
        // emits an unterminated string literal that reads like a truncation bug.
        var scrubbed = SecretScrub.Text("""var pwd = "hunter2";""");

        Assert.NotNull(scrubbed);
        Assert.Equal("""var pwd = "<redacted>";""", scrubbed);
    }

    [Fact]
    public void Inline_connection_string_pair_is_redacted()
    {
        var scrubbed = SecretScrub.Text("""var cs = "Server=x;Password=p;Database=d";""");

        Assert.NotNull(scrubbed);
        Assert.DoesNotContain("Password=p", scrubbed, StringComparison.Ordinal);
        Assert.Contains("Password=<redacted>", scrubbed, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declaration_with_no_quoted_value_is_left_alone()
    {
        var line = "var passwordPolicy = 3;";
        Assert.Equal(line, SecretScrub.Text(line));
    }

    [Fact]
    public void A_comment_mentioning_secrets_is_left_alone()
    {
        var line = "// password is in Key Vault, not here";
        Assert.Equal(line, SecretScrub.Text(line));
    }

    [Fact]
    public void Line_count_is_preserved()
    {
        var source = "line one\nvar pwd = \"hunter2\";\nline three";
        var scrubbed = SecretScrub.Text(source);

        Assert.NotNull(scrubbed);
        Assert.Equal(3, scrubbed.Split('\n').Length);
    }
}
