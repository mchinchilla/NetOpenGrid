using System.Text.Encodings.Web;
using NetOpenGrid.Application.Options;

namespace NetOpenGrid.Infrastructure.Rendering;

/// <summary>
/// What goes right after the theme <c>&lt;link&gt;</c>: the brand colour and the extra
/// stylesheets. Shared by the grid document (<see cref="GridHtmlRenderer{TItem}"/>) and the host
/// page head (<see cref="GridAssetTags.Head"/>) so both write the same tags in the same order:
/// <list type="number">
///   <item><c>&lt;style&gt;:root{--ng-brand:…}&lt;/style&gt;</c>, when there is a brand colour;</item>
///   <item>the global stylesheets, then the grid's own, each as <c>&lt;link rel="stylesheet"&gt;</c>.</item>
/// </list>
/// The stylesheets go last so an override in them beats both the theme and the brand rule.
/// </summary>
internal static class GridThemeOverrides
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;

    public static void Write(
        TextWriter w,
        string? brandColor,
        IEnumerable<string> globalStylesheets,
        IEnumerable<string>? gridStylesheets = null)
    {
        // Options and builders already validate; this re-check keeps a hand-built GridOptions
        // (its init setters do not validate) from writing into a <style> element, where HTML
        // encoding would not help.
        if (GridCssColor.IsValid(brandColor))
        {
            w.Write("<style>:root{--ng-brand:");
            w.Write(brandColor!.Trim());
            w.Write("}</style>");
        }

        WriteLinks(w, globalStylesheets);
        if (gridStylesheets is not null)
        {
            WriteLinks(w, gridStylesheets);
        }
    }

    private static void WriteLinks(TextWriter w, IEnumerable<string> stylesheets)
    {
        foreach (var stylesheet in stylesheets)
        {
            if (GridUrl.Stylesheet(stylesheet) is not { } href)
            {
                continue;
            }

            w.Write("<link rel=\"stylesheet\" href=\"");
            Encoder.Encode(w, href);
            w.Write("\">");
        }
    }
}
