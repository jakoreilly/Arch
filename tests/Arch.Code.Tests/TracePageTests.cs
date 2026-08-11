using Arch.Code.Graph;
using Arch.Code.Site.Pages;

namespace Arch.Code.Tests;

public class TracePageTests
{
    [Fact]
    public void Body_renders_two_search_inputs_and_the_embedded_payload()
    {
        var model = new ProjectModel { RootName = "Sample", SourcePath = "C:/sample" };

        var html = TracePage.Body(model, "{\"nodes\":[],\"edges\":[]}");

        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, "class=\"filter-input\"").Count);
        Assert.Contains("window.ARCH_TRACE={\"nodes\":[],\"edges\":[]};", html);
    }

    [Fact]
    public void Empty_model_pre_renders_the_type_a_name_above_empty_state()
    {
        var model = new ProjectModel { RootName = "Sample", SourcePath = "C:/sample" };

        var html = TracePage.Body(model, "{\"nodes\":[],\"edges\":[]}");

        Assert.Contains("Type a class, method, route, or file name above to trace from it.", html);
    }
}
