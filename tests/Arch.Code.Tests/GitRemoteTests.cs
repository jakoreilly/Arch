using Arch.Code.Analysis;

namespace Arch.Code.Tests;

/// <summary>Covers <see cref="GitRemote.ParseRemote"/>, the pure half of source-link derivation.
/// Deliberately no test shells out to git: the value of these is that they pin the URL shapes and
/// the credential stripping without depending on what repository the suite happens to run in.</summary>
public class GitRemoteTests
{
    [Theory]
    // scp-like, the default for an SSH clone
    [InlineData("git@github.com:org/repo.git", "github", "https://github.com/org/repo")]
    [InlineData("git@gitlab.com:org/sub/repo.git", "gitlab", "https://gitlab.com/org/sub/repo")]
    // https, with and without the .git suffix
    [InlineData("https://github.com/org/repo.git", "github", "https://github.com/org/repo")]
    [InlineData("https://github.com/org/repo", "github", "https://github.com/org/repo")]
    // ssh:// with an explicit port — the port must not survive into a web URL
    [InlineData("ssh://git@gitlab.com:2222/org/repo.git", "gitlab", "https://gitlab.com/org/repo")]
    [InlineData("git://github.com/org/repo.git", "github", "https://github.com/org/repo")]
    // subgroups nest arbitrarily deep on GitLab
    [InlineData("https://gitlab.com/a/b/c/d.git", "gitlab", "https://gitlab.com/a/b/c/d")]
    // host matching is case-insensitive and tolerates subdomains
    [InlineData("https://www.github.com/org/repo", "github", "https://www.github.com/org/repo")]
    public void ParseRemote_recognised_hosts(string raw, string type, string expectedBase)
    {
        var (t, b) = GitRemote.ParseRemote(raw);
        Assert.Equal(type, t);
        Assert.Equal(expectedBase, b);
    }

    /// <summary>The one that matters for "secrets never reach the output": a remote may carry a
    /// token, and this value is written into model.json and into every generated page.</summary>
    [Theory]
    [InlineData("https://oauth2:glpat-SECRETVALUE@gitlab.com/org/repo.git")]
    [InlineData("https://someuser:ghp_SECRETVALUE@github.com/org/repo.git")]
    [InlineData("https://SECRETVALUE@github.com/org/repo.git")]
    public void ParseRemote_strips_credentials(string raw)
    {
        var (_, b) = GitRemote.ParseRemote(raw);
        Assert.DoesNotContain("SECRET", b, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", b, StringComparison.Ordinal);
    }

    [Theory]
    // A self-hosted instance is not guessed at: the blob URL shape is unknowable from the
    // hostname, and a guessed link 404s. --source-link-type covers this case explicitly.
    [InlineData("git@git.company.com:org/repo.git")]
    [InlineData("https://bitbucket.org/org/repo.git")]
    // Not remotes at all
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:/src/app")]
    [InlineData("/home/me/src/app")]
    // A host with no path is not a repository
    [InlineData("https://github.com/")]
    public void ParseRemote_unrecognised_yields_nothing(string raw)
    {
        var (t, b) = GitRemote.ParseRemote(raw);
        Assert.Equal("", t);
        Assert.Equal("", b);
    }

    [Fact]
    public void Detect_on_a_non_git_folder_is_null()
    {
        var dir = Path.Combine(Path.GetTempPath(), "arch-gitremote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(GitRemote.Detect(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
