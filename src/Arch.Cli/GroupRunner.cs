using System.Diagnostics;
using System.Text;

namespace Arch.Cli;

/// <summary>`arch group &lt;config.json&gt;` — scans every project in every declared group into
/// its own site, builds one landscape per group, and optionally one across all of them.
///
/// <para>Pure orchestration over verbs that already exist: each project goes through the same
/// <see cref="Runner.Run"/> the no-verb path uses (so a project containing both code and SQL gets
/// its combined site and hub, unchanged), and each landscape is the existing
/// <c>arch landscape --only</c>. No new analysis, and nothing here knows what a model contains.</para>
///
/// <para><b>On the read-only rule:</b> a project given as a <c>url</c> is cloned into
/// <c>&lt;out&gt;/_repos/</c> — a scratch directory this tool owns — and the clone is what gets
/// analysed. Nothing is ever written into a repository the user pointed at, which is the rule;
/// "never runs git write commands anywhere" was never the rule.</para></summary>
internal static class GroupRunner
{
    internal static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help") { PrintUsage(); return args.Length == 0 ? 2 : 0; }

        var configPath = Path.GetFullPath(args[0]);
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"arch: '{configPath}' is not a file.");
            return 2;
        }

        var cfg = GroupConfig.Load(configPath, out var error);
        if (cfg is null) { Console.Error.WriteLine($"arch: {error}"); return 2; }

        var open = true;
        string? outOverride = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--no-open") { open = false; }
            else if (args[i] == "--out" && i + 1 < args.Length) { outOverride = args[++i]; }
            else { Console.Error.WriteLine($"arch: unknown argument '{args[i]}'."); return 2; }
        }

        var configDir = Path.GetDirectoryName(configPath) ?? ".";
        var outDir = outOverride is not null
            ? Path.GetFullPath(outOverride, Directory.GetCurrentDirectory())
            : GroupConfig.Resolve(cfg.Out, configDir);
        Directory.CreateDirectory(outDir);

        Console.Error.WriteLine($"arch: group run — {cfg.Groups.Count} group(s) → {outDir}");

        var failures = new List<string>();
        var groupSiteIds = new List<(string Name, List<string> Ids)>();

        foreach (var group in cfg.Groups)
        {
            Console.Error.WriteLine($"arch: group '{group.Name}' — {group.Projects.Count} project(s)");
            var ids = new List<string>();
            foreach (var project in group.Projects)
            {
                var id = GroupConfig.SiteId(project);
                int code;
                if (project.ConnFile.Length > 0 || project.Env)
                {
                    code = RunConnectProject(project, configDir, Path.Combine(outDir, id));
                }
                else
                {
                    var source = ResolveSource(project, configDir, outDir, failures);
                    if (source is null) { continue; }

                    // Every project's site is generated with --no-open regardless of the run's own
                    // --no-open: a ten-project group must not open ten browser tabs. Only the final
                    // landscape is offered to the browser.
                    var runArgs = new List<string> { source, "--out", Path.Combine(outDir, id), "--no-open" };
                    AddSourceLinkArgs(runArgs, project);
                    code = Runner.Run([.. runArgs]);
                }
                if (code is 0 or 3) { ids.Add(id); }   // 3 = a --fail-on gate tripped; the site still exists
                else { failures.Add($"{group.Name}/{id}: arch exited {code}"); }
            }
            groupSiteIds.Add((group.Name, ids));

            if (ids.Count == 0)
            {
                Console.Error.WriteLine($"arch: group '{group.Name}' produced no sites — skipping its landscape.");
                continue;
            }
            RunLandscape(outDir, Path.Combine(outDir, "site-landscape-" + SlugForFolder(group.Name)), ids, group.Name);
        }

        string? overallIndex = null;
        var allIds = groupSiteIds.SelectMany(g => g.Ids).ToList();
        if (cfg.OverallLandscape && allIds.Count > 0)
        {
            var overallOut = Path.Combine(outDir, "site-landscape");
            RunLandscape(outDir, overallOut, allIds, "All groups");
            overallIndex = Path.Combine(overallOut, "index.html");
        }

        foreach (var f in failures) { Console.Error.WriteLine($"arch: FAILED — {f}"); }
        Console.Error.WriteLine($"arch: group run done — {allIds.Count} site(s), {cfg.Groups.Count} group landscape(s)"
            + (overallIndex is not null ? ", 1 overall landscape" : ""));

        if (open && overallIndex is not null && File.Exists(overallIndex))
        {
            try { Process.Start(new ProcessStartInfo(overallIndex) { UseShellExecute = true }); }
            catch (Exception ex) { Console.Error.WriteLine($"arch: could not auto-open the landscape: {ex.Message}"); }
        }

        // Partial success is still success: one unreachable repo should not throw away the other
        // nine sites. A failure is reported and exits 3 (same "output written, something is off"
        // meaning --fail-on already uses), never 1.
        return failures.Count > 0 ? 3 : 0;
    }

    /// <summary>The folder to analyse for a project: its local path, or a clone of its URL kept
    /// under <c>&lt;out&gt;/_repos/</c>. Returns null (having recorded a failure) when a clone or
    /// update fails, so one bad repo does not abort the whole run.</summary>
    private static string? ResolveSource(GroupConfig.Project project, string configDir, string outDir, List<string> failures)
    {
        if (!string.IsNullOrWhiteSpace(project.Path)) { return GroupConfig.Resolve(project.Path, configDir); }

        var reposDir = Path.Combine(outDir, "_repos");
        Directory.CreateDirectory(reposDir);
        var leaf = GroupConfig.SiteId(project)["site-".Length..];
        var dest = Path.Combine(reposDir, leaf);

        var ok = Directory.Exists(Path.Combine(dest, ".git"))
            ? Update(dest, project.Ref)
            : Clone(project.Url, project.Ref, dest);

        if (!ok) { failures.Add($"{leaf}: git clone/update failed (see the git output above)"); return null; }
        return dest;
    }

    /// <summary>Runs a database (connFile/env) project through the same `connect` verb
    /// `arch connect` itself dispatches to (<see cref="Entry.Run"/>), so a group's database
    /// site is byte-for-byte what running `arch connect` standalone against the same
    /// connection would produce. `Verbs.RunConnect` is internal to Arch.Sql but visible here
    /// via that assembly's InternalsVisibleTo("arch") (Arch.Sql.csproj) — the same visibility
    /// Entry.cs already relies on for the top-level `connect` verb.</summary>
    private static int RunConnectProject(GroupConfig.Project project, string configDir, string projectOutDir)
    {
        var args = new List<string> { "connect" };
        if (project.ConnFile.Length > 0)
        {
            args.Add("--conn-file");
            args.Add(GroupConfig.Resolve(project.ConnFile, configDir));
        }
        else
        {
            args.Add("--env");
        }
        args.Add("--out");
        args.Add(projectOutDir);
        args.Add("--no-open");
        return Arch.Sql.Cli.Verbs.RunConnect([.. args]);
    }

    /// <summary>A cloned repo's <c>origin</c> already feeds Arch.Code's own auto-detection
    /// (<see cref="Arch.Code.Analysis.GitRemote.ParseRemote"/>), which recognises github.com and
    /// gitlab.com by name. A self-hosted GitLab on a company domain does not name itself, so
    /// there is nothing to sniff — <c>sourceLinkType</c> tells us explicitly, and the web base is
    /// derived from the same URL this project was cloned from, not re-read from the clone.</summary>
    private static void AddSourceLinkArgs(List<string> args, GroupConfig.Project project)
    {
        if (string.IsNullOrWhiteSpace(project.SourceLinkType) || string.IsNullOrWhiteSpace(project.Url)) { return; }
        var webBase = Arch.Code.Analysis.GitRemote.WebBase(project.Url);
        if (webBase.Length == 0) { return; }
        args.Add("--source-link-type"); args.Add(project.SourceLinkType);
        args.Add("--source-link-base"); args.Add(webBase);
        if (project.Ref.Length > 0) { args.Add("--source-link-ref"); args.Add(project.Ref); }
    }

    private static bool Clone(string url, string gitRef, string dest)
    {
        Console.Error.WriteLine($"arch: cloning {Redact(url)} → {dest}");
        var args = gitRef.Length > 0 ? $"clone --branch {Quote(gitRef)} {Quote(url)} {Quote(dest)}" : $"clone {Quote(url)} {Quote(dest)}";
        return Git(Path.GetDirectoryName(dest) ?? ".", args);
    }

    private static bool Update(string dest, string gitRef)
    {
        Console.Error.WriteLine($"arch: updating existing clone {dest}");
        if (!Git(dest, "fetch --prune")) { return false; }
        if (gitRef.Length > 0 && !Git(dest, $"checkout {Quote(gitRef)}")) { return false; }
        return Git(dest, "pull --ff-only");
    }

    /// <summary>Runs git, streaming nothing but surfacing failure. Deliberately not
    /// <c>GitHistory.RunGit</c>: that one is tuned for a bounded read (60s, swallows everything),
    /// and a clone of a large repository legitimately takes longer and needs its stderr shown.</summary>
    private static bool Git(string workingDir, string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using var proc = Process.Start(psi);
            if (proc is null) { return false; }
            proc.StandardOutput.ReadToEnd();
            var err = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                Console.Error.WriteLine($"arch: git {args.Split(' ')[0]} failed — {Redact(err).Trim()}");
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Console.Error.WriteLine($"arch: git could not be started ({ex.Message}). Is it on PATH?");
            return false;
        }
    }

    private static void RunLandscape(string parent, string outDir, IReadOnlyList<string> ids, string title)
    {
        // Same entry point `arch landscape` uses, with --only scoping it to this group's sites.
        // --no-open always: the group run decides what, if anything, to open at the end.
        Arch.Code.Cli.Verbs.RunLandscape(
            ["--landscape", parent, "--out", outDir, "--only", string.Join(",", ids), "--title", title, "--no-open"]);
    }

    /// <summary>A clone URL can carry a token, and this one is printed to the console and may end
    /// up in a CI log. Same rule as GitRemote: userinfo never gets echoed.</summary>
    internal static string Redact(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"://[^/\s@]+@", "://<redacted>@");

    private static string Quote(string s) => s.Contains(' ') ? $"\"{s}\"" : s;

    private static string SlugForFolder(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name) { sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'); }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "group" : slug;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: arch group <config.json> [--out <dir>] [--no-open]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  Scans every project in every group into its own site, then builds one landscape");
        Console.Error.WriteLine("  per group and (unless disabled) one across all of them.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  {");
        Console.Error.WriteLine("    \"out\": \"sites\",");
        Console.Error.WriteLine("    \"overallLandscape\": true,");
        Console.Error.WriteLine("    \"groups\": [");
        Console.Error.WriteLine("      { \"name\": \"Backend\", \"projects\": [");
        Console.Error.WriteLine("          { \"path\": \"C:/src/api\" },");
        Console.Error.WriteLine("          { \"url\": \"git@gitlab.company.local:org/worker.git\", \"ref\": \"main\", \"sourceLinkType\": \"gitlab\" } ] }");
        Console.Error.WriteLine("      { \"name\": \"Warehouse\", \"projects\": [");
        Console.Error.WriteLine("          { \"connFile\": \"warehouse-conn.json\", \"name\": \"Warehouse DB\" } ] }");
        Console.Error.WriteLine("    ]");
        Console.Error.WriteLine("  }");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  Paths in the config resolve against the config file's folder. A \"url\" project is");
        Console.Error.WriteLine("  cloned into <out>/_repos/ and the clone is analysed — nothing is ever written to a");
        Console.Error.WriteLine("  repository you pointed at. Exit 3 means some projects failed and the rest succeeded.");
        Console.Error.WriteLine("  A \"connFile\" project connects to a live database instead of scanning a folder — same");
        Console.Error.WriteLine("  as \"arch connect --conn-file\"; set \"env\": true instead to read ARCHSQL_CONNECTION.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  Source links (jump-to-repo from a file page) are auto-detected from a github.com/gitlab.com");
        Console.Error.WriteLine("  clone URL. A self-hosted GitLab/GitHub on a company domain needs \"sourceLinkType\": \"gitlab\"");
        Console.Error.WriteLine("  (or \"github\") on that project — the web base is then derived from its own \"url\".");
    }
}
