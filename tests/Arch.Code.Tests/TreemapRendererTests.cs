using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Arch.Code.Graph;
using Arch.Code.Site;

namespace Arch.Code.Tests;

public class TreemapRendererTests
{
    // TreemapRenderer.Clip is private static — invoked via reflection because its
    // empty-return branch (label too narrow to be worth an ellipsis) is unreachable
    // through the public Render() API: the caller's own MinLabelW=46 gate never lets a
    // rect through narrow enough to produce maxChars < MinReadableChars.
    private static string Clip(string name, double width)
    {
        var method = typeof(TreemapRenderer).GetMethod("Clip", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (string)method.Invoke(null, [name, width])!;
    }

    [Fact]
    public void Clip_omits_a_label_too_narrow_to_read_even_with_an_ellipsis()
    {
        // maxChars = (int)((24 - 6) / 5.2) = 3, below MinReadableChars (4).
        Assert.Equal("", Clip("VeryLongFileName.cs", 24));
    }

    [Fact]
    public void Clip_truncates_with_an_ellipsis_instead_of_a_raw_character_cut()
    {
        var name = "PackagesPageTests.cs";
        var clipped = Clip(name, 60); // maxChars = (int)((60-6)/5.2) = 10
        Assert.Equal("PackagesP…", clipped); // not "PackagesPa" — the old bug: a raw mid-word cut
        Assert.EndsWith("…", clipped);
        Assert.True(clipped.Length < name.Length);
    }

    [Fact]
    public void Clip_returns_the_full_name_when_it_fits()
    {
        Assert.Equal("short.cs", Clip("short.cs", 200));
    }

    private static List<FileNode> Files() =>
    [
        new() { RelPath = "src/Big.cs", Slug = "src_big_cs", Language = "C#", Loc = 400 },
        new() { RelPath = "src/Small.cs", Slug = "src_small_cs", Language = "C#", Loc = 20 },
        new() { RelPath = "web/app.ts", Slug = "web_app_ts", Language = "TypeScript/JavaScript", Loc = 120 },
        new() { RelPath = "docs/readme.md", Slug = "docs_readme_md", Language = "Markdown", Loc = 0 }, // no LOC → excluded
    ];

    [Fact]
    public void Emits_one_rect_per_file_with_loc()
    {
        var svg = TreemapRenderer.Render(Files());
        Assert.Equal(3, Regex.Matches(svg, "<rect ").Count); // the 0-LOC file is excluded
    }

    [Fact]
    public void Is_valid_xml()
    {
        var svg = TreemapRenderer.Render(Files());
        var doc = XDocument.Parse(svg); // throws if malformed / unbalanced / unescaped
        Assert.Equal("svg", doc.Root!.Name.LocalName);
    }

    [Fact]
    public void Has_no_http_urls()
    {
        Assert.DoesNotContain("http", TreemapRenderer.Render(Files()));
    }

    [Fact]
    public void All_hrefs_point_at_file_pages()
    {
        var svg = TreemapRenderer.Render(Files());
        foreach (Match m in Regex.Matches(svg, "href=\"([^\"]+)\""))
        {
            Assert.Matches(new Regex(@"^files/[^""]+\.html$"), m.Groups[1].Value);
        }
    }

    [Fact]
    public void Is_deterministic()
    {
        Assert.Equal(TreemapRenderer.Render(Files()), TreemapRenderer.Render(Files()));
    }

    [Fact]
    public void Excludes_test_files()
    {
        var files = new List<FileNode>
        {
            new() { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 100 },
            new() { RelPath = "tests/ATests.cs", Slug = "at", Language = "C#", Loc = 100, IsTest = true },
        };
        var svg = TreemapRenderer.Render(files);
        Assert.Contains("files/a.html", svg);
        Assert.DoesNotContain("files/at.html", svg);
    }

    [Fact]
    public void Excludes_vendored_files()
    {
        var files = new List<FileNode>
        {
            new() { RelPath = "src/A.cs", Slug = "a", Language = "C#", Loc = 100 },
            new() { RelPath = "assets/lib/mermaid.min.js", Slug = "mm", Language = "TypeScript/JavaScript", Loc = 83419, IsVendored = true },
        };
        var svg = TreemapRenderer.Render(files);
        Assert.Contains("files/a.html", svg);
        Assert.DoesNotContain("files/mm.html", svg);
    }

    [Fact]
    public void Empty_when_no_loc()
    {
        Assert.Equal("", TreemapRenderer.Render([new FileNode { RelPath = "x.md", Slug = "x", Language = "Markdown", Loc = 0 }]));
    }
}
