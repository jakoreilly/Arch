using System.Text;
using Arch.Code.Graph;
using Arch.Code.Rendering;
using Arch.Code.Site;
using Arch.Code.Site.Pages;

namespace Arch.Code;

/// <summary>Writes the complete static site: shared assets, overview + drill-down
/// pages, one page per file, and model.json. Everything is relative-path only.</summary>
public static class SiteGenerator
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string Generate(ProjectModel model, string outDir, int maxNodes, string generatedOn,
        bool showComplexity = false, bool showSnippets = false, bool wiki = false)
    {
        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(Path.Combine(outDir, "files"));
        SiteAssets.CopyTo(outDir, "assets-code");

        // Computed once: fan-in/out, call indexes, scorecard, metrics, importance ranking and
        // the 3D graph payload. Every page below reuses this instead of recomputing its own
        // copy.
        var ctx = SiteContext.Build(model);

        // NAMING RULE: the title passed here, the last breadcrumb, and the page's label in
        // PageTemplate.NavSections are the same string. They used to drift — packages.html was
        // "Dependencies & Stack" in the sidebar, "External Dependencies" in the browser tab and
        // "Packages" in the breadcrumb, three names for one page — and the tab title is how a
        // reader finds their way back to a page from history or a crowded tab bar. A page's <h1>
        // may still elaborate ("Method Call Graph" under a "Call Graph" nav entry): it has the
        // width and the context that a 230px sidebar and a tab strip do not.
        WritePage(outDir, "index.html", "Overview", model, "index.html", "",
            Html.Crumbs((null, "Overview")),
            IndexPage.Body(ctx, maxNodes, generatedOn));

        WritePage(outDir, "brief.html", "System Brief", model, "brief.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "System Brief")),
            BriefPage.Body(model, generatedOn));

        WritePage(outDir, "guide.html", "Guide", model, "guide.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Guide")),
            GuidePage.Body(model));

        WritePage(outDir, "structure.html", "Structure", model, "structure.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Structure")),
            StructurePage.Body(model));

        WritePage(outDir, "dependencies.html", "Dependencies", model, "dependencies.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Dependencies")),
            DependenciesPage.Body(model, maxNodes));

        WritePage(outDir, "modules.html", "Modules", model, "modules.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Modules")),
            ModulesPage.Body(model, maxNodes));

        WritePage(outDir, "layers.html", "Dependency Direction", model, "layers.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Dependency Direction")),
            LayeringPage.Body(model));

        WritePage(outDir, "metrics.html", "Metrics", model, "metrics.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Metrics")),
            MetricsPage.Body(ctx));

        WritePage(outDir, "scorecard.html", "Scorecard", model, "scorecard.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Scorecard")),
            ScorecardPage.Body(model));

        WritePage(outDir, "refactor.html", "Refactoring", model, "refactor.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Refactoring")),
            RefactorPage.Body(model));

        WritePage(outDir, "types.html", "Types & Members", model, "types.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Types & Members")),
            TypesPage.Body(model, maxNodes));

        WritePage(outDir, "api.html", "API Surface", model, "api.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "API Surface")),
            ApiSurfacePage.Body(model));

        WritePage(outDir, "calls.html", "Call Graph", model, "calls.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Call Graph")),
            CallsPage.Body(model, maxNodes));

        WritePage(outDir, "packages.html", "Dependencies & Stack", model, "packages.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Dependencies & Stack")),
            PackagesPage.Body(model));

        WritePage(outDir, "config.html", "Config & Secrets", model, "config.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Config & Secrets")),
            ConfigSecretsPage.Body(model));

        WritePage(outDir, "ops.html", "Ops & Network", model, "ops.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Ops & Network")),
            OpsPage.Body(model));

        WritePage(outDir, "hotspots.html", "Hotspots", model, "hotspots.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Hotspots")),
            HotspotsPage.Body(ctx, showComplexity));

        WritePage(outDir, "evolution.html", "Evolution", model, "evolution.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Evolution")),
            EvolutionPage.Body(model));

        WritePage(outDir, "graph.html", "Graph (3D)", model, "graph.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Graph (3D)")),
            GraphPage.Body(model, ctx.GraphJson));

        WritePage(outDir, "explore.html", "Explore", model, "explore.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Explore")),
            ExplorePage.Body(model, ctx.GraphJson));

        WritePage(outDir, "trace.html", "Trace", model, "trace.html", "",
            Html.Crumbs(("index.html", "Overview"), (null, "Trace")),
            TracePage.Body(model, ctx.TraceJson));

        // One page per file — each iteration reads only shared, already-built (read-only) state
        // (ctx, model) and writes its own distinct path (file.Slug is unique per MakeSlug), so
        // this is safe to run in parallel. On a large repo this loop, not the analysis pass, is
        // the slow part of generation.
        Parallel.ForEach(model.Files, file =>
        {
            var crumbs = Html.Crumbs(("../index.html", "Overview"), ("../structure.html", "Structure"), (null, file.RelPath));
            // activeHref "structure.html", not "": a file page is reached from Structure, and an
            // empty activeHref left every nav entry unhighlighted, so drilling into a file lost
            // all sense of place in the sidebar. Nav hrefs are relative and relRoot is applied
            // separately, so the un-prefixed href is what matches here.
            var html = PageTemplate.Render(file.RelPath, model.RootName, "structure.html", "../", crumbs, FilePage.Body(ctx, file, maxNodes, showComplexity, showSnippets), navItems: null, sourceLink: model.SourceLink);
            File.WriteAllText(Path.Combine(outDir, "files", file.Slug + ".html"), html, Utf8NoBom);
        });

        ModelJsonWriter.Write(model, Path.Combine(outDir, "model.json"));
        GraphDataWriter.WriteJson(ctx.GraphJson, Path.Combine(outDir, "graph.json"));
        SearchIndexWriter.Write(model, Path.Combine(outDir, "assets", "search-index.js"));
        MarkdownExporter.Write(ctx, Path.Combine(outDir, "ARCHITECTURE.md"), maxNodes, generatedOn);
        if (wiki)
        {
            WikiExporter.Write(ctx, Path.Combine(outDir, "wiki"), maxNodes, generatedOn, showComplexity);
        }
        return Path.Combine(outDir, "index.html");
    }

    private static void WritePage(string outDir, string fileName, string title, ProjectModel model,
        string activeHref, string relRoot, string crumbs, string body)
    {
        var html = PageTemplate.Render(title, model.RootName, activeHref, relRoot, crumbs, body, navItems: null, sourceLink: model.SourceLink);
        File.WriteAllText(Path.Combine(outDir, fileName), html, Utf8NoBom);
    }
}
