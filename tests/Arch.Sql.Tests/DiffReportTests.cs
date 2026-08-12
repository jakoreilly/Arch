using Arch.Sql.Analysis;
using Arch.Sql.Rendering;
using Xunit;

namespace Arch.Sql.Tests;

public class DiffReportTests
{
    [Fact]
    public void Degrading_risk_gets_the_accent_badge_not_a_bare_colourless_one()
    {
        var changes = new List<SchemaChange> { new("index-dropped", "dbo.T", ChangeRisk.Degrading, "IX_T dropped") };
        var html = DiffReport.Body(changes, new HashSet<string>(StringComparer.Ordinal));

        Assert.Contains("<span class=\"badge accent\">Degrading</span>", html);
    }
}
