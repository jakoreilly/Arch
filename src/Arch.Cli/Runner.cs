using System.Diagnostics;
using System.Globalization;
using Arch.Code.Analysis;
using Arch.Code.Cli;
using Arch.Code.Graph;
using Arch.Core.Detection;
using Arch.Sql.Cli;
using Arch.Sql.Model;

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
        object? codeResult = null;
        object? sqlResult = null;
        string[]? codeArgs = null;
        foreach (var (provider, detection) in applying)
        {
            var providerOutDir = combined ? Path.Combine(outDir, provider.Id) : outDir;
            var providerArgs = BuildProviderArgs(args, providerOutDir);
            try
            {
                var model = provider.Generate(sourceFull, providerOutDir, providerArgs);
                if (provider.Id == "code") { codeResult = model; codeArgs = providerArgs; }
                if (provider.Id == "sql") { sqlResult = model; }
                var (title, icon) = DisplayInfo(provider.Id);
                links.Add(new HubPage.Link(provider.Id, title, icon, detection.Summary, HeadlineStats(model)));
            }
            catch (Exception ex)
            {
                anyFailed = true;
                Console.Error.WriteLine($"  {provider.Id,-4} FAILED — {ex.Message}");
            }
        }

        // Phase 6: only meaningful when both a code and a sql provider ran and succeeded in the
        // same (necessarily combined-mode) run — re-renders the code site with each detected
        // database's cross-link outcome attached. See CrossLink.Apply's own doc comment for why
        // this is a cheap no-op when the code side found no databases at all.
        IReadOnlyList<DbNode> joinedDatabases = [];
        if (codeResult is ProjectModel codeModel && sqlResult is SqlModel sqlModel && codeArgs is not null)
        {
            joinedDatabases = CrossLink.Apply(codeModel, sqlModel, codeArgs, "../sql");
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
            var generatedOn = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            HubPage.Write(outDir, siteName, links, sourceFull, generatedOn, HubDbLinks(joinedDatabases));
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

    /// <summary>The handful of figures the hub card shows for a provider, chosen to agree with
    /// the headline tiles on the subsite that card links to — code counts first-party files and
    /// lines (tests and vendored bundles excluded, as CodebaseStats defines it), sql counts the
    /// same object kinds its own Overview leads with. An unrecognised model type contributes no
    /// figures rather than guessing, so a future provider degrades to a summary-only card.</summary>
    private static IReadOnlyList<HubPage.Stat> HeadlineStats(object? model) => model switch
    {
        ProjectModel p => CodeStats(p),
        SqlModel s => SqlStats(s),
        _ => [],
    };

    private static IReadOnlyList<HubPage.Stat> CodeStats(ProjectModel model) =>
    [
        Count(model.Files.Count, "file"),
        Count(CodebaseStats.FirstPartyLanguageLoc(model).Values.Sum(), "line"),
        Count(model.Projects.Count, "project"),
        Count(model.Files.Where(CodebaseStats.IsFirstParty).Sum(f => f.Types.Count), "type"),
    ];

    private static IReadOnlyList<HubPage.Stat> SqlStats(SqlModel model) =>
    [
        Count(model.Objects.Count(o => o.Kind == "table"), "table"),
        Count(model.Objects.Count(o => o.Kind == "view"), "view"),
        Count(model.Objects.Count(o => o.Kind is "procedure" or "function" or "trigger"), "routine"),
        Count(model.ForeignKeys.Count, "foreign key"),
    ];

    /// <summary>A figure and its noun, pluralised — "1 project", not "1 projects". Invariant
    /// culture for the same reason MarkdownExporter needs it: Arch.Cli runs with
    /// InvariantGlobalization=false, so an implicit-culture format here would render
    /// differently from the standalone exes (see continue.md, Phase 5 findings).</summary>
    private static HubPage.Stat Count(int n, string noun) =>
        new(n.ToString("N0", CultureInfo.InvariantCulture), n == 1 ? noun : noun + "s");

    /// <summary>Renders the cross-layer join for the hub. Databases whose SqlLink is null were
    /// never examined (no catalog in the connection string) and are left off entirely — the panel
    /// only reports on joins that actually ran. The href is rebuilt hub-relative rather than
    /// reusing SqlLink.Href, which is deliberately relative to the *code* site.</summary>
    private static IReadOnlyList<HubPage.DbLink> HubDbLinks(IReadOnlyList<DbNode> databases) =>
        databases
            .Where(db => db.SqlLink is not null)
            .Select(db =>
            {
                var link = db.SqlLink!;
                var (status, badge) = (link.Matched, link.Verified) switch
                {
                    (true, true) => ("verified match", "ok"),
                    (true, false) => ("matched by name", "accent"),
                    _ => ("not in this scan", "warn"),
                };
                var href = link.Matched
                    ? $"sql/objects.html?catalog={Uri.EscapeDataString(db.Catalog.Trim())}"
                    : "";
                return new HubPage.DbLink(db.Label, status, badge, href);
            })
            .ToList();

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
        Console.Error.WriteLine("       arch landscape <parent-dir> [--out <dir>] [--only <a,b>] [--title <t>] [--no-open]");
        Console.Error.WriteLine("                           cross-reference every site already generated under <parent-dir>");
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
