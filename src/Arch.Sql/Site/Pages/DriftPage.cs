using Arch.Sql.Analysis;
using Arch.Sql.Rendering;

namespace Arch.Sql.Site.Pages;

/// <summary>Schema drift since a baseline scan, when one was supplied. Reuses the same schema-diff
/// report the standalone diff verb produces. Empty (with guidance) when no baseline was given.</summary>
public static class DriftPage
{
    public static string Body(List<SchemaChange>? changes)
    {
        if (changes is null)
        {
            // The lede is repeated here rather than shared with DiffReport.Body: this branch never
            // reaches that method, so without it the no-baseline page was the one page on the site
            // opening with a bare h1 and no sentence saying what it shows once configured.
            return """
<h1>Schema Diff</h1>
<p class="lede">What changed between this scan and an earlier one, classified by risk — breaking
drops and type tightenings, degrading index/FK removals, and additive-safe changes.</p>
<div class="panel empty-state"><div class="big" aria-hidden="true">◇</div>
<p>No baseline was supplied for this run. Pass <code>--baseline &lt;model.json&gt;</code> — the
<code>model.json</code> written by an earlier scan or connection — to see what changed since then.</p>
</div>
""";
        }
        return DiffReport.Body(changes, new HashSet<string>(StringComparer.Ordinal));
    }
}
