using System.Text;
using Arch.Code.Site;

namespace Arch.Code.Landscape.Pages;

public static class LandscapeDatabasesPage
{
    public static string Body(LandscapeModel model)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Shared Databases</h1>");
        sb.Append("""
<p class="lede">Databases discovered across the sites, matched by a normalized connection-string hash
(server + catalog). A database used by two or more sites is a real coupling point — a change to its
schema affects every site marked below.</p>
""");

        if (model.Databases.Count == 0)
        {
            sb.Append("<div class=\"panel empty-state\"><div class=\"big\">🗄</div><p>None of the discovered sites recorded "
                + "a database connection. Database links surface when a project's connection string resolves to the same "
                + "server + catalog across sites.</p></div>");
            return sb.ToString();
        }

        sb.Append("<table class=\"grid\"><thead><tr><th>Database</th><th>Server</th><th>Catalog</th>");
        foreach (var s in model.Sites) { sb.Append($"<th>{Html.Encode(s.Id)}</th>"); }
        sb.Append("</tr></thead><tbody>");

        foreach (var db in model.Databases)
        {
            var shared = db.SiteIds.Count >= 2 ? " <span class=\"badge\">shared</span>" : "";
            // Phase 8: a code-side database is normally only "matched by name" — the catalog it
            // names, not proof the two point at the same physical instance. Verified means a
            // SQL-only site's OWN Server+Catalog (arch sql / archsql / arch connect, the
            // authoritative inventory of what that database contains) confirmed it.
            var verified = db.Verified
                ? $" <span class=\"badge ok\" title=\"Confirmed against {Html.Encode(string.Join(", ", db.SqlSiteIds))}\">verified match</span>"
                : "";
            sb.Append($"<tr><td>{Html.Encode(db.Label)}{shared}{verified}</td><td>{Html.Encode(db.Server)}</td><td>{Html.Encode(db.Catalog)}</td>");
            foreach (var s in model.Sites)
            {
                sb.Append(db.SiteIds.Contains(s.Id) ? "<td style=\"text-align:center\">✓</td>" : "<td></td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }
}
