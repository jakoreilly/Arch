using Arch.Code.Graph;

namespace Arch.Code.Tests;

public class SourceLinkTests
{
    [Theory]
    [InlineData("github", "https://github.com/o/r", "main", "src/A.cs", 12, "https://github.com/o/r/blob/main/src/A.cs#L12")]
    [InlineData("gitlab", "https://gl.com/o/r", "dev", "src/A.cs", 0, "https://gl.com/o/r/-/blob/dev/src/A.cs")]
    [InlineData("github", "https://github.com/o/r/", "main", "/src/A.cs", 0, "https://github.com/o/r/blob/main/src/A.cs")]
    public void UrlFor_web(string t, string b, string r, string p, int line, string expected)
        => Assert.Equal(expected, new SourceLink { Type = t, Base = b, Ref = r }.UrlFor(p, line));

    [Fact]
    public void UrlFor_local_prefixes_file_scheme()
        => Assert.Equal("file:///C:/src/app/src/A.cs",
            new SourceLink { Type = "local", Base = "C:/src/app" }.UrlFor("src\\A.cs", 9));

    /// <summary>Scanning a subfolder of a repository: the model's paths are relative to the SCAN
    /// root, the blob URL is rooted at the REPOSITORY, and Prefix is what bridges them. Without it
    /// every link on such a site 404s — which is invisible until someone clicks one.</summary>
    [Theory]
    [InlineData("github", "tests/Fixtures/SampleRepo/", "App/Program.cs", "https://github.com/o/r/blob/main/tests/Fixtures/SampleRepo/App/Program.cs#L3")]
    [InlineData("gitlab", "svc/api/", "src/A.cs", "https://github.com/o/r/-/blob/main/svc/api/src/A.cs#L3")]
    [InlineData("github", "", "App/Program.cs", "https://github.com/o/r/blob/main/App/Program.cs#L3")]
    public void UrlFor_web_applies_the_repo_root_prefix(string type, string prefix, string path, string expected)
        => Assert.Equal(expected,
            new SourceLink { Type = type, Base = "https://github.com/o/r", Ref = "main", Prefix = prefix }.UrlFor(path, 3));

    /// <summary>...and the local forms must NOT apply it: their base already IS the scan root, so
    /// adding the prefix would point one or more levels too deep.</summary>
    [Theory]
    [InlineData("vscode", "vscode://file/C:/scan/App/Program.cs:3")]
    [InlineData("local", "file:///C:/scan/App/Program.cs")]
    public void UrlFor_local_forms_ignore_the_prefix(string type, string expected)
        => Assert.Equal(expected,
            new SourceLink { Type = type, Base = "C:/scan", Prefix = "tests/Fixtures/SampleRepo/" }.UrlFor("App/Program.cs", 3));

    [Theory]
    [InlineData("C:/src/app", "src\\A.cs", 42, "vscode://file/C:/src/app/src/A.cs:42")]
    [InlineData("C:/src/app/", "src/A.cs", 0, "vscode://file/C:/src/app/src/A.cs")]
    [InlineData("C:\\src\\app", "src/A.cs", 7, "vscode://file/C:/src/app/src/A.cs:7")]
    public void UrlFor_vscode_deep_links_a_line(string b, string p, int line, string expected)
        => Assert.Equal(expected, new SourceLink { Type = "vscode", Base = b }.UrlFor(p, line));

    [Fact]
    public void UrlFor_empty_base_returns_empty()
        => Assert.Equal("", new SourceLink { Type = "github" }.UrlFor("A.cs", 1));

    [Fact]
    public void UrlFor_unknown_type_returns_empty()
        => Assert.Equal("", new SourceLink { Type = "none", Base = "x" }.UrlFor("A.cs", 1));
}
