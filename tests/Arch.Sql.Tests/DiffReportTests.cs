using Arch.Sql.Analysis;
using Arch.Sql.Rendering;
using Xunit;

namespace Arch.Sql.Tests;

public class DiffReportTests
{
    /// <summary>Breaking is the worst risk this report has, so it takes the danger hue and
    /// Degrading the warning one — the same Critical/High/Medium/Low mapping LintPage uses.
    /// This test used to assert Degrading rendered as "badge accent", which was the brand blue
    /// rather than a severity colour: scanning the report by colour gave amber/blue/green, which
    /// does not tell a reader that blue outranks green, and ".badge.danger" was never used at all.
    /// The three assertions live in one test because it is the ORDER that is being pinned, not
    /// three independent facts.</summary>
    [Fact]
    public void Risk_badges_run_danger_then_warn_then_ok_so_severity_is_readable_by_colour()
    {
        var changes = new List<SchemaChange>
        {
            new("table-dropped", "dbo.Gone", ChangeRisk.Breaking, "table removed"),
            new("index-dropped", "dbo.T", ChangeRisk.Degrading, "IX_T dropped"),
            new("column-added", "dbo.T.NewCol", ChangeRisk.Safe, "nullable column added"),
        };

        var html = DiffReport.Body(changes, new HashSet<string>(StringComparer.Ordinal));

        Assert.Contains("<span class=\"badge danger\">Breaking</span>", html);
        Assert.Contains("<span class=\"badge warn\">Degrading</span>", html);
        Assert.Contains("<span class=\"badge ok\">Safe</span>", html);
    }

    /// <summary>The report opens with the counts, not the row list: a release reviewer needs the
    /// breaking total before the detail, and how much of it a baseline has already accepted.</summary>
    [Fact]
    public void Body_leads_with_a_tile_row_counting_each_risk_class()
    {
        var changes = new List<SchemaChange>
        {
            new("table-dropped", "dbo.Gone", ChangeRisk.Breaking, "table removed"),
            new("column-added", "dbo.T.A", ChangeRisk.Safe, "nullable column added"),
            new("column-added", "dbo.T.B", ChangeRisk.Safe, "nullable column added"),
        };

        var html = DiffReport.Body(changes, new HashSet<string>(StringComparer.Ordinal));

        Assert.Contains("class=\"tiles\"", html);
        Assert.Contains("<div class=\"num\">1</div><div class=\"lbl\">Breaking</div>", html);
        Assert.Contains("<div class=\"num\">2</div><div class=\"lbl\">Safe</div>", html);
        // Nothing was baselined, so that tile is present but dimmed rather than dropped — the
        // row's shape has to stay comparable between two runs of the same diff.
        Assert.Contains("<div class=\"tile tile-zero\"><div class=\"num\">0</div><div class=\"lbl\">Baselined</div>", html);
    }

    /// <summary>The rows are sortable and filterable, which needs the header row inside a thead
    /// and the data rows inside a tbody — site.js's sorter bails on a table without both.</summary>
    [Fact]
    public void Body_emits_a_sortable_filterable_table_with_a_real_thead()
    {
        var changes = new List<SchemaChange> { new("table-dropped", "dbo.Gone", ChangeRisk.Breaking, "table removed") };

        var html = DiffReport.Body(changes, new HashSet<string>(StringComparer.Ordinal));

        Assert.Contains("class=\"grid sortable\"", html);
        Assert.Contains("<thead>", html);
        Assert.Contains("<tbody id=\"diff-rows\">", html);
        Assert.Contains("data-filter-target=\"#diff-rows\"", html);
        Assert.Contains("class=\"filterable\"", html);
    }
}
