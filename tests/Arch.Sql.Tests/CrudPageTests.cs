using Arch.Sql.Model;
using Arch.Sql.Site;
using Arch.Sql.Site.Pages;
using Xunit;

namespace Arch.Sql.Tests;

public class CrudPageTests
{
    [Fact]
    public void File_column_links_to_the_defining_file_not_the_actor_object_page()
    {
        var actor = new DbObject { Id = "dbo.p", Schema = "dbo", Name = "P", Kind = "procedure", Dialect = "tsql", DefinedInSlug = "dbo_p_a1b2c3d4" };
        var target = new DbObject { Id = "dbo.t", Schema = "dbo", Name = "T", Kind = "table", Dialect = "tsql" };
        var file = new SqlFile { RelPath = "procs/p.sql", Slug = "dbo_p_a1b2c3d4", Dialect = "tsql" };
        var model = new SqlModel
        {
            RootName = "x", SourcePath = "x",
            Objects = [actor, target],
            Files = [file],
            Crud = [new CrudEntry { Actor = actor.Id, Target = target.Id, Ops = "R" }],
        };
        var html = CrudPage.Body(SiteContext.Build(model));

        Assert.Contains("href=\"files/dbo_p_a1b2c3d4.html\"", html);
        Assert.Contains(">procs/p.sql<", html);
        Assert.DoesNotContain("href=\"object.html?id=dbo.p\">dbo_p_a1b2c3d4<", html);
    }
}
