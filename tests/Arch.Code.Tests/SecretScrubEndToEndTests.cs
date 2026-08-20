using Arch.Code.Cli;
using Arch.Code.Site;

namespace Arch.Code.Tests;

/// <summary>End-to-end counterpart to SecretScrubTests: generates a real site from
/// Fixtures/SecretSample (a method at cognitive complexity >= 11 carrying a hardcoded password
/// and an API-key-shaped literal) and asserts neither reaches the published HTML — the
/// guarantee README.md and HOW-TO-USE.md both state ("passwords never appear in any generated
/// file"), which FilePage.AppendSnippet violated before SecretScrub existed.</summary>
public class SecretScrubEndToEndTests : IDisposable
{
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), "archdiagram-secret-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_outDir, recursive: true); } catch { }
    }

    [Fact]
    public void Generated_site_does_not_contain_the_plaintext_secret()
    {
        var model = Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.SecretSample, Open = false });
        SiteGenerator.Generate(model, _outDir, maxNodes: 60, generatedOn: "2026-01-01", showSnippets: true);

        var pages = Directory.GetFiles(Path.Combine(_outDir, "files"), "*.html");
        Assert.NotEmpty(pages);

        foreach (var page in pages)
        {
            var html = File.ReadAllText(page);
            Assert.DoesNotContain("Sup3rS3cret", html, StringComparison.Ordinal);
            Assert.DoesNotContain("AKIAIOSFODNN7EXAMPLE", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_snippet_still_renders_with_a_redacted_marker()
    {
        // Guards against the fix degenerating into "drop the snippet entirely" — the feature
        // must still show the method's shape, just not its secrets.
        var model = Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.SecretSample, Open = false });
        SiteGenerator.Generate(model, _outDir, maxNodes: 60, generatedOn: "2026-01-01", showSnippets: true);

        var legacyPage = Directory.GetFiles(Path.Combine(_outDir, "files"), "*Legacy*.html").Single();
        var html = File.ReadAllText(legacyPage);

        Assert.Contains("code-snippet", html, StringComparison.Ordinal);
        // Html.Encode runs over the whole snippet, so the marker appears HTML-entity-encoded
        // in the final page — "<redacted>" itself never appears literally.
        Assert.Contains("&lt;redacted&gt;", html, StringComparison.Ordinal);
    }
}
