using System.Diagnostics;
using Arch.Code.Cli;
using Arch.Core.Detection;
using Arch.Sql.Cli;

namespace Arch.Cli;

/// <summary>The default (no-verb) path: detect what's at a folder and generate the right
/// site. Single-provider mode is a pass-through — outDir is exactly what the user asked
/// for, byte-identical to running that product's own exe directly. Combined mode nests
/// each provider's complete, unmodified site under outDir/{id}/ and writes a small hub
/// page at outDir/index.html linking to whichever succeeded (confirmed with the user:
/// nesting over a merged sidebar — see plan.md's Phase 5 findings).</summary>
public static class Runner
{
    private static readonly IAnalysisProvider[] Providers = [new CodeAnalysisProvider(), new SqlAnalysisProvider()];

    public static int Run(string[] args)
    {
        if (args.Length == 0) { PrintUsage(); return 2; }
        if (args[0] is "-h" or "--help") { PrintUsage(); return 0; }

        var sourceArg = args[0];
        if (sourceArg.StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"arch: unrecognized argument '{sourceArg}'.");
            PrintUsage();
            return 2;
        }
        var sourceFull = Path.GetFullPath(sourceArg);
        if (!Directory.Exists(sourceFull))
        {
            Console.Error.WriteLine($"arch: '{sourceFull}' is not a directory.");
            return 2;
        }

        Console.Error.WriteLine($"Arch — scanning {sourceFull}");

        var detections = Providers.Select(p => (Provider: p, Detection: p.Detect(sourceFull))).ToList();
        var applying = detections.Where(d => d.Detection.Applies).ToList();

        if (applying.Count == 0)
        {
            PrintEmptyState();
            return 2;
        }

        foreach (var (provider, detection) in detections)
        {
            Console.Error.WriteLine(detection.Applies
                ? $"  {provider.Id,-4} {detection.Summary}"
                : $"  {provider.Id,-4} skipped — no {provider.Describes} found");
        }

        var outDir = ResolveOutDir(args, sourceFull);
        var noOpen = args.Contains("--no-open");
        var combined = applying.Count > 1;

        var sw = Stopwatch.StartNew();
        var links = new List<HubPage.Link>();
        var anyFailed = false;
        foreach (var (provider, detection) in applying)
        {
            var providerOutDir = combined ? Path.Combine(outDir, provider.Id) : outDir;
            var providerArgs = BuildProviderArgs(args, providerOutDir);
            try
            {
                provider.Generate(sourceFull, providerOutDir, providerArgs);
                var (title, icon) = DisplayInfo(provider.Id);
                links.Add(new HubPage.Link(provider.Id, title, icon, detection.Summary));
            }
            catch (Exception ex)
            {
                anyFailed = true;
                Console.Error.WriteLine($"  {provider.Id,-4} FAILED — {ex.Message}");
            }
        }
        sw.Stop();

        if (links.Count == 0)
        {
            Console.Error.WriteLine("Arch: every applicable provider failed; nothing was generated.");
            return 1;
        }

        string indexPath;
        if (!combined)
        {
            indexPath = Path.Combine(outDir, "index.html");
        }
        else
        {
            var siteName = Path.GetFileName(sourceFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            HubPage.Write(outDir, siteName, links);
            indexPath = Path.Combine(outDir, "index.html");
        }

        var outName = Path.GetFileName(outDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var status = anyFailed
            ? $"done, with {applying.Count - links.Count} provider(s) failed"
            : $"done ({sw.Elapsed.TotalSeconds:F1}s)";
        Console.Error.WriteLine($"Generating {outName} … {status}");
        Console.Error.WriteLine($"  → {indexPath}");

        if (!noOpen)
        {
            try { Process.Start(new ProcessStartInfo(indexPath) { UseShellExecute = true }); }
            catch (Exception ex) { Console.Error.WriteLine($"arch: could not auto-open the site: {ex.Message}"); }
        }

        return anyFailed ? 1 : 0;
    }

    private static (string Title, string Icon) DisplayInfo(string id) => id switch
    {
        "code" => ("Code Analysis", "◈"),
        "sql" => ("SQL Analysis", "❖"),
        _ => (id, "•"),
    };

    /// <summary>--out from argv if given, else "site-{slugified folder name}" — the same
    /// default convention both products' own CliOptions.Parse already use, duplicated
    /// here (their Slugify is private) since Runner needs this default before it knows
    /// which provider(s) will run, to build each provider's own sub-outDir.</summary>
    private static string ResolveOutDir(string[] args, string sourceFull)
    {
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--out") { return Path.GetFullPath(args[i + 1], Environment.CurrentDirectory); }
        }
        var folderName = Path.GetFileName(sourceFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.GetFullPath("site-" + Slugify(folderName), Environment.CurrentDirectory);
    }

    private static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { return "project"; }
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name) { sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-'); }
        var slug = sb.ToString().Trim('-', '.');
        return slug.Length == 0 ? "project" : slug;
    }

    /// <summary>The original argv, with --out replaced by this provider's actual output
    /// folder and --no-open forced on — Runner opens exactly one browser tab at the end,
    /// not each provider independently. Every other flag (--exclude, --sarif, --max-nodes,
    /// --fail-on, ...) passes through untouched for the provider's own CliOptions.Parse.</summary>
    private static string[] BuildProviderArgs(string[] args, string providerOutDir)
    {
        var list = new List<string>(args);
        for (var i = list.Count - 1; i >= 1; i--)
        {
            if (list[i] == "--out") { list.RemoveRange(i, Math.Min(2, list.Count - i)); }
        }
        if (!list.Contains("--no-open")) { list.Add("--no-open"); }
        list.Add("--out");
        list.Add(providerOutDir);
        return list.ToArray();
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Usage: arch <path> [--out <dir>] [--no-open] [--max-nodes <n>] [--exclude <dirname>]...");
        Console.Error.WriteLine("       arch code <args>    (drop-in for running archdiagram directly)");
        Console.Error.WriteLine("       arch sql <args>     (drop-in for running archsql directly)");
        Console.Error.WriteLine("       arch connect (--conn-file <path> | --env) [--out <dir>] [--no-open] ...");
    }

    private static void PrintEmptyState()
    {
        var descriptions = Providers.Select(p => p.Describes).ToList();
        Console.Error.WriteLine();
        Console.Error.WriteLine("Nothing to analyze here.");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Arch looks for {descriptions[0]} and");
        Console.Error.WriteLine($"{descriptions[1]}. This folder has neither.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("  • Pointing at the right folder?   arch c:\\path\\to\\your\\project");
        Console.Error.WriteLine("  • Analyzing a live database?      arch connect --conn-file db.json");
        Console.Error.WriteLine("  • Just want to see it work?       arch --demo");
    }
}
