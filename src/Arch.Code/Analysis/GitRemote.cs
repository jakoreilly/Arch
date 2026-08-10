namespace Arch.Code.Analysis;

/// <summary>Derives a default source-link configuration from the scanned tree's git remote, so a
/// generated site links back to real source without anyone having to pass three flags. Explicit
/// <c>--source-link-*</c> options always win; this only fills the gap when none were given.
///
/// Offline and non-fatal, exactly like <see cref="GitHistory"/>: a tree with no git, no remote, or
/// a remote on a host this cannot classify yields <c>null</c>, and the viewer falls back to the
/// in-browser "Set source link…" prompt it has always had.
///
/// <para><b>Credentials are stripped, never emitted.</b> A remote URL is one of the few places a
/// token legitimately lives on disk (<c>https://oauth2:glpat-xxx@gitlab.com/org/repo.git</c>), and
/// this value is written into <c>model.json</c> and every page's <c>window.ARCH_SOURCELINK</c>.
/// <see cref="ParseRemote"/> drops the userinfo component unconditionally — see the tests.</para></summary>
public static class GitRemote
{
    /// <summary>A source-link configuration derived from the repository. Field names and meanings
    /// match the <c>--source-link-type/base/ref</c> options one-for-one.</summary>
    public sealed record Detected(string Type, string Base, string Ref, string Prefix);

    /// <summary>Reads <c>origin</c> and HEAD from the tree at <paramref name="sourcePath"/>.
    /// Returns null when there is no git, no <c>origin</c>, or the remote's host is not one this
    /// can turn into a correct blob URL — guessing a URL shape produces links that 404, which is
    /// worse than offering the prompt.</summary>
    public static Detected? Detect(string sourcePath)
    {
        var raw = GitHistory.RunGit(sourcePath, "remote get-url origin")?.Trim();
        if (string.IsNullOrEmpty(raw)) { return null; }

        var (type, webBase) = ParseRemote(raw);
        if (type.Length == 0 || webBase.Length == 0) { return null; }

        return new Detected(type, webBase, DetectRef(sourcePath), DetectPrefix(sourcePath));
    }

    /// <summary>Path from the repository root down to the scanned root, with a trailing slash, or
    /// "" when they are the same directory. Scanning a subfolder of a repo is ordinary (a monorepo
    /// service, or this repo's own fixtures), and without this every blob URL 404s because model
    /// paths are relative to the scan root while the URL is rooted at the repository.
    ///
    /// <para>Deliberately the same arithmetic as <see cref="GitHistory.Analyze"/>'s subPrefix —
    /// two different corrections for the same mismatch would be a bug waiting to happen.</para></summary>
    internal static string DetectPrefix(string sourcePath)
    {
        var repoRoot = GitHistory.RunGit(sourcePath, "rev-parse --show-toplevel")?.Trim();
        if (string.IsNullOrEmpty(repoRoot)) { return ""; }

        var scanFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath)).Replace('\\', '/');
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot)).Replace('\\', '/');
        return scanFull.Length > rootFull.Length
            && scanFull.StartsWith(rootFull + "/", StringComparison.OrdinalIgnoreCase)
                ? scanFull[(rootFull.Length + 1)..] + "/"
                : "";
    }

    /// <summary>The ref a blob URL should point at. A branch name is the readable, stable choice
    /// and is what a reviewer expects; a detached HEAD has no branch to name, so the commit itself
    /// is used (which is also the only honest answer there).</summary>
    private static string DetectRef(string sourcePath)
    {
        var branch = GitHistory.RunGit(sourcePath, "rev-parse --abbrev-ref HEAD")?.Trim();
        if (!string.IsNullOrEmpty(branch) && !string.Equals(branch, "HEAD", StringComparison.Ordinal))
        {
            return branch;
        }
        var sha = GitHistory.RunGit(sourcePath, "rev-parse HEAD")?.Trim();
        return string.IsNullOrEmpty(sha) ? "main" : sha;
    }

    /// <summary>Turns any spelling of a git remote into ("github"|"gitlab", web base URL), or
    /// ("", "") when the host is not recognised. Pure and deterministic — all the interesting
    /// cases are covered by unit tests rather than by running git.
    ///
    /// <para>Handles the four shapes a remote actually takes: scp-like (<c>git@host:org/repo.git</c>),
    /// <c>ssh://</c>, <c>git://</c> and <c>https://</c> — the last of which may carry userinfo that
    /// must not survive into the output.</para></summary>
    public static (string Type, string Base) ParseRemote(string raw)
    {
        if (!TryParseHostPath(raw, out var host, out var path)) { return ("", ""); }

        // Classified by hostname because the blob URL SHAPE differs between the two
        // (/blob/<ref>/ vs /-/blob/<ref>/) and nothing else in the remote reveals which.
        // A self-hosted instance under a company domain is deliberately NOT guessed at;
        // --source-link-type still covers it explicitly.
        var type = host.Contains("github", StringComparison.OrdinalIgnoreCase) ? "github"
                 : host.Contains("gitlab", StringComparison.OrdinalIgnoreCase) ? "gitlab"
                 : "";
        return type.Length == 0 ? ("", "") : (type, $"https://{host}/{path}");
    }

    /// <summary>Builds the web base for a remote whose host cannot be guessed from its name (a
    /// self-hosted GitLab/GitHub Enterprise instance) but whose <paramref name="type"/> the caller
    /// already knows — e.g. <c>arch group</c>, where a config entry is explicitly declared as a
    /// GitLab URL rather than sniffed from an origin remote. Same credential-stripping and shape
    /// handling as <see cref="ParseRemote"/>, just without the hostname gate.</summary>
    public static string WebBase(string raw) => TryParseHostPath(raw, out var host, out var path) ? $"https://{host}/{path}" : "";

    /// <summary>Handles the four shapes a remote actually takes: scp-like
    /// (<c>git@host:org/repo.git</c>), <c>ssh://</c>, <c>git://</c> and <c>https://</c> — the last
    /// of which may carry userinfo that must not survive into the output.</summary>
    private static bool TryParseHostPath(string raw, out string host, out string path)
    {
        host = ""; path = "";
        var url = (raw ?? "").Trim();
        if (url.Length == 0) { return false; }

        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var rest = url[(scheme + 3)..];
            var slash = rest.IndexOf('/');
            if (slash < 0) { return false; }
            host = rest[..slash];
            path = rest[(slash + 1)..];
        }
        else
        {
            // scp-like: [user@]host:path — the FIRST colon separates them. A Windows path
            // ("C:/src/app") also matches that shape, which is why the host is required to
            // look like a hostname below.
            var colon = url.IndexOf(':');
            if (colon <= 0) { return false; }
            host = url[..colon];
            path = url[(colon + 1)..];
        }

        // Userinfo: "oauth2:glpat-xxx@gitlab.com" or "git@github.com". Everything before the
        // last '@' is dropped — this is the credential-stripping step.
        var at = host.LastIndexOf('@');
        if (at >= 0) { host = host[(at + 1)..]; }

        // A port is legal in ssh:// remotes and never belongs in a web URL.
        var portSep = host.LastIndexOf(':');
        if (portSep > 0) { host = host[..portSep]; }

        if (host.Length == 0 || !host.Contains('.')) { return false; }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) { path = path[..^4]; }
        return path.Length > 0;
    }
}
