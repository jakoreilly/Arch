namespace Arch.Sql.Site.Pages;

public static class GuidePage
{
    private static readonly (string Href, string Title, string What)[] PageGuide =
    [
        ("index.html", "Overview", "Stat tiles, dialect mix, overall grade, and the ER diagram."),
        ("guide.html", "Guide", "This page — an orientation tour of what each page shows and how this site was built."),
        ("explore.html", "Explore", "A query console over the dependency graph — referencedby:, affects:, orphans, numeric filters."),
        ("objects.html", "Objects", "Every table, view, procedure, function and trigger found. Each links to its detail + neighborhood diagram."),
        ("domains.html", "Domains", "Likely bounded contexts, grouped by object name prefix, and how much each one reaches into the others."),
        ("er.html", "ER Diagram", "Tables and their foreign-key relationships."),
        ("relationships.html", "Relationships", "Likely relationships between tables inferred from column-naming patterns rather than declared foreign keys — a lead to confirm, not a fact."),
        ("dependencies.html", "Dependencies", "Which objects reference which — procedures calling procedures, views selecting from tables, and so on."),
        ("graph.html", "Graph (3D)", "The whole schema as an interactive force-directed 3D graph; click a node to focus its neighbourhood."),
        ("crud.html", "CRUD Matrix", "Which procedures/triggers/views Create, Read, Update or Delete each table."),
        ("lint.html", "Lint", "SonarQube-style findings: security, correctness, performance and maintainability issues."),
        ("scorecard.html", "Scorecard", "A worst-wins health grade across the same signals as Lint, at a glance."),
        ("metrics.html", "Metrics", "Fan-in/fan-out coupling and procedure complexity."),
        ("impact.html", "Impact", "What breaks if you change an object — its transitive dependents (blast radius)."),
        ("activity.html", "Activity", "Runtime hotspots, index issues and issue concentration — populated only from a live connection with DMV permission."),
        ("indexes.html", "Indexes", "Index health from the connected server's catalog: heaps, duplicate/overlapping indexes, and indexes never read according to runtime counters."),
        ("drift.html", "Schema Diff", "Schema drift since a baseline scan, when one was supplied via --baseline."),
        ("config.html", "Config & Secrets", "Files that embed a credential — the fact only, never the value."),
    ];

    public static string Body(SiteContext ctx)
    {
        var live = ctx.Model.Runtime.Source == "live-mssql";
        var sourceLine = live
            ? "This site was built from a read-only connection to a live SQL Server: schema is read from catalog views and runtime figures from DMVs. It only issues SELECT queries and never writes."
            : "Arch read the .sql files you pointed it at; no database was connected. It can also build this site from a read-only live connection (the `connect` verb).";

        var rows = new System.Text.StringBuilder();
        foreach (var (href, title, what) in PageGuide)
        {
            rows.Append($"<tr><td><a href=\"{href}\">{Html.Encode(title)}</a></td><td>{Html.Encode(what)}</td></tr>");
        }

        return $$"""
<h1>Guide</h1>
<p class="lede">Arch turns a folder of SQL scripts (or a live SQL Server) into this site. It
supports T-SQL (SQL Server), MySQL and PostgreSQL. T-SQL is parsed in full; MySQL and PostgreSQL use
a lighter-weight parse, so objects from those files are badged 'shallow parse' and a few complex
references may be missing. When a file's dialect can't be told apart, Arch assumes T-SQL.</p>

<h2>Where to start</h2>
<p class="lede">New here? Use <a href="explore.html">Explore</a> to search objects and ask the graph
questions, open any object to see its <a href="objects.html">neighborhood</a>, or orbit the whole
schema in the <a href="graph.html">3D Graph</a>.</p>

<h2>What each page shows</h2>
<table class="grid">
<thead><tr><th>Page</th><th>What it shows</th></tr></thead>
<tbody>{{rows}}</tbody>
</table>

<h2>How this site was built</h2>
<p class="note">{{sourceLine}} Everything works from file:// with no network.</p>
""";
    }
}
