using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using NetOpenGrid.Infrastructure.Rendering;
using NetOpenGrid.Infrastructure.Runtime;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

/// <summary>
/// Everything the grid renders must load under a strict Content-Security-Policy (script-src 'self'; style-src 'self', no
/// nonce, no 'unsafe-inline', no 'unsafe-eval') with the Alpine CSP build: no inline style attributes or &lt;style&gt; blocks,
/// no executable inline script (the state is a JSON data block), no inline event handlers, and Alpine expressions the CSP
/// build can evaluate (one expression: no statement sequences, arrow functions, ?. ?? typeof, template literals or globals).
/// Every feature is on, so a new one that forgets this fails here, not in a host.
/// </summary>
public partial class StrictCspRenderingTests
{
    private sealed record Item(string Code, string Name, string City, int Units, bool Active);

    private static readonly Item[] Items =
    [
        new("A1", "Ana", "Lima", 3, true),
        new("B2", "Beto", "Lima", 5, false),
        new("C3", "Caro", "Quito", 8, true),
    ];

    public static TheoryData<string> Pages =>
    [
        "/netgrid/full",
        "/netgrid/full?embed=1",
        "/netgrid/full/rows",
        "/netgrid/full?groupby=city&expand=city%3DLima",
        "/netgrid/full/rows?groupby=city&expand=city%3DLima",
        "/netgrid/full?filter=code:equals:A1",
        "/netgrid/virtual",
        "/netgrid/virtual/rows?page=1&pageSize=20",
    ];

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task Page_Loads_Under_A_Strict_Csp(string url)
    {
        using var host = await BuildHostAsync();
        var response = await host.GetTestClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AssertStrict(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Fragment_And_Head_Tags_Load_Under_A_Strict_Csp()
    {
        using var host = await BuildHostAsync();
        var runtime = host.Services.GetRequiredKeyedService<IGridRuntime>("full");

        var fragment = await runtime.RenderFragmentAsync(GridRequestValues.Empty);
        AssertStrict(fragment);
        // htmx drops every <script> from swapped content under allowScriptTags: false (the strict-CSP setting): a fragment
        // swapped in (hx-boost, hx-get) must carry its state without one.
        Assert.DoesNotContain("<script", fragment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<template data-netgrid-state=\"full\">{", fragment, StringComparison.Ordinal);
        AssertStrict(GridAssetTags.Head(host.Services.GetRequiredService<NetOpenGridAssetOptions>(), "grid"));
    }

    [Fact]
    public void Client_Runtime_Builds_No_Inline_Styles()
    {
        var js = File.ReadAllText(Path.Combine(RepoRoot(), "src", "NetOpenGrid.Infrastructure", "Assets", "netopengrid.js"));

        Assert.DoesNotContain("style=\"", js);
        Assert.DoesNotContain("setAttribute('style'", js);
        Assert.DoesNotContain("x-html", js);
    }

    private static void AssertStrict(string html)
    {
        Assert.DoesNotMatch(StyleAttribute(), html);
        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(EventHandlerAttribute(), html);
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);

        foreach (Match script in ScriptTag().Matches(html))
        {
            Assert.True(script.Value.Contains(" src=\"", StringComparison.Ordinal) || script.Value.Contains("type=\"application/json\"", StringComparison.Ordinal),
                $"executable inline script: {script.Value}");
        }

        foreach (Match attribute in AlpineAttribute().Matches(html))
        {
            var expression = WebUtility.HtmlDecode(attribute.Groups["value"].Value);
            var code = QuotedString().Replace(expression, "''");
            Assert.False(StatementSequence().IsMatch(code), $"{attribute.Groups["name"].Value}=\"{expression}\": one expression only");
            Assert.DoesNotContain("=>", code);
            Assert.DoesNotContain("?.", code);
            Assert.DoesNotContain("??", code);
            Assert.DoesNotContain("`", code);
            Assert.False(Typeof().IsMatch(code), $"typeof in {expression}");
            Assert.False(Global().IsMatch(code), $"global access in {expression}");
        }
    }

    [GeneratedRegex(@"\sstyle\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();

    [GeneratedRegex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerAttribute();

    [GeneratedRegex("<script[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    [GeneratedRegex("\\s(?<name>x-[\\w:.-]+|@[\\w.:-]+|:[\\w-]+)=\"(?<value>[^\"]*)\"")]
    private static partial Regex AlpineAttribute();

    [GeneratedRegex("'(?:[^'\\\\]|\\\\.)*'")]
    private static partial Regex QuotedString();

    [GeneratedRegex(@";\s*\S")]
    private static partial Regex StatementSequence();

    [GeneratedRegex(@"\btypeof\b")]
    private static partial Regex Typeof();

    [GeneratedRegex(@"\b(window|document|globalThis|Math|JSON|Object|Array|Number|String|Date|console|localStorage|fetch)\b\s*[.(]")]
    private static partial Regex Global();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NetOpenGrid.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static async Task<IHost> BuildHostAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();

                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetOpenGrid(assets => assets.BrandColor = "#0d9488")
                        .AddGrid<Item>("full", options => options
                            .WithTitle("Items")
                            .WithMinHeight("24rem")
                            .WithBrandColor("#4f46e5")
                            .EnableRowSelection(i => i.Code)
                            .RowSelectable(i => i.Units > 2)
                            .AddBulkAction("close", a => a.Label("Close").Tone(BulkActionTone.Danger).Confirm().When(i => i.Active))
                            .WithAggregateRows(GridAggregateRows.Footer | GridAggregateRows.GroupHeader | GridAggregateRows.GroupFooter)
                            .AddView("Active", "filter=active:equals:true")
                            .AddColumn("code", i => i.Code, c => c.Pinned())
                            .AddColumn("name", i => i.Name, c => c.Searchable())
                            .AddColumn("city", i => i.City)
                            .AddColumn("units", i => i.Units, c => c.PinnedRight().Aggregate(GridAggregate.Sum | GridAggregate.Avg))
                            .AddColumn("active", i => i.Active, c => c.Visible(false))
                            .WithRowDetail(i => $"/items/{i.Code}/detail")
                            .WithRowActions(a => a
                                .Pinned()
                                .Link("Open", i => $"/items/{i.Code}")
                                .Event("archive", "Archive", RowActionStyle.Danger, visible: i => i.Active)),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, Items))
                        .AddGrid<Item>("virtual", options => options
                            .WithVirtualScroll(blockSize: 20, height: "60vh")
                            .AddColumn("code", i => i.Code)
                            .AddColumn("name", i => i.Name),
                            (_, opts) => new InMemoryGridDataSource<Item>(opts, Items));
                });

                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapNetOpenGrid());
                });
            });

        return await builder.StartAsync();
    }
}
