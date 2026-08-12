using System.Text.RegularExpressions;
using Arch.Sql.Model;
using Arch.Sql.Site;
using Arch.Sql.Site.Pages;
using Xunit;

namespace Arch.Sql.Tests;

public class GuidePageTests
{
    // Every nav href must appear as a link somewhere in the Guide's own rendered page —
    // a page that is missing from the Guide is a page nobody is told exists.
    [Fact]
    public void Every_nav_href_is_linked_from_the_guide_page()
    {
        var model = new SqlModel { RootName = "x", SourcePath = "x" };
        var ctx = SiteContext.Build(model);
        var html = GuidePage.Body(ctx);
        var linked = Regex.Matches(html, "href=\"([a-z0-9-]+\\.html)\"").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        var navHrefs = PageTemplate.NavSections.SelectMany(s => s.Items).Select(i => i.Href).ToHashSet(StringComparer.Ordinal);

        var missing = navHrefs.Except(linked).ToList();
        Assert.True(missing.Count == 0, "Guide page is missing links to: " + string.Join(", ", missing));
    }
}
