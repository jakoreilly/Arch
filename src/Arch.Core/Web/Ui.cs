using System.Text;

namespace Arch.Core.Web;

/// <summary>The shared component vocabulary, as C# helpers rather than hand-written markup
/// repeated per page. Both analysers and the hub were each spelling out the same
/// <c>.tiles</c>/<c>.tile</c> and legend markup inline, which is how the two sites drifted apart:
/// the code site opened almost every page with a KPI row and the SQL site mostly did not, and
/// nothing made that visible. Every method here emits only classes that already exist in
/// <c>assets/site.css</c> — this adds no CSS.</summary>
public static class Ui
{
    /// <summary>One KPI row: the "summary before detail" opener every content page is supposed to
    /// have. A tile whose value reads as nothing (0, "0", "—") is dimmed via <c>.tile-zero</c>
    /// rather than dropped, so the row's shape stays comparable between two runs of the same
    /// scan.</summary>
    /// <param name="tiles">Value/label pairs in display order. Values are pre-formatted by the
    /// caller (thousands separators, invariant culture) because only the caller knows the model;
    /// labels are plain text and are encoded here.</param>
    public static string Tiles(params (string Num, string Label)[] tiles)
    {
        if (tiles.Length == 0) { return ""; }
        var sb = new StringBuilder();
        sb.Append("<div class=\"tiles\">");
        foreach (var (num, label) in tiles) { sb.Append(Tile(num, label)); }
        sb.Append("</div>");
        return sb.ToString();
    }

    /// <summary>A single tile. Private because every row goes through <see cref="Tiles"/> —
    /// a caller assembling one tile at a time would be hand-rolling the row markup this type
    /// exists to stop being hand-rolled.</summary>
    private static string Tile(string num, string label)
    {
        var zero = IsZero(num) ? " tile-zero" : "";
        return $"<div class=\"tile{zero}\"><div class=\"num\">{Html.Encode(num)}</div>"
             + $"<div class=\"lbl\">{Html.Encode(label)}</div></div>";
    }

    /// <summary>The swatch legend used under a diagram, for surfaces whose colour coding was
    /// previously only described in a sentence of prose. <paramref name="items"/> pairs a CSS
    /// colour token name (e.g. "--cat-1", "--ok") with what that colour means. Token names only —
    /// a literal here would not re-theme, exactly as for any other colour in this codebase.</summary>
    /// <param name="summary">The &lt;details&gt; summary text.</param>
    /// <param name="open">Expand it by default, for pages where the legend is primary content.</param>
    public static string SwatchLegend(string summary, bool open, params (string Token, string Meaning)[] items)
    {
        if (items.Length == 0) { return ""; }
        var sb = new StringBuilder();
        sb.Append($"<details class=\"legend\"{(open ? " open" : "")}><summary>{Html.Encode(summary)}</summary>");
        sb.Append("<div class=\"legend-grid\">");
        foreach (var (token, meaning) in items)
        {
            sb.Append($"<span class=\"legend-item\"><span class=\"legend-swatch round\" "
                    + $"style=\"background:var({token});border-color:var({token})\"></span>{Html.Encode(meaning)}</span>");
        }
        sb.Append("</div></details>");
        return sb.ToString();
    }

    /// <summary>The standard search box + live match count above a filterable table.
    /// <paramref name="targetSelector"/> is the tbody the rows live in (site.js hides
    /// <c>.filterable</c> children of it and re-pages any paginated table over the survivors).</summary>
    public static string FilterBox(string targetSelector, string placeholder) =>
        $"<input class=\"filter-input\" type=\"search\" data-filter-target=\"{Html.Encode(targetSelector)}\" "
        + $"placeholder=\"{Html.Encode(placeholder)}\" autocomplete=\"off\" spellcheck=\"false\"> "
        + "<span class=\"filter-count\"></span>";

    /// <summary>"Nothing to show here", with the reason. Never rendered for a section that simply
    /// has no data yet without saying why — an empty panel and a panel that means "this needs a
    /// live connection" are different claims.</summary>
    public static string EmptyState(string glyph, string message) =>
        $"<div class=\"panel empty-state\"><div class=\"big\" aria-hidden=\"true\">{Html.Encode(glyph)}</div>"
        + $"<p>{message}</p></div>";

    /// <summary>Treats "0" and its formatted variants as nothing-to-see, matching what the
    /// hand-written <c>num == "0"</c> checks across the page classes already did.</summary>
    private static bool IsZero(string num) =>
        num is "0" or "0.0" or "0.00" or "—" or "-" or "";
}
