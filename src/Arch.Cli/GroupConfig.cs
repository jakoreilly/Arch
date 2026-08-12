using System.Text.Json;
using System.Text.Json.Serialization;
using Arch.Core.Serialization;

namespace Arch.Cli;

/// <summary>The `arch group` config file: named sets of projects, each scanned into its own
/// site, then federated into one landscape per group (and optionally one across all of them).
/// Each project is a local Path already on disk, a Url to clone, or a live database via
/// connFile/env.
///
/// <para>This is the shape ArchDiagram's <c>Launch-ArchDiagram.ps1</c> carried in PowerShell and
/// that never came across in the migration — see continue.md. It is deliberately declarative and
/// re-runnable: pointing `arch group` at the same file again refreshes every site in place.</para></summary>
public sealed record GroupConfig
{
    /// <summary>Where every generated site lands. Relative paths resolve against the CONFIG
    /// FILE's folder, not the current directory — a config is a checked-in artifact that should
    /// mean the same thing from wherever it is invoked.</summary>
    public string Out { get; init; } = "sites";

    /// <summary>Also build one landscape spanning every project in every group.</summary>
    public bool OverallLandscape { get; init; } = true;

    public List<Group> Groups { get; init; } = [];

    public sealed record Group
    {
        public string Name { get; init; } = "";
        public List<Project> Projects { get; init; } = [];
    }

    /// <summary>One project: a local <see cref="Path"/> already on disk, a <see cref="Url"/>
    /// to clone, or a live database via <see cref="ConnFile"/>/<see cref="Env"/>. Exactly one
    /// of the four.</summary>
    public sealed record Project
    {
        public string Path { get; init; } = "";
        public string Url { get; init; } = "";
        /// <summary>Branch/tag to check out after cloning. Ignored for a local path.</summary>
        public string Ref { get; init; } = "";
        /// <summary>Site folder name; defaults to the repo/folder leaf, slugified. Required
        /// (not defaulted) for a database project — see <see cref="ConnFile"/>.</summary>
        public string Name { get; init; } = "";
        /// <summary>"github" | "gitlab", same meaning as <c>--source-link-type</c>. A cloned
        /// project's own <c>origin</c> remote is auto-detected already (see
        /// <see cref="Arch.Code.Analysis.GitRemote.ParseRemote"/>), but that only recognises a
        /// host whose name contains "github"/"gitlab" — a self-hosted instance on a company
        /// domain (e.g. <c>dev.internal</c>) needs this told explicitly, exactly as the
        /// standalone CLI does. Empty leaves auto-detection in charge.</summary>
        public string SourceLinkType { get; init; } = "";
        /// <summary>A live database instead of a folder: the connection string is read from
        /// this file at run time, resolved against the config file's own folder — never
        /// inlined in the config itself, the same secrecy rule
        /// <c>Arch.Sql.Cli.ConnectOptions</c> enforces for the standalone `connect` verb.
        /// <see cref="GroupConfig.Load"/> only checks that this file exists; it never reads
        /// it, so a connection string is never held in memory until the actual run needs it.</summary>
        public string ConnFile { get; init; } = "";
        /// <summary>Read the connection string from the ARCHSQL_CONNECTION environment
        /// variable instead of a file — same as <c>arch connect --env</c>. The env var is a
        /// single process-wide value, so setting this on more than one project in a config
        /// connects them all to the same database; that is a config mistake for the author to
        /// avoid, not something this tool detects.</summary>
        public bool Env { get; init; }
    }

    /// <summary>Reads and validates a config. Returns null with <paramref name="error"/> set on
    /// anything that would produce a confusing failure three minutes into a long run — an empty
    /// group, a project with neither path nor url, a local path that does not exist. Validating
    /// up front matters more here than elsewhere: a group run can take minutes per project, and
    /// discovering a typo at the end wastes all of it.</summary>
    public static GroupConfig? Load(string path, out string? error)
    {
        error = null;
        GroupConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<GroupConfig>(File.ReadAllText(path), ModelJson.Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            error = $"could not read '{path}': {ex.Message}";
            return null;
        }
        if (cfg is null) { error = $"'{path}' did not contain a group config."; return null; }
        if (cfg.Groups.Count == 0) { error = $"'{path}' declares no groups."; return null; }

        var configDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".";
        foreach (var g in cfg.Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Name)) { error = "every group needs a non-empty name."; return null; }
            if (g.Projects.Count == 0) { error = $"group '{g.Name}' has no projects."; return null; }
            foreach (var p in g.Projects)
            {
                var hasPath = !string.IsNullOrWhiteSpace(p.Path);
                var hasUrl = !string.IsNullOrWhiteSpace(p.Url);
                var hasConnFile = !string.IsNullOrWhiteSpace(p.ConnFile);
                var hasEnv = p.Env;
                var sourceCount = (hasPath ? 1 : 0) + (hasUrl ? 1 : 0) + (hasConnFile ? 1 : 0) + (hasEnv ? 1 : 0);
                if (sourceCount != 1)
                {
                    error = $"group '{g.Name}': each project needs exactly one of \"path\", \"url\", \"connFile\", or \"env\": true.";
                    return null;
                }
                if ((hasConnFile || hasEnv) && string.IsNullOrWhiteSpace(p.Name))
                {
                    error = $"group '{g.Name}': a database project (connFile/env) needs an explicit \"name\" — there is no path or url to derive one from.";
                    return null;
                }
                if (hasPath && !Directory.Exists(Resolve(p.Path, configDir)))
                {
                    error = $"group '{g.Name}': project path '{p.Path}' is not a directory.";
                    return null;
                }
                if (hasConnFile && !File.Exists(Resolve(p.ConnFile, configDir)))
                {
                    error = $"group '{g.Name}': project connFile '{p.ConnFile}' is not a file.";
                    return null;
                }
            }
        }
        return cfg;
    }

    /// <summary>Resolves a config-relative path against the config file's own folder.</summary>
    public static string Resolve(string maybeRelative, string configDir) =>
        System.IO.Path.GetFullPath(maybeRelative, configDir);

    /// <summary>The site folder name for a project: its explicit Name, else the leaf of its path
    /// or URL. Slugified so it is safe as both a directory name and a `--only` token.</summary>
    public static string SiteId(Project p)
    {
        if (!string.IsNullOrWhiteSpace(p.Name)) { return "site-" + Slugify(p.Name); }
        var raw = !string.IsNullOrWhiteSpace(p.Path) ? p.Path : p.Url;
        var leaf = raw.TrimEnd('/', '\\').Split('/', '\\')[^1];
        if (leaf.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) { leaf = leaf[..^4]; }
        return "site-" + Slugify(leaf);
    }

    /// <summary>Same reduction CliOptions uses for its default --out name, kept here rather than
    /// shared because these two slugs must be allowed to diverge: this one also has to survive
    /// being a comma-separated `--only` token.</summary>
    private static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return "project"; }
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-');
        }
        var slug = sb.ToString().Trim('-', '.');
        return slug.Length == 0 ? "project" : slug;
    }

    /// <summary>Used only by the JSON contract; kept so the serializer never trips on an unknown
    /// member in a hand-written config the user is expected to edit by hand.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }
}
