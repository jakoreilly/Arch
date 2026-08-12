using Arch.Cli;

namespace Arch.Cli.Tests;

/// <summary>Covers the validation and naming half of `arch group`. The orchestration itself is
/// exercised end-to-end elsewhere; what matters here is that a bad config is rejected BEFORE a run
/// that can take minutes per project, with a message naming the group at fault.</summary>
public class GroupConfigTests : IDisposable
{
    private readonly string _dir;

    public GroupConfigTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "arch-group-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(Path.Combine(_dir, "proj"));
        File.WriteAllText(Path.Combine(_dir, "db.json"), "Server=.;Database=Test;Trusted_Connection=True;");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "group.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_accepts_a_valid_config_and_resolves_paths_against_the_config_folder()
    {
        var cfg = GroupConfig.Load(Write("""
            { "out": "sites", "groups": [ { "name": "A", "projects": [ { "path": "proj" } ] } ] }
            """), out var error);

        Assert.Null(error);
        Assert.NotNull(cfg);
        Assert.Equal("sites", cfg.Out);
        Assert.True(cfg.OverallLandscape);   // defaults on
        Assert.Equal(Path.Combine(_dir, "proj"), GroupConfig.Resolve("proj", _dir));
    }

    [Fact]
    public void Load_accepts_a_database_project_naming_itself_via_connFile()
    {
        var cfg = GroupConfig.Load(Write("""
            { "groups": [ { "name": "A", "projects": [ { "connFile": "db.json", "name": "Warehouse DB" } ] } ] }
            """), out var error);

        Assert.Null(error);
        Assert.NotNull(cfg);
        Assert.Equal("site-Warehouse-DB", GroupConfig.SiteId(cfg.Groups[0].Projects[0]));
    }

    [Theory]
    // No groups at all
    [InlineData("""{ "groups": [] }""", "declares no groups")]
    // A group with no name, and one with no projects
    [InlineData("""{ "groups": [ { "name": "", "projects": [ { "path": "proj" } ] } ] }""", "non-empty name")]
    [InlineData("""{ "groups": [ { "name": "A", "projects": [] } ] }""", "no projects")]
    // Neither path nor url, and both at once — the message must name the group
    [InlineData("""{ "groups": [ { "name": "A", "projects": [ { } ] } ] }""", "exactly one")]
    [InlineData("""{ "groups": [ { "name": "A", "projects": [ { "path": "proj", "url": "https://x/y.git" } ] } ] }""", "exactly one")]
    // A path that is not there: caught up front, not three minutes in
    [InlineData("""{ "groups": [ { "name": "A", "projects": [ { "path": "nope" } ] } ] }""", "not a directory")]
    // A database project with no name — nothing to derive one from
    [InlineData("""{ "groups": [ { "name": "A", "projects": [ { "connFile": "db.json" } ] } ] }""", "explicit")]
    // env alone also needs a name
    [InlineData("""{ "groups": [ { "name": "A", "projects": [ { "env": true } ] } ] }""", "explicit")]
    // connFile and env both set on the same project — two sources at once
    [InlineData("""{ "groups": [ { "name": "A", "projects": [ { "connFile": "db.json", "env": true, "name": "X" } ] } ] }""", "exactly one")]
    // connFile pointing at a file that is not there
    [InlineData("""{ "groups": [ { "name": "A", "projects": [ { "connFile": "nope.json", "name": "X" } ] } ] }""", "not a file")]
    public void Load_rejects_a_bad_config_with_a_useful_message(string json, string expectedFragment)
    {
        var cfg = GroupConfig.Load(Write(json), out var error);
        Assert.Null(cfg);
        Assert.NotNull(error);
        Assert.Contains(expectedFragment, error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // Leaf of a local path
    [InlineData("C:/src/my-api", "", "", "site-my-api")]
    // Leaf of a URL, with the .git suffix dropped
    [InlineData("", "https://gitlab.com/org/sub/worker.git", "", "site-worker")]
    [InlineData("", "git@github.com:org/repo.git", "", "site-repo")]
    // An explicit name wins, and is slugified so it survives being an --only token
    [InlineData("C:/src/my-api", "", "Billing Service", "site-Billing-Service")]
    // A database project has no path/url leaf to fall back to — name is everything
    [InlineData("", "", "Warehouse DB", "site-Warehouse-DB")]
    public void SiteId_names_the_site_folder(string path, string url, string name, string expected)
        => Assert.Equal(expected, GroupConfig.SiteId(new GroupConfig.Project { Path = path, Url = url, Name = name }));

    /// <summary>A clone URL can carry a token and the group runner echoes the URL to the console,
    /// which routinely ends up in a CI log.</summary>
    [Theory]
    [InlineData("https://oauth2:glpat-SECRET@gitlab.com/org/repo.git")]
    [InlineData("cloning https://user:ghp_SECRET@github.com/o/r.git -> dest")]
    public void Redact_removes_userinfo_from_anything_echoed(string text)
    {
        var redacted = GroupRunner.Redact(text);
        Assert.DoesNotContain("SECRET", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<redacted>", redacted, StringComparison.Ordinal);
    }
}
