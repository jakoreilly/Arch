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

    [Fact]
    public void Both_inputs_carry_a_combobox_wired_to_their_own_autocomplete_list()
    {
        var model = new ProjectModel { RootName = "Sample", SourcePath = "C:/sample" };

        var html = TracePage.Body(model, "{\"nodes\":[],\"edges\":[]}");

        Assert.Contains("id=\"trace-from\"", html);
        Assert.Contains("aria-controls=\"trace-from-list\"", html);
        Assert.Contains("id=\"trace-from-list\" role=\"listbox\"", html);
        Assert.Contains("id=\"trace-to\"", html);
        Assert.Contains("aria-controls=\"trace-to-list\"", html);
        Assert.Contains("id=\"trace-to-list\" role=\"listbox\"", html);
    }

    [Fact]
    public void Carries_a_result_count_span_and_an_examples_container_for_site_js_to_fill_in()
    {
        var model = new ProjectModel { RootName = "Sample", SourcePath = "C:/sample" };

        var html = TracePage.Body(model, "{\"nodes\":[],\"edges\":[]}");

        Assert.Contains("id=\"trace-count\"", html);
        Assert.Contains("id=\"trace-examples\"", html);
    }
}
