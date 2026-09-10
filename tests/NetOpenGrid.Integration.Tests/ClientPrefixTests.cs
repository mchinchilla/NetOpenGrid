using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using NetOpenGrid.Infrastructure.Runtime;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

/// <summary>
/// <see cref="GridHostFixture"/> is a plain DI container that never calls
/// <c>MapNetOpenGrid</c>, so these two tests only ever see the default
/// <see cref="NetOpenGridAssetOptions.RoutePrefix"/> value ("/netgrid") baked into the class
/// itself — a fully hardcoded renderer would satisfy them too. They're still useful (they pin
/// the exact serialized shape) but are not proof the prefix is actually wired end to end;
/// <see cref="CustomPrefixRoutingTests"/> below is what proves that.
/// </summary>
public class ClientPrefixTests : IClassFixture<GridHostFixture>
{
    private readonly GridHostFixture _fixture;
    public ClientPrefixTests(GridHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task State_Script_Publishes_The_Route_Prefix()
    {
        var runtime = _fixture.Services.GetRequiredKeyedService<IGridRuntime>("products");
        var html = await runtime.RenderFragmentAsync(GridRequestValues.Empty);

        Assert.Contains("\"prefix\":\"/netgrid\"", html.Replace(" ", ""), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selection_Export_Form_Uses_The_Route_Prefix()
    {
        var runtime = _fixture.Services.GetRequiredKeyedService<IGridRuntime>("products");
        var html = await runtime.RenderFragmentAsync(GridRequestValues.Empty);

        Assert.Contains(":action=\"'/netgrid' + '/' + id + '/export'\"", html, StringComparison.Ordinal);
    }
}

/// <summary>
/// Routes through a real <c>MapNetOpenGrid(o =&gt; o.Prefix = "/custom-grid")</c> host (cloned
/// from <see cref="EndpointAuthorizationTests.BuildHostAsync"/>) so a non-default prefix is
/// actually exercised: the data endpoints must be mapped under it, the default "/netgrid" must
/// NOT work, and the HTML the host renders must publish that same custom prefix back to the
/// client (both the state script and the selection-export form). A renderer that hardcodes
/// "/netgrid" anywhere would fail this test even though it passes every <see cref="ClientPrefixTests"/>
/// assertion, which only ever observe the default.
/// </summary>
public class CustomPrefixRoutingTests
{
    [Fact]
    public async Task Custom_Prefix_Routes_Requests_And_Is_The_One_Published_To_The_Client()
    {
        using var host = await BuildHostAsync("/custom-grid");
        var client = host.GetTestClient();

        var customRows = await client.GetAsync("/custom-grid/widgets/rows");
        Assert.Equal(HttpStatusCode.OK, customRows.StatusCode);

        var defaultRows = await client.GetAsync("/netgrid/widgets/rows");
        Assert.Equal(HttpStatusCode.NotFound, defaultRows.StatusCode);

        var shell = await client.GetAsync("/custom-grid/widgets");
        shell.EnsureSuccessStatusCode();
        var html = await shell.Content.ReadAsStringAsync();

        Assert.Contains("\"prefix\":\"/custom-grid\"", html.Replace(" ", ""), StringComparison.Ordinal);
        Assert.Contains(":action=\"'/custom-grid' + '/' + id + '/export'\"", html, StringComparison.Ordinal);
    }

    private static async Task<IHost> BuildHostAsync(string prefix)
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();

                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();

                    services.AddNetOpenGrid()
                        .AddGrid<Widget>("widgets", options => options
                            .EnableRowSelection(w => w.Name)
                            .AddColumn("name", w => w.Name, c => c.Searchable()),
                            (_, opts) => new InMemoryGridDataSource<Widget>(opts, WidgetData.All));
                });

                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapNetOpenGrid(o => o.Prefix = prefix);
                    });
                });
            });

        return await builder.StartAsync();
    }
}

file sealed record Widget(string Name);

file static class WidgetData
{
    public static readonly IReadOnlyList<Widget> All = [new("Alpha"), new("Beta")];
}
