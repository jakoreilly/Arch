using System.Text;

namespace Arch.Sql.Site.Pages;

public static class ConfigPage
{
    public static string Body(SiteContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Config &amp; Secrets</h1>");
        sb.Append("""<p class="lede">Files that embed a credential in DDL — the fact and location only, never the secret value.</p>""");

        var withCred = ctx.Model.Files.Where(f => f.HasCredential).ToList();
        if (withCred.Count == 0)
        {
            sb.Append("""<div class="panel empty-state"><div class="big">✓</div><p>No embedded credentials were found in this scan.</p></div>""");
            return sb.ToString();
        }

        sb.Append(Ui.Tiles(
            (withCred.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Files with a credential"),
            (ctx.Model.Files.Count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), "Files scanned")));

        sb.Append("""<table class="grid sortable"><thead><tr><th>File</th><th>Finding</th></tr></thead><tbody>""");
        foreach (var f in withCred)
        {
            sb.Append($"""<tr><td><a href="files/{f.Slug}.html">{Html.Encode(f.RelPath)}</a></td><td><span class="badge warn">Credential in DDL</span></td></tr>""");
        }
        sb.Append("</tbody></table>");
        sb.Append("""<p class="note">Only the fact and the location are reported. Arch never copies a credential value into any generated file — open the listed file in your own editor to see it.</p>""");
        return sb.ToString();
    }
}
