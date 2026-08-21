using Xunit;

namespace Arch.Sql.Tests;

/// <summary>Pins the two contracts in the shared sortable/paginated-table engine that a page
/// author cannot see from their own page's markup. Both were regressions found by reading
/// site.js against the pages that use it, and both are invisible to every other test here
/// because the generated HTML is identical either way — the defect is in the behaviour.
/// String assertions, because this repo has no JS test runner; the point is that removing the
/// fix breaks a test rather than silently restoring the bug.</summary>
public class SiteJsTableBehaviourTests
{
    private static string SiteJs() => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "Arch.Core", "Web", "assets", "site.js"));

    /// <summary>Pagination hides rows with an inline display; the .filter-input filter hides them
    /// with the [hidden] attribute, and inline style wins. Indexing the page window by raw DOM
    /// position therefore left any filter match sitting past the page boundary carrying the
    /// display:none it was given on load — invisible, while .filter-count counted it as shown.
    /// On activity.html (page size 20, filter over the same tbody) searching for any object
    /// outside the top 20 by executions produced "1 of 200 shown" above an empty table.</summary>
    [Fact]
    public void Pagination_counts_position_among_filter_visible_rows_only()
    {
        var js = SiteJs();

        Assert.Contains("rows().filter(function (tr) { return !tr.hidden; })", js);
    }

    /// <summary>...and filtering has to re-page, or the surviving rows keep the window computed
    /// for the unfiltered order and "Show all (N more)" describes the wrong set.</summary>
    [Fact]
    public void Filtering_re_pages_any_paginated_table_over_the_same_rows()
    {
        var js = SiteJs();

        Assert.Contains("document.dispatchEvent(new CustomEvent(\"arch:filtered\"))", js);
        Assert.Contains("document.addEventListener(\"arch:filtered\"", js);
    }

    /// <summary>A sortable header keeps its implicit columnheader role. Setting role="button" on
    /// the &lt;th&gt; replaced that role, which is both what associates the header with its
    /// column's cells and the only role aria-sort is defined for — so the attribute set on the
    /// very next line was ignored by assistive tech, silencing the announcement it was added to
    /// provide. tabindex plus the keydown handler make the header operable without a role
    /// override, and wrapping the label in a real &lt;button&gt; is not open to us: several
    /// headers already contain a Glossary.Info() button and buttons cannot nest.</summary>
    [Fact]
    public void Sortable_headers_keep_their_columnheader_role()
    {
        var js = SiteJs();

        Assert.DoesNotContain("th.setAttribute(\"role\", \"button\")", js);
        Assert.Contains("th.setAttribute(\"aria-sort\", \"none\")", js);
        Assert.Contains("th.setAttribute(\"tabindex\", \"0\")", js);
    }
}
