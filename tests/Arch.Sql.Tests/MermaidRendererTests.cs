using Arch.Sql.Model;
using Arch.Sql.Site;
using Xunit;

namespace Arch.Sql.Tests;

public class MermaidRendererTests
{
    private static SqlModel ModelWithTables(int count)
    {
        var tables = Enumerable.Range(0, count)
            .Select(i => new DbObject { Id = $"dbo.t{i}", Schema = "dbo", Name = $"T{i}", Kind = "table", Dialect = "tsql" })
            .ToList();
        return new SqlModel { RootName = "x", SourcePath = "x", Objects = tables };
    }

    [Fact]
    public void BuildEr_reports_trimmed_and_the_real_shown_total_counts_when_capped()
    {
        var result = MermaidRenderer.BuildEr(ModelWithTables(5), maxNodes: 3);

        Assert.True(result.Trimmed);
        Assert.Equal(3, result.Shown);
        Assert.Equal(5, result.Total);
    }

    [Fact]
    public void BuildEr_reports_not_trimmed_when_under_the_cap()
    {
        var result = MermaidRenderer.BuildEr(ModelWithTables(2), maxNodes: 10);

        Assert.False(result.Trimmed);
        Assert.Equal(2, result.Shown);
        Assert.Equal(2, result.Total);
    }
}
