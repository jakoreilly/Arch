using System.Text.RegularExpressions;
using Arch.Code.Graph;
using Arch.Code.Site;
using Arch.Code.Site.Pages;

namespace Arch.Code.Tests;

public class GuidePageTests
{
    // Every nav href must appear as a link somewhere in the Guide's own rendered page —
    // a page that is missing from the Guide is a page nobody is told exists.
    [Fact]
    public void Every_nav_href_is_linked_from_the_guide_page()
    {
        var model = new ProjectModel { RootName = "R", SourcePath = "C:/r" };
        var html = GuidePage.Body(model);
        var linked = Regex.Matches(html, "href=\"([a-z0-9-]+\\.html)\"").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        var navHrefs = PageTemplate.NavSections.SelectMany(s => s.Items).Select(i => i.Href).ToHashSet(StringComparer.Ordinal);

        var missing = navHrefs.Except(linked).ToList();
        Assert.True(missing.Count == 0, "Guide page is missing links to: " + string.Join(", ", missing));
    }
}
