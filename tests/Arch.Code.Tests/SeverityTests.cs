using Arch.Code.Graph;
using Arch.Code.Site.Pages;

namespace Arch.Code.Tests;

public class SeverityTests
{
    private static ProjectModel ModelWithMethod(int cognitive)
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Files.Add(new FileNode
        {
            RelPath = "src/Big.cs", Slug = "big", Language = "C#",
            Types = [new TypeInfo { Name = "Big", Kind = "class", Methods = [new MethodInfo { Name = "M", Cyclomatic = 2, Cognitive = cognitive }] }],
        });
        return m;
    }

    [Fact]
    public void Very_high_complexity_gets_the_danger_badge_not_warn()
    {
        var html = HotspotsPage.Body(ModelWithMethod(21), showComplexity: true);
        Assert.Contains("<span class=\"badge danger\">21 · Very High</span>", html);
    }

    [Fact]
    public void High_complexity_still_gets_warn()
    {
        var html = HotspotsPage.Body(ModelWithMethod(15), showComplexity: true);
        Assert.Contains("<span class=\"badge warn\">15 · High</span>", html);
    }
}
