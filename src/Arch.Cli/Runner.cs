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
            var providerArgs = BuildProviderArgs(args, providerOutDir, provider, combined);
            try
            {
                var model = provider.Generate(sourceFull, providerOutDir, providerArgs);
                if (provider.Id == "code") { codeResult = model; codeArgs = providerArgs; }
                if (provider.Id == "sql") { sqlResult = model; }
                var (title, icon) = DisplayInfo(provider.Id);
                var (grade, gradeClass, gradeDetail) = Grade(model);
                links.Add(new HubPage.Link(provider.Id, title, icon, detection.Summary, HeadlineStats(model),
                                           grade, gradeClass, KeyPages(provider.Id), gradeDetail));
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
            var owner = codeResult is ProjectModel om ? om.Owner : "";
            var capabilities = codeResult is ProjectModel cm
                ? cm.Capabilities.Select(c => c.Name).ToList()
                : [];
            HubPage.Write(outDir, siteName, links, sourceFull, generatedOn, HubDbLinks(joinedDatabases),
                          TopActions(codeResult, sqlResult), owner, capabilities);
            indexPath = Path.Combine(outDir, "index.html");

            // Must run after HubPage.Write, not just after the provider loop: the hub's own
            // outDir/assets/ (the dedup's "canonical" copy every subsite links to) is written
            // by HubPage.Write itself (SiteAssets.CopyTo(outDir)) — running this any earlier
            // finds no canonical file yet and silently dedupes nothing.
            DedupeVendorAssets(outDir);
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

    /// <summary>The subsite's overall grade, normalised to ONE vocabulary. Both analysers grade on
    /// the same Ok/Watch/Fail scale but print it differently on their own pages ("AT RISK" vs
    /// "Fail"), which is fine in isolation and actively misleading when the two sit side by side on
    /// the hub. The words below are the hub's, and the badge colour is the shared semantic token.</summary>
    private static (string Grade, string Class, string Detail) Grade(object? model)
    {
        switch (model)
        {
            case ProjectModel p:
            {
                var card = ScorecardBuilder.Build(p);
                var rank = card.Overall switch
                {
                    ScorecardBuilder.Status.Ok => 0,
                    ScorecardBuilder.Status.Watch => 1,
                    ScorecardBuilder.Status.Fail => 2,
                    _ => 3,
                };
                var attention = card.Rows.Count(r => r.Status is ScorecardBuilder.Status.Watch or ScorecardBuilder.Status.Fail);
                var graded = card.Rows.Count(r => r.Status != ScorecardBuilder.Status.NA);
                return GradeWords(rank, attention, graded);
            }
            case SqlModel s:
            {
                var card = Arch.Sql.Analysis.SqlScorecard.Build(s);
                var rank = card.Overall switch
                {
                    Arch.Sql.Analysis.SqlScorecard.Status.Ok => 0,
                    Arch.Sql.Analysis.SqlScorecard.Status.Watch => 1,
                    Arch.Sql.Analysis.SqlScorecard.Status.Fail => 2,
                    _ => 3,
                };
                var attention = card.Rows.Count(r => r.Status is Arch.Sql.Analysis.SqlScorecard.Status.Watch
                                                              or Arch.Sql.Analysis.SqlScorecard.Status.Fail);
                var graded = card.Rows.Count(r => r.Status != Arch.Sql.Analysis.SqlScorecard.Status.NA);
                return GradeWords(rank, attention, graded);
            }
            default:
                return ("", "", "");
        }
    }

    private static (string, string, string) GradeWords(int rank, int attention, int graded)
    {
        var detail = graded == 0
            ? "nothing could be measured"
            : attention == 0
                ? $"all {graded:N0} signals passing"
                : $"{attention:N0} of {graded:N0} signals need attention";
        return rank switch
        {
            0 => ("HEALTHY", "ok", detail),
            1 => ("NEEDS WORK", "warn", detail),
            2 => ("AT RISK", "danger", detail),
            _ => ("NOT GRADED", "", detail),
        };
    }

    /// <summary>The pages worth a direct link from the hub. Hard-coded per provider rather than
    /// discovered, because "which pages matter" is an editorial judgement, not a fact about the
    /// output directory — and a link to a page that exists but nobody needs is noise.</summary>
    private static IReadOnlyList<HubPage.Page> KeyPages(string providerId) => providerId switch
    {
        "code" =>
        [
            new("System Brief", "brief.html"),
            new("Scorecard", "scorecard.html"),
            new("Refactoring", "refactor.html"),
            new("Ops & Network", "ops.html"),
            new("Config & Secrets", "config.html"),
        ],
        "sql" =>
        [
            new("Objects", "objects.html"),
            new("ER Diagram", "er.html"),
            new("Lint", "lint.html"),
            new("Scorecard", "scorecard.html"),
            new("Impact", "impact.html"),
        ],
        _ => [],
    };

    /// <summary>The merged backlog, worst first, capped at five. Code items come from the
    /// refactoring backlog (which already ranks itself); SQL items from its scorecard's failing
    /// rows, since Arch.Sql has no backlog of its own. Ordering is severity-then-source so the
    /// list is stable run to run — two items of equal severity must not be free to swap.</summary>
    private static IReadOnlyList<HubPage.Action> TopActions(object? codeResult, object? sqlResult)
    {
        var actions = new List<(int Rank, HubPage.Action Action)>();

        if (codeResult is ProjectModel code)
        {
            foreach (var item in RefactoringBacklog.Build(code).Take(5))
            {
                var (label, cls, rank) = item.Severity switch
                {
                    RefactoringBacklog.Sev.Critical => ("critical", "danger", 0),
                    RefactoringBacklog.Sev.High => ("high", "danger", 1),
                    RefactoringBacklog.Sev.Medium => ("medium", "warn", 2),
                    _ => ("low", "", 3),
                };
                actions.Add((rank, new HubPage.Action(label, cls, item.Title, $"code/{item.Link}", "Code Analysis")));
            }
        }

        if (sqlResult is SqlModel sql)
        {
            foreach (var row in Arch.Sql.Analysis.SqlScorecard.Build(sql).Rows
                         .Where(r => r.Status is Arch.Sql.Analysis.SqlScorecard.Status.Fail
                                              or Arch.Sql.Analysis.SqlScorecard.Status.Watch))
            {
                var fail = row.Status == Arch.Sql.Analysis.SqlScorecard.Status.Fail;
                var href = row.Link.Length > 0 ? $"sql/{row.Link}" : "sql/scorecard.html";
                actions.Add((fail ? 1 : 2,
                    new HubPage.Action(fail ? "high" : "medium", fail ? "danger" : "warn",
                                       $"{row.Metric}: {row.Value}", href, "SQL Analysis")));
            }
        }

        return actions
            .OrderBy(a => a.Rank)
            .ThenBy(a => a.Action.Source, StringComparer.Ordinal)
            .ThenBy(a => a.Action.Text, StringComparer.Ordinal)
            .Take(5)
            .Select(a => a.Action)
            .ToList();
    }

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

    /// <summary>Union of every provider's own <see cref="IAnalysisProvider.KnownFlags"/>,
    /// used only to decide arity (does a flag take a following value?) when
    /// <see cref="BuildProviderArgs"/> drops a flag that belongs to a *different* provider.
    /// Both providers agree on the arity of every flag name they happen to share, so there is
    /// no ambiguity to resolve.</summary>
    private static readonly IReadOnlyDictionary<string, bool> AllKnownFlags = BuildAllKnownFlags();

    private static Dictionary<string, bool> BuildAllKnownFlags()
    {
        // Not a plain ToDictionary: both providers declare --out/--no-open (with the same
        // arity), which ToDictionary would reject as a duplicate key even though there is no
        // real conflict — last-write-wins here, which is fine since every shared name agrees.
        var combined = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var provider in Providers)
        {
            foreach (var (name, takesValue) in provider.KnownFlags) { combined[name] = takesValue; }
        }
        return combined;
    }

    /// <summary>The original argv, with --out replaced by this provider's actual output
    /// folder and --no-open forced on — Runner opens exactly one browser tab at the end,
    /// not each provider independently. In single-provider mode every other flag passes
    /// through untouched, same as always. In combined mode (more than one provider applies)
    /// the same argv is offered to each provider in turn, so a flag only a *different*
    /// provider declares — e.g. Arch.Sql's --dialect reaching Arch.Code, or vice versa — is
    /// dropped here (its value too, if it takes one) instead of reaching this provider's own
    /// CliOptions.Parse, which would otherwise hard-fail on it as an unknown argument
    /// (continue.md's disclosed Phase 5 limitation). A flag neither provider declares is left
    /// alone either way, so Parse still reports a genuinely bad flag itself.</summary>
    private static string[] BuildProviderArgs(string[] args, string providerOutDir, IAnalysisProvider provider, bool combined)
    {
        var list = new List<string> { args[0] };
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--out") { i++; continue; }
            if (!combined || provider.KnownFlags.ContainsKey(arg) || !AllKnownFlags.TryGetValue(arg, out var takesValue))
            {
                list.Add(arg);
                continue;
            }
            if (takesValue && i + 1 < args.Length) { i++; }   // owned by another provider — drop it and its value
        }
        if (!list.Contains("--no-open")) { list.Add("--no-open"); }
        list.Add("--out");
        list.Add(providerOutDir);
        return list.ToArray();
    }

    /// <summary>Combined mode writes the hub's own assets/ plus one full copy inside each
    /// subsite (SiteAssets.CopyTo is called independently per site — see CLAUDE.md:
    /// "Determinism is the product" and this class's own doc comment on byte-identical
    /// standalone output, both of which rule out changing any subsite's asset PATHS).
    /// This collapses the large vendored libraries (mermaid.min.js, 3d-force-graph.min.js)
    /// that are guaranteed byte-identical across every copy — same shared assets/ source
    /// tree, never modified per-provider — down to NTFS hardlinks. Every subsite still
    /// has its own real file at its own path with identical content; only the on-disk
    /// bytes are shared. Content is never compared byte-by-byte: identical relative path
    /// under every copy's own assets/lib/ is the guarantee, not a hash check, because the
    /// files are known to originate from the same untouched vendored copy shipped in the
    /// exe's own output directory.</summary>
    private static void DedupeVendorAssets(string outDir)
    {
        var relPaths = new[] { "assets/lib/mermaid.min.js", "assets/lib/3d-force-graph.min.js" };
        foreach (var rel in relPaths)
        {
            var canonical = Path.Combine(outDir, rel);
            if (!File.Exists(canonical)) { continue; }
            foreach (var subsite in new[] { "code", "sql" })
            {
                var copy = Path.Combine(outDir, subsite, rel);
                if (!File.Exists(copy)) { continue; }
                try
                {
                    File.Delete(copy);
                    // CreateHardLink returns false (not a thrown exception) on failure — e.g.
                    // cross-volume outDir or a filesystem without hardlink support — so a missed
                    // check here would silently leave the just-deleted file gone. Promote it to
                    // an exception so the catch below's fallback copy actually runs.
                    if (!CreateHardLink(copy, canonical, IntPtr.Zero))
                    {
                        throw new IOException($"CreateHardLink failed (Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Cross-volume outDir, or a filesystem without hardlink support (e.g. a
                    // network share). Not fatal: the copy either still exists (delete failed,
                    // caught before the link attempt) or is gone with the link failed too, in
                    // which case restore it exactly as SiteAssets.CopyTo originally would.
                    if (!File.Exists(copy)) { File.Copy(canonical, copy, overwrite: true); }
                }
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

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
