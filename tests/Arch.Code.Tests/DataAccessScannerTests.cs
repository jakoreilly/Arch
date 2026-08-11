using Arch.Code;
using Arch.Code.Cli;
using Arch.Code.Graph;

namespace Arch.Code.Tests;

public class DataAccessScannerTests
{
    private static readonly ProjectModel Model =
        Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.RouteSample, Open = false });

    [Fact]
    public void Dapper_call_with_a_literal_command_text_is_attributed_to_the_calling_method()
    {
        var d = Model.DataAccess.Single(d => d.MethodName == "CountOrders");
        Assert.Equal("dbo.Orders", d.ObjectName);
        Assert.Equal("R", d.Ops);
        Assert.Equal("dapper", d.Source);
        Assert.False(d.IsBlindSpot);
    }

    [Fact]
    public void Interpolated_sql_is_reported_as_a_blind_spot_not_silently_dropped()
    {
        var d = Model.DataAccess.Single(d => d.MethodName == "ArchiveOrder");
        Assert.True(d.IsBlindSpot);
        Assert.Equal("?", d.Ops);
        Assert.Equal("", d.ObjectName);
    }

    [Fact]
    public void Ef_core_dbset_access_paired_with_savechanges_is_detected_same_type()
    {
        var d = Model.DataAccess.Single(d => d.MethodName == "AddOrder");
        Assert.Equal("Orders", d.ObjectName);
        Assert.Equal("RU", d.Ops);
        Assert.Equal("ef-core", d.Source);
        Assert.False(d.IsBlindSpot);
    }
}
