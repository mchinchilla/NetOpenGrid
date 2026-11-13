using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.Builders;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Json;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using NetOpenGrid.Infrastructure.Runtime;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

/// <summary>
/// A filter whose value does not parse to its column's type (an old link with a broken id) matches
/// nothing in every engine, without failing the request, and its chip is marked as invalid.
/// </summary>
public class InvalidFilterTests
{
    private enum Kind
    {
        Cash,
        Bank
    }

    private sealed record Register(Guid Id, string Name, int Units, DateOnly OpenedOn, Kind Kind, bool Active);

    private static readonly Register[] Registers =
    [
        new(Guid.Parse("01a1d207-0000-4000-8000-00000000000a"), "Main", 3, new DateOnly(2026, 1, 5), Kind.Cash, true),
        new(Guid.Parse("01a1d207-0000-4000-8000-00000000000b"), "Back", 7, new DateOnly(2026, 2, 9), Kind.Bank, false)
    ];

    private static GridOptionsBuilder<Register> Builder() => new GridOptionsBuilder<Register>()
        .WithId("registers")
        .AddColumn("name", r => r.Name)
        .AddColumn("units", r => r.Units)
        .AddColumn("openedOn", r => r.OpenedOn)
        .AddColumn("kind", r => r.Kind)
        .AddColumn("active", r => r.Active)
        .AddColumn("registerId", r => r.Id, c => c.Header("Register").Visible(false).FilterValueLabels((_, _) =>
            throw new InvalidOperationException("Never called for a value that does not parse.")));

    private static GridQueryNormalization Parse(params string[] filters) =>
        GridRequestParser.Parse(new GridRequestValues([new("filter", filters)]), Builder().Build());

    // ---- Parser --------------------------------------------------------------------------------

    [Theory]
    [InlineData("registerId:equals:basura")]
    [InlineData("registerId:not-equals:01a1d207")]
    [InlineData("units:equals:abc")]
    [InlineData("units:>=x")]
    [InlineData("units:equals:")]
    [InlineData("openedOn:lt:not-a-date")]
    [InlineData("kind:equals:crypto")]
    [InlineData("active:equals:maybe")]
    [InlineData("""units:in:["1","x"]""")]
    public void A_Value_That_Does_Not_Fit_The_Column_Is_Set_Aside(string filter)
    {
        var normalization = Parse(filter, "name:contains:a");

        // Set aside: no data source ever gets a value it cannot parse.
        Assert.Equal(["name"], normalization.Query.Filters.Select(f => f.Field));
        var invalid = Assert.Single(normalization.InvalidFilters);
        Assert.Equal(filter.Split(':')[0], invalid.Field);
        Assert.Contains(normalization.Warnings, w => w.Contains("not valid for field", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("registerId:equals:01A1D207-0000-4000-8000-00000000000A")]
    [InlineData("units:equals:3")]
    [InlineData("units:>=3")]
    [InlineData("openedOn:gte:2026-01-01")]
    [InlineData("kind:equals:cash")]
    [InlineData("active:equals:true")]
    [InlineData("name:equals:anything goes")]
    [InlineData("name:is-empty")]
    // An empty element of an "in" list stands for "no value", as the value checklist sends it.
    [InlineData("""units:in:["3",""]""")]
    public void Values_That_Fit_Stay_In_The_Query(string filter)
    {
        var normalization = Parse(filter);

        Assert.Single(normalization.Query.Filters);
        Assert.Empty(normalization.InvalidFilters);
    }

    [Fact]
    public void Untyped_Json_Columns_Take_Any_Value()
    {
        var options = new JsonGridOptionsBuilder().WithId("j").AddColumn("amount").Build();
        var normalization = GridRequestParser.Parse(new GridRequestValues([new("filter", ["amount:equals:abc"])]), options);

        Assert.Single(normalization.Query.Filters);
        Assert.Empty(normalization.InvalidFilters);
    }

    // ---- Endpoints -----------------------------------------------------------------------------

    [Fact]
    public async Task Rows_Come_Back_Empty_And_Name_The_Field()
    {
        var loads = new Loads();
        using var host = await BuildHostAsync(loads);
        var response = await host.GetTestClient().GetAsync(
            "/netgrid/registers/rows?filter=registerId:equals:basura&filter=units:equals:abc&filter=name:contains:a");

        // 200 with no rows, whatever the other filters match, and the data source is not asked.
        response.EnsureSuccessStatusCode();
        Assert.Equal("0", response.Headers.GetValues("X-Grid-Total").Single());
        Assert.DoesNotContain("data-field=", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, loads.Count);

        var fields = JsonSerializer.Deserialize<string[]>(response.Headers.GetValues(GridFilterLabels.InvalidFiltersHeaderName).Single());
        Assert.Equal(["registerId", "units"], fields!);
        Assert.False(response.Headers.Contains(GridFilterLabels.HeaderName));
    }

    [Fact]
    public async Task Grouped_Rows_Come_Back_Empty_Too()
    {
        var loads = new Loads();
        using var host = await BuildHostAsync(loads);
        var response = await host.GetTestClient().GetAsync("/netgrid/registers/rows?groupby=kind&filter=units:equals:abc");

        response.EnsureSuccessStatusCode();
        Assert.Equal("0", response.Headers.GetValues("X-Grid-Total").Single());
        Assert.Equal(0, loads.Count);
    }

    [Fact]
    public async Task Rows_Without_An_Invalid_Filter_Send_No_Header()
    {
        using var host = await BuildHostAsync(new Loads());
        var response = await host.GetTestClient().GetAsync("/netgrid/registers/rows?filter=units:equals:3");

        response.EnsureSuccessStatusCode();
        Assert.Equal("1", response.Headers.GetValues("X-Grid-Total").Single());
        Assert.False(response.Headers.Contains(GridFilterLabels.InvalidFiltersHeaderName));
    }

    [Fact]
    public async Task Shell_Renders_Empty_With_The_Invalid_Filter_In_Its_Initial_State()
    {
        var loads = new Loads();
        using var host = await BuildHostAsync(loads);
        var response = await host.GetTestClient().GetAsync("/netgrid/registers?filter=registerId:equals:basura");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var initial = Regex.Match(html, "__NETGRID__\\.initial\\[\"registers\"\\]=(\\{.*?\\});").Groups[1].Value;
        var state = JsonDocument.Parse(initial).RootElement;
        Assert.Equal(0, state.GetProperty("total").GetInt32());
        Assert.Equal(["registerId"], state.GetProperty("invalidFilters").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(0, loads.Count);

        Assert.DoesNotContain("invalidFilters", await host.GetTestClient().GetStringAsync("/netgrid/registers"));
    }

    [Fact]
    public async Task Export_Has_Only_Its_Header_Row()
    {
        var loads = new Loads();
        using var host = await BuildHostAsync(loads);

        var csv = await host.GetTestClient().GetStringAsync("/netgrid/registers/export?filter=units:equals:abc");

        Assert.Single(csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(0, loads.Count);
    }

    [Fact]
    public async Task Value_Lists_Are_Empty_Except_For_The_Invalid_Column_Itself()
    {
        using var host = await BuildHostAsync(new Loads());
        var client = host.GetTestClient();

        var other = JsonDocument.Parse(await client.GetStringAsync("/netgrid/registers/values?field=name&filter=units:equals:abc"));
        Assert.Equal(0, other.RootElement.GetProperty("values").GetArrayLength());

        // The column's own list ignores its own filter: this is where a valid value is picked.
        var own = JsonDocument.Parse(await client.GetStringAsync("/netgrid/registers/values?field=units&filter=units:equals:abc"));
        Assert.Equal(2, own.RootElement.GetProperty("values").GetArrayLength());
    }

    // ---- Markup and client contract ---------------------------------------------------------------

    [Fact]
    public async Task Chip_Is_Marked_And_Explains_Itself()
    {
        using var host = await BuildHostAsync(new Loads());
        var client = host.GetTestClient();
        var html = await client.GetStringAsync("/netgrid/registers");
        var js = await client.GetStringAsync("/_netgrid/netopengrid.js");

        Assert.Contains(
            "@click=\"clearFilter(entry.field)\" class=\"chip\" :class=\"{ 'chip-invalid': entry.invalid }\" :title=\"filterChipTitle(entry)\"",
            html);
        Assert.Contains("<span x-show=\"entry.invalid\" x-cloak aria-hidden=\"true\">&#9888;</span>", html);

        Assert.Contains("invalid: this.invalidFilters.includes(field)", js);
        Assert.Contains("return L(entry.invalid ? 'chips.invalid' : 'chips.remove');", Method(js, "filterChipTitle(entry)"));

        // First paint, then every /rows response replaces the list (no header: nothing is marked).
        Assert.Contains("this.invalidFilters = invalidFilters ?? [];", js);
        Assert.Contains(
            $"this.applyInvalidFilters(headers.get('{GridFilterLabels.InvalidFiltersHeaderName}'));",
            Method(js, "applyRowsMeta(headers)"));
        var apply = Method(js, "applyInvalidFilters(json)");
        Assert.Contains("let fields = [];", apply);
        Assert.Contains("this.invalidFilters = fields;", apply);
    }

    [Theory]
    [InlineData("grid")]
    [InlineData("cobalt")]
    [InlineData("midnight")]
    public async Task Every_Theme_Styles_The_Invalid_Chip(string theme)
    {
        using var host = await BuildHostAsync(new Loads());
        var css = await host.GetTestClient().GetStringAsync($"/_netgrid/css/netopengrid-{theme}.css");

        Assert.Contains(".chip-invalid", css);
    }

    [Fact]
    public void Invalid_Chip_Text_Exists_In_Both_Presets()
    {
        foreach (var culture in new[] { "en", "es" })
        {
            var locale = new NetOpenGridLocalizationOptions();
            locale.UseCulture(culture);
            Assert.NotEqual("chips.invalid", locale.Get("chips.invalid"));
            Assert.NotEqual("chips.remove", locale.Get("chips.remove"));   // was missing: the title read "chips.remove"
        }
    }

    private static string Method(string js, string signature)
    {
        var start = js.IndexOf($"\n      {signature} {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found in netopengrid.js");
        var end = js.IndexOf("\n      },", start, StringComparison.Ordinal);
        return js[start..end];
    }

    private sealed class Loads
    {
        public int Count;
    }

    private static async Task<IHost> BuildHostAsync(Loads loads)
    {
        var options = Builder().Build();
        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();

                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddNetOpenGrid()
                        .AddGrid(options, (_, opts) => new InMemoryGridDataSource<Register>(opts, _ =>
                        {
                            Interlocked.Increment(ref loads.Count);
                            return ValueTask.FromResult<IReadOnlyList<Register>>(Registers);
                        }));
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
