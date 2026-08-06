using System.Text;

namespace Arch.Core.Web;

/// <summary>HTML primitives shared by every page generator. Both products carried a
/// character-identical copy of these before the core was extracted.</summary>
public static class Html
{
    public static string Encode(string s) => System.Net.WebUtility.HtmlEncode(s);

    public static string Crumbs(params (string? Href, string Text)[] parts)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) { sb.Append(" <span class=\"crumb-sep\">/</span> "); }
            var (href, text) = parts[i];
            sb.Append(href is null
                ? $"<span class=\"crumb-here\">{Html.Encode(text)}</span>"
                : $"<a href=\"{href}\">{Html.Encode(text)}</a>");
        }
        return sb.ToString();
    }
}
