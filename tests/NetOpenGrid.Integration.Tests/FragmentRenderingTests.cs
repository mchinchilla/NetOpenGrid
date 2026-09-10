using NetOpenGrid.Application.Binding;
using NetOpenGrid.Infrastructure.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class FragmentRenderingTests : IClassFixture<GridHostFixture>
{
    private readonly GridHostFixture _fixture;
    public FragmentRenderingTests(GridHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Fragment_Contains_No_Document_Scaffolding()
    {
        var runtime = _fixture.Services.GetRequiredKeyedService<IGridRuntime>("products");
        var html = await runtime.RenderFragmentAsync(GridRequestValues.Empty);

        Assert.DoesNotContain("<!doctype", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<head", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<body", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<main", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fragment_Contains_Grid_Root_And_State_Script()
    {
        var runtime = _fixture.Services.GetRequiredKeyedService<IGridRuntime>("products");
        var html = await runtime.RenderFragmentAsync(GridRequestValues.Empty);

        Assert.Contains("x-data=\"netgrid", html, StringComparison.Ordinal);
        Assert.Contains("__NETGRID__", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shell_Still_Renders_A_Complete_Document()
    {
        var runtime = _fixture.Services.GetRequiredKeyedService<IGridRuntime>("products");
        var html = await runtime.RenderShellAsync(GridRequestValues.Empty);

        Assert.StartsWith("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</html>", html, StringComparison.Ordinal);
        Assert.Contains("__NETGRID__", html, StringComparison.Ordinal);

        // Pin the verified-good composition order (doctype -> head -> body -> <main> ->
        // grid root -> state script -> </main> -> </body></html>). In particular this
        // catches the state script escaping past </main>, or <head>/<body> reordering.
        var headIndex = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        var bodyIndex = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        var mainIndex = html.IndexOf("<main", StringComparison.OrdinalIgnoreCase);
        var gridRootIndex = html.IndexOf("x-data=\"netgrid", StringComparison.Ordinal);
        var stateScriptIndex = html.IndexOf("__NETGRID__", StringComparison.Ordinal);
        var mainCloseIndex = html.IndexOf("</main>", StringComparison.OrdinalIgnoreCase);
        var bodyCloseIndex = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        var htmlCloseIndex = html.IndexOf("</html>", StringComparison.OrdinalIgnoreCase);

        Assert.True(
            headIndex >= 0 && bodyIndex >= 0 && mainIndex >= 0 && gridRootIndex >= 0 &&
            stateScriptIndex >= 0 && mainCloseIndex >= 0 && bodyCloseIndex >= 0 && htmlCloseIndex >= 0,
            "expected every composition marker to be present exactly once");

        Assert.True(headIndex < bodyIndex, "<head> should open before <body>");
        Assert.True(bodyIndex < mainIndex, "<body> should open before <main>");
        Assert.True(mainIndex < gridRootIndex, "the grid root should render after <main> opens");
        Assert.True(gridRootIndex < stateScriptIndex, "the state script should render after the grid root");
        Assert.True(stateScriptIndex < mainCloseIndex, "the state script must stay inside <main>, not escape past </main>");
        Assert.True(mainCloseIndex < bodyCloseIndex, "</main> should close before </body>");
        Assert.True(bodyCloseIndex < htmlCloseIndex, "</body> should close before </html>");
    }
}
