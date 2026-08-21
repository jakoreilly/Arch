using System.Globalization;
using Arch.Sql.Analysis;
using Arch.Sql.Rendering;
using Xunit;

namespace Arch.Sql.Tests;

/// <summary>Determinism is the product: the same input must produce the same bytes on anyone's
/// machine. Arch.Sql and Arch.Cli both run with <c>InvariantGlobalization=false</c> (SqlClient
/// needs ICU), so an unqualified <c>ToString("N0")</c> in a page class picks up whatever locale
/// the machine happens to have — 1234 renders "1,234" here and "1.234" in de-DE, and two
/// colleagues scanning the same schema get byte-different sites. Every figure in a KPI tile is
/// therefore formatted with an explicit InvariantCulture; these tests are what stops the next
/// one being added without it.</summary>
public class InvariantFormattingTests
{
    private static string BodyUnder(string culture, int breakingCount)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var changes = Enumerable.Range(0, breakingCount)
                .Select(i => new SchemaChange("table-dropped", "dbo.T" + i, ChangeRisk.Breaking, "table removed"))
                .ToList();
            return DiffReport.Body(changes, new HashSet<string>(StringComparer.Ordinal));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    /// <summary>The guard that stops the real test below passing for the wrong reason. If the
    /// runtime ever resolves de-DE to the invariant culture (globalization-invariant mode, a
    /// container with no ICU), grouping separators stop differing and a culture test would pass
    /// while proving nothing. Assert the difference exists before relying on its absence.</summary>
    [Fact]
    public void The_culture_this_test_relies_on_really_does_format_differently()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.234", 1234.ToString("N0"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void Tile_figures_are_invariant_regardless_of_the_machines_locale()
    {
        var german = BodyUnder("de-DE", 1234);
        var french = BodyUnder("fr-FR", 1234);
        var american = BodyUnder("en-US", 1234);

        Assert.Contains("<div class=\"num\">1,234</div><div class=\"lbl\">Breaking</div>", german);
        Assert.Equal(american, german);
        Assert.Equal(american, french);
    }
}
