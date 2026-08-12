using Arch.Sql.Model;
using Arch.Sql.Site;
using Arch.Sql.Site.Pages;
using Xunit;

namespace Arch.Sql.Tests;

public class ObjectFilePageTests
{
    [Fact]
    public void Type_header_links_to_the_objects_own_detail_page()
    {
        var obj = new DbObject { Id = "dbo.t", Schema = "dbo", Name = "T", Kind = "table", Dialect = "tsql" };
        var file = new SqlFile { RelPath = "t.sql", Slug = "t-slug", Dialect = "tsql", ObjectIds = [obj.Id] };
        var model = new SqlModel { RootName = "x", SourcePath = "x", Objects = [obj], Files = [file] };
        var html = ObjectFilePage.Body(SiteContext.Build(model), file);

        Assert.Contains($"""<a class="type-name" href="object.html?id={Uri.EscapeDataString(obj.Id)}">""", html);
    }
}
