using NetOpenGrid.Infrastructure.Rendering;
using NetOpenGrid.Infrastructure.Runtime;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class GridAssetTagsTests
{
    [Fact]
    public void Head_HtmlEncodes_TheThemeName_InTheStylesheetHref()
    {
        // A theme name containing a double quote would close the href attribute early
        // and inject markup into the host page's <head> if left unencoded.
        var maliciousTheme = "grid\" onerror=\"alert(1)";

        var head = GridAssetTags.Head(new NetOpenGridAssetOptions(), maliciousTheme);

        Assert.DoesNotContain("onerror=\"alert(1)\"", head, StringComparison.Ordinal);
        Assert.Contains("&quot;", head, StringComparison.Ordinal);
    }

    [Fact]
    public void Head_RendersTheStylesheetLinkAndDeferredScript_ForAnOrdinaryTheme()
    {
        var head = GridAssetTags.Head(new NetOpenGridAssetOptions(), "grid");

        Assert.Contains("<link rel=\"stylesheet\" href=\"/_netgrid/css/netopengrid-grid.css", head, StringComparison.Ordinal);
        Assert.Contains("<script src=\"/_netgrid/netopengrid.js?v=", head, StringComparison.Ordinal);
    }
}
