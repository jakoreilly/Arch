using Arch.Code.Graph;
using Arch.Code.Site.Pages;

namespace Arch.Code.Tests;

public class MetricsPageTests
{
    private static FileNode F(string slug, string ns, string kind = "class") => new()
    {
        RelPath = slug + ".cs", Slug = slug, Language = "C#", Loc = 10,
        Types = [new TypeInfo { Name = slug, Kind = kind, Namespace = ns }],
    };

    // A → B → C chain, no interfaces/abstract types anywhere — maxAbstractness is 0.
    private static ProjectModel Chain() => new()
    {
        RootName = "R", SourcePath = "C:/r",
        Files = { F("a", "NA"), F("b", "NB"), F("c", "NC") },
        FileDependencies =
        {
            new DepEdge { FromSlug = "a", ToSlug = "b" },
            new DepEdge { FromSlug = "b", ToSlug = "c" },
        },
    };

    [Fact]
    public void Worst_distance_is_na_when_no_module_has_abstract_types()
    {
        var html = MetricsPage.Body(Chain());
        Assert.Contains(">n/a</div><div class=\"lbl\">Worst distance (D)", html);
        Assert.DoesNotContain("style=\"border-color:var(--warn)\"><div class=\"num\">n/a", html);
    }

    [Fact]
    public void Worst_distance_is_numeric_when_a_module_has_an_interface()
    {
        var model = new ProjectModel
        {
            RootName = "R", SourcePath = "C:/r",
            Files = { F("a", "NA"), F("iface", "NB", kind: "interface") },
            FileDependencies = { new DepEdge { FromSlug = "a", ToSlug = "iface" } },
        };
        var html = MetricsPage.Body(model);
        Assert.DoesNotContain(">n/a</div><div class=\"lbl\">Worst distance (D)", html);
    }
}
