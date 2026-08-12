using Arch.Code.Graph;
using Arch.Code.Site.Pages;

namespace Arch.Code.Tests;

public class RefactorPageTests
{
    [Fact]
    public void Critical_items_get_the_danger_badge_not_warn()
    {
        var m = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        m.Projects.Add(new CsprojInfo
        {
            Name = "Api", RelPath = "Api/Api.csproj",
            ConnectionStrings = [new DbUse { Hash = "h", Label = "db", HasCredential = true }],
        });
        var html = RefactorPage.Body(m);

        Assert.Contains("<span class=\"badge danger\">critical</span>", html);
    }
}
