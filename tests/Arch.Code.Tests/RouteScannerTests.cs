using Arch.Code;
using Arch.Code.Cli;
using Arch.Code.Graph;

namespace Arch.Code.Tests;

public class RouteScannerTests
{
    private static readonly ProjectModel Model =
        Pipeline.BuildModel(new CliOptions { SourcePath = FixturePaths.RouteSample, Open = false });

    private static HttpEndpoint Find(string methodName) =>
        Model.Endpoints.Single(e => e.MethodName == methodName);

    [Fact]
    public void Attribute_routed_action_composes_type_and_method_templates()
    {
        var e = Find("GetById");
        Assert.Equal("GET", e.Verb);
        Assert.Equal("api/orders/{id}", e.Route);
        Assert.Equal("attribute", e.Source);
    }

    [Fact]
    public void Convention_routed_action_with_no_verb_attribute_is_labelled_convention()
    {
        var e = Find("Post");
        Assert.Equal("POST", e.Verb);
        Assert.Equal("convention", e.Source);
    }

    [Fact]
    public void Non_literal_route_argument_is_unresolved_not_dropped()
    {
        var e = Find("Unresolvable");
        Assert.Equal("", e.Route);
        Assert.Equal("unresolved", e.Source);
    }

    [Fact]
    public void Minimal_api_map_call_in_top_level_statements_is_recognised()
    {
        var e = Find("<main>");
        Assert.Equal("GET", e.Verb);
        Assert.Equal("health", e.Route);
        Assert.Equal("minimal-api", e.Source);
    }
}
