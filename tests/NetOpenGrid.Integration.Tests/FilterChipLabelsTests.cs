using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetOpenGrid.Application.Binding;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Json;
using NetOpenGrid.Application.Options;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;
using NetOpenGrid.Infrastructure.Runtime;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

/// <summary>
/// Filter chips name hidden filter-only columns by their header and show the labels the host
/// resolves for raw filter values (an id as its number).
/// </summary>
public class FilterChipLabelsTests
{
    private sealed record Movement(string Id, Guid SessionId, string Naturaleza, string Status, decimal Amount, string Note);

    private static readonly Guid SessionA = Guid.Parse("01a1d207-0000-4000-8000-00000000000a");
    private static readonly Guid SessionB = Guid.Parse("01a1d207-0000-4000-8000-00000000000b");
    private static readonly Guid Missing = Guid.Parse("01a1d207-0000-4000-8000-0000000000ff");

    private static readonly Movement[] Movements =
    [
        new("M1", SessionA, "Egreso", "open", 10m, "a"),
        new("M2", SessionB, "Ingreso", "closed", 5m, "b")
    ];

    /// <summary>What the fake host observed, and how its resolvers should behave.</summary>
    private sealed class Probe
    {
        public List<(string Field, string[] Values, string? RequestPath, int LoadsBefore)> Calls { get; } = [];
        public List<string> Warnings { get; } = [];
        public int Loads;
        public int Running;
        public bool Overlapped;
        public Func<Exception>? SessionFailure;
        public string SessionLabelPrefix = "SES-2026-";

        public async Task<T> ExclusiveAsync<T>(Func<T> work)
        {
            if (Interlocked.Increment(ref Running) > 1)
            {
                Overlapped = true;
            }

            try
            {
                await Task.Delay(15);
                return work();
            }
            finally
            {
                Interlocked.Decrement(ref Running);
            }
        }
    }

    /// <summary>Scoped: one per request, stamped by a middleware with that request's path.</summary>
    private sealed class RequestMarker
    {
        public string? Path { get; set; }
    }

    private static string In(params Guid[] ids) =>
        Uri.EscapeDataString("sessionId:in:" + JsonSerializer.Serialize(ids.Select(id => id.ToString())));

    private static Dictionary<string, Dictionary<string, string>> Labels(HttpResponseMessage response) =>
        response.Headers.TryGetValues(GridFilterLabels.HeaderName, out var values)
            ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(values.Single())!
            : [];

    // ---- Render --------------------------------------------------------------------------------

    [Fact]
    public async Task Filter_Headers_List_Hidden_Filterable_Columns_Only()
    {
        using var host = await BuildHostAsync(new Probe());
        var html = await host.GetTestClient().GetStringAsync("/netgrid/movs");

        // Localized like the visible headers ("Sesión" goes through TextLocalizer), HTML-safe JSON.
        var headers = Regex.Match(html, "__NETGRID__\\.filterHeaders\\[\"movs\"\\]=(\\{.*?\\});").Groups[1].Value;
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(headers)!;
        Assert.Equal(new Dictionary<string, string> { ["sessionId"] = "[Sesión]", ["naturaleza"] = "[Naturaleza]" }, map);
        Assert.DoesNotContain("note", map.Keys);   // hidden but not filterable
    }

    [Fact]
    public async Task Columns_Blob_Still_Lists_The_Visible_Columns_Only()
    {
        using var host = await BuildHostAsync(new Probe());
        var html = await host.GetTestClient().GetStringAsync("/netgrid/movs");

        var columns = Regex.Match(html, "__NETGRID__\\.columns\\[\"movs\"\\]=(\\[.*?\\]);").Groups[1].Value;
        var fields = JsonDocument.Parse(columns).RootElement.EnumerateArray().Select(c => c.GetProperty("field").GetString());
        Assert.Equal(["id", "status", "amount"], fields);

        // And no <th> for them either: nothing to drag, pin or pick in the column chooser.
        Assert.DoesNotContain("data-field=\"sessionId\"", html);
    }

    [Fact]
    public async Task A_Grid_Without_Hidden_Filter_Columns_Publishes_No_Filter_Headers()
    {
        using var host = await BuildHostAsync(new Probe());
        var html = await host.GetTestClient().GetStringAsync("/netgrid/plain");

        Assert.DoesNotContain("filterHeaders", html);
        Assert.DoesNotContain("filterLabels", html);
    }

    [Fact]
    public async Task Initial_State_Carries_The_Labels_Of_The_Filters_In_The_Link()
    {
        var probe = new Probe();
        using var host = await BuildHostAsync(probe);
        var html = await host.GetTestClient().GetStringAsync($"/netgrid/movs?filter=sessionId:equals:{SessionA}");

        var initial = Regex.Match(html, "__NETGRID__\\.initial\\[\"movs\"\\]=(\\{.*?\\});__NETGRID__\\.filterHeaders").Groups[1].Value;
        var labels = JsonDocument.Parse(initial).RootElement.GetProperty("filterLabels");
        Assert.Equal("SES-2026-00000a", labels.GetProperty("sessionId").GetProperty(SessionA.ToString()).GetString());

        // Without a filter the state is what it always was.
        var plain = await host.GetTestClient().GetStringAsync("/netgrid/movs");
        Assert.DoesNotContain("filterLabels", plain);
        Assert.Single(probe.Calls);
    }

    // ---- Runtime -------------------------------------------------------------------------------

    [Fact]
    public async Task Rows_Send_The_Labels_In_An_Ascii_Header()
    {
        var probe = new Probe { SessionLabelPrefix = "Sesión ñ <b>\"" };
        using var host = await BuildHostAsync(probe);
        var response = await host.GetTestClient().GetAsync($"/netgrid/movs/rows?filter=sessionId:equals:{SessionA}");

        response.EnsureSuccessStatusCode();
        var raw = response.Headers.GetValues(GridFilterLabels.HeaderName).Single();
        Assert.All(raw, c => Assert.InRange(c, ' ', '~'));
        Assert.Equal("Sesión ñ <b>\"00000a", Labels(response)["sessionId"][SessionA.ToString()]);

        // The body is still nothing but rows.
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public async Task Resolver_Runs_Once_Per_Column_With_Its_Distinct_Values()
    {
        var probe = new Probe();
        using var host = await BuildHostAsync(probe);
        var filters = $"filter={In(SessionA, SessionB, SessionA)}&filter=sessionId:not-equals:{Missing}" +
                      "&filter=status:equals:open&filter=naturaleza:contains:Egr";
        var response = await host.GetTestClient().GetAsync($"/netgrid/movs/rows?{filters}");

        response.EnsureSuccessStatusCode();
        Assert.Equal(["sessionId", "status"], probe.Calls.Select(c => c.Field).Order());

        var session = probe.Calls.Single(c => c.Field == "sessionId");
        Assert.Equal([SessionA.ToString(), SessionB.ToString(), Missing.ToString()], session.Values);

        // A record that no longer exists gets no label: the chip keeps its raw id.
        var labels = Labels(response);
        Assert.Equal(["SES-2026-00000a", "SES-2026-00000b"], labels["sessionId"].Values.Order());
        Assert.DoesNotContain(Missing.ToString(), labels["sessionId"].Keys);
        Assert.Equal("Abierta", labels["status"]["open"]);
    }

    [Fact]
    public async Task Resolver_Is_Not_Called_Without_A_Value_Filter_On_Its_Column()
    {
        var probe = new Probe();
        using var host = await BuildHostAsync(probe);
        var client = host.GetTestClient();

        (await client.GetAsync("/netgrid/movs/rows")).EnsureSuccessStatusCode();
        (await client.GetAsync("/netgrid/movs/rows?sort=id&q=M&filter=naturaleza:equals:Egreso")).EnsureSuccessStatusCode();
        // isEmpty and text operators carry no value to label.
        var response = await client.GetAsync("/netgrid/movs/rows?filter=status:is-not-empty&filter=status:contains:op");
        response.EnsureSuccessStatusCode();

        Assert.Empty(probe.Calls);
        Assert.False(response.Headers.Contains(GridFilterLabels.HeaderName));
    }

    [Fact]
    public async Task Resolver_Gets_The_Services_Of_The_Request()
    {
        var probe = new Probe();
        using var host = await BuildHostAsync(probe);
        var client = host.GetTestClient();

        await client.GetAsync($"/netgrid/movs/rows?filter=sessionId:equals:{SessionA}");
        await client.GetAsync($"/netgrid/movs?filter=sessionId:equals:{SessionA}");

        Assert.Equal(["/netgrid/movs/rows", "/netgrid/movs"], probe.Calls.Select(c => c.RequestPath));
    }

    [Fact]
    public async Task A_Failing_Resolver_Falls_Back_To_Raw_Values_And_Logs_A_Warning()
    {
        var probe = new Probe { SessionFailure = () => new InvalidOperationException("db down") };
        using var host = await BuildHostAsync(probe);
        var client = host.GetTestClient();
        var filters = $"filter=sessionId:equals:{SessionA}&filter=status:equals:open";

        var rows = await client.GetAsync($"/netgrid/movs/rows?{filters}");
        rows.EnsureSuccessStatusCode();
        Assert.Contains("M1", await rows.Content.ReadAsStringAsync());

        // The other column's labels still arrive.
        var labels = Labels(rows);
        Assert.DoesNotContain("sessionId", labels.Keys);
        Assert.Equal("Abierta", labels["status"]["open"]);

        var shell = await client.GetAsync($"/netgrid/movs?{filters}");
        shell.EnsureSuccessStatusCode();

        Assert.Equal(2, probe.Warnings.Count);
        Assert.All(probe.Warnings, w => Assert.Contains("'sessionId'", w));
    }

    [Fact]
    public async Task A_Resolver_Cancelled_On_Its_Own_Falls_Back_To_Raw_Values()
    {
        // Its own timeout, not the request's: the rows are still served.
        var probe = new Probe { SessionFailure = () => new OperationCanceledException("resolver timeout") };
        using var host = await BuildHostAsync(probe);

        var response = await host.GetTestClient().GetAsync($"/netgrid/movs/rows?filter=sessionId:equals:{SessionA}");

        response.EnsureSuccessStatusCode();
        Assert.False(response.Headers.Contains(GridFilterLabels.HeaderName));
        Assert.Single(probe.Warnings);
    }

    [Fact]
    public async Task An_Aborted_Request_Cancels_The_Resolver_Without_A_Warning()
    {
        var probe = new Probe();
        using var host = await BuildHostAsync(probe);
        var runtime = host.Services.GetRequiredKeyedService<IGridRuntime>("movs");
        var query = GridQuery.Empty with { Filters = [new FilterDescriptor("sessionId", FilterOperator.Equals, SessionA.ToString())] };

        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await runtime.ResolveFilterLabelsAsync(query, host.Services, aborted.Token));
        Assert.Empty(probe.Warnings);
    }

    [Theory]
    [InlineData("/netgrid/movs/rows")]
    [InlineData("/netgrid/movs")]
    [InlineData("/netgrid/movs/rows?groupby=status")]
    public async Task Resolvers_Never_Overlap_With_The_Data_Source(string path)
    {
        // The data source and both resolvers each hold the "connection" for a while; anything
        // started before the previous one finished trips Overlapped.
        var probe = new Probe();
        using var host = await BuildHostAsync(probe);
        var separator = path.Contains('?') ? '&' : '?';

        var response = await host.GetTestClient().GetAsync(
            $"{path}{separator}filter={In(SessionA, SessionB)}&filter=status:equals:open");

        response.EnsureSuccessStatusCode();
        Assert.Equal(2, probe.Calls.Count);
        Assert.False(probe.Overlapped);

        // Rows and totals were both read before the first resolver started.
        Assert.All(probe.Calls, call => Assert.Equal(probe.Loads, call.LoadsBefore));
        Assert.True(probe.Loads >= 2);
    }

    [Fact]
    public async Task Virtual_Scroll_Blocks_Skip_The_Resolvers()
    {
        var probe = new Probe();
        using var host = await BuildHostAsync(probe);

        var response = await host.GetTestClient().GetAsync($"/netgrid/movs/rows?labels=0&filter=sessionId:equals:{SessionA}");

        response.EnsureSuccessStatusCode();
        Assert.Empty(probe.Calls);
    }

    [Fact]
    public async Task Labels_Too_Long_For_A_Header_Are_Left_Out_With_A_Warning()
    {
        var probe = new Probe { SessionLabelPrefix = new string('x', GridFilterLabels.MaxHeaderLength) };
        using var host = await BuildHostAsync(probe);

        var response = await host.GetTestClient().GetAsync($"/netgrid/movs/rows?filter=sessionId:equals:{SessionA}");

        response.EnsureSuccessStatusCode();
        Assert.False(response.Headers.Contains(GridFilterLabels.HeaderName));
        Assert.Contains(probe.Warnings, w => w.Contains("response header", StringComparison.Ordinal));
    }

    // ---- Public API for the host's own exports ---------------------------------------------------

    [Fact]
    public async Task Host_Can_Resolve_The_Same_Labels_For_Its_Own_Export()
    {
        var probe = new Probe();
        using var host = await BuildHostAsync(probe);
        var runtime = host.Services.GetRequiredKeyedService<IGridRuntime>("movs");

        // A Guid matches whatever its case in the link.
        var upper = SessionA.ToString().ToUpperInvariant();
        var query = GridQuery.Empty with { Filters = [new FilterDescriptor("sessionId", FilterOperator.Equals, upper)] };
        var fromQuery = await runtime.ResolveFilterLabelsAsync(query, host.Services);
        Assert.Equal("SES-2026-00000a", fromQuery["sessionId"][upper]);

        var values = new GridRequestValues([new("filter", [$"sessionId:equals:{SessionB}", "bogus:equals:1"])]).WithServices(host.Services);
        var fromValues = await runtime.ResolveFilterLabelsAsync(values);
        Assert.Equal("SES-2026-00000b", fromValues["sessionId"][SessionB.ToString()]);

        Assert.Empty(await runtime.ResolveFilterLabelsAsync(GridQuery.Empty, host.Services));
    }

    [Fact]
    public void Builders_Carry_The_Resolver_Only_On_Filterable_Columns()
    {
        GridFilterValueLabelResolver resolver = (_, _) => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        var typed = new Application.Builders.GridOptionsBuilder<Movement>()
            .WithId("t")
            .AddColumn("id", m => m.Id, c => c.FilterValueLabels(resolver))
            .AddColumn("note", m => m.Note, c => c.Filterable(false).FilterValueLabels(resolver))
            .Build();
        Assert.Same(resolver, typed.Columns[0].FilterValueLabels);
        Assert.Null(typed.Columns[1].FilterValueLabels);

        var json = new JsonGridOptionsBuilder()
            .WithId("j")
            .AddColumn("id", c => c.Visible(false).FilterValueLabels(resolver))
            .AddColumn("name")
            .Build();
        Assert.Same(resolver, json.Columns[0].FilterValueLabels);
    }

    // ---- Client contract (netopengrid.js) --------------------------------------------------------

    [Fact]
    public async Task Client_Names_A_Field_By_Its_Visible_Header_Then_Its_Filter_Header()
    {
        using var host = await BuildHostAsync(new Probe());
        var client = host.GetTestClient();
        var js = await client.GetStringAsync("/_netgrid/netopengrid.js");

        Assert.Contains(
            "return this.columnInfo(field)?.header || window.__NETGRID__?.filterHeaders?.[id]?.[field] || field;",
            Method(js, "columnHeader(field)"));

        // columnInfo keeps reading the visible columns only: it decides drop targets and pins.
        Assert.DoesNotContain("filterHeaders", Method(js, "columnInfo(field)"));

        // Every text that names a field goes through the same rule.
        Assert.DoesNotContain("this.columnInfo(field)?.header || field", js);
        Assert.Contains("L(key, { field: this.columnHeader(field) })", Method(js, "pinLabel(field)"));
        Assert.Contains("this.columnHeader(field)", Method(js, "removeGroupLabel(field)"));
        var shell = await client.GetStringAsync("/netgrid/movs");
        Assert.DoesNotContain("columnInfo(g)?.header", shell);
        Assert.Contains("x-text=\"columnHeader(g)\"", shell);
    }

    [Fact]
    public async Task Client_Chip_Shows_The_Label_Of_Each_Value_Or_The_Raw_Value()
    {
        using var host = await BuildHostAsync(new Probe());
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        var label = Method(js, "filterLabel(f, field)");
        Assert.Contains("const labels = (field && this.filterValueLabels[field]) || {};", label);
        Assert.Contains("const text = (value) => labels[value] ?? value;", label);
        Assert.Contains("parseInValues(f.value).map(text)", label);
        Assert.Contains("${text(f.value)}", label);
        Assert.Contains("${this.columnHeader(field)} ${this.filterLabel(f, field)}", js);
    }

    [Fact]
    public async Task Client_Takes_The_Labels_From_The_Initial_State_And_From_Every_Rows_Response()
    {
        using var host = await BuildHostAsync(new Probe());
        var js = await host.GetTestClient().GetStringAsync("/_netgrid/netopengrid.js");

        // First paint.
        Assert.Contains("const { filterLabels, invalidFilters, ...meta } = initial;", js);
        Assert.Contains("this.filterValueLabels = filterLabels ?? {};", js);

        // Every /rows response: fetchRows hands its headers to applyRowsMeta, and a saved view or a
        // link ends in the same fetch (applyView → refresh).
        Assert.Contains($"this.applyFilterLabels(headers.get('{GridFilterLabels.HeaderName}'));", Method(js, "applyRowsMeta(headers)"));
        Assert.Contains("state.applyRowsMeta(response.headers);", js);
        Assert.Contains("await this.refresh();", Method(js, "async applyView(query)"));
        Assert.Contains("JSON.parse(json)", Method(js, "applyFilterLabels(json)"));

        // Blocks of a virtual scroll repeat the query: they ask the server to skip the resolvers.
        Assert.Contains("params.set('labels', '0');", Method(js, "async loadVirtualBlock(block)"));
    }

    private static string Method(string js, string signature)
    {
        var start = js.IndexOf($"\n      {signature} {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} not found in netopengrid.js");
        var end = js.IndexOf("\n      },", start, StringComparison.Ordinal);
        return js[start..end];
    }

    // ---- Host ----------------------------------------------------------------------------------

    private sealed class CaptureLoggerProvider(Probe probe) : ILoggerProvider, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                lock (probe.Warnings)
                {
                    probe.Warnings.Add(formatter(state, exception));
                }
            }
        }
    }

    private static async Task<IHost> BuildHostAsync(Probe probe)
    {
        async Task<IReadOnlyDictionary<string, string>> Record(GridFilterValueLabelContext ctx, Func<Dictionary<string, string>> resolve)
        {
            lock (probe.Calls)
            {
                probe.Calls.Add((ctx.Field, [.. ctx.Values], ctx.Services.GetService<RequestMarker>()?.Path, probe.Loads));
            }

            return await probe.ExclusiveAsync(resolve);
        }

        var builder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();

                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddScoped<RequestMarker>();
                    services.AddLogging(logging => logging.ClearProviders().AddProvider(new CaptureLoggerProvider(probe)));
                    services.AddNetOpenGrid(_ => { }, locale => locale.TextLocalizer = text => $"[{text}]")
                        .AddGrid<Movement>("movs", options => options
                            .WithAggregateRows(GridAggregateRows.Footer)
                            .AddColumn("id", m => m.Id)
                            .AddColumn("status", m => m.Status, c => c.FilterValueLabels((ctx, _) =>
                                Record(ctx, () => new Dictionary<string, string> { ["open"] = "Abierta", ["closed"] = "Cerrada" })))
                            .AddColumn("amount", m => m.Amount, c => c.Aggregate(GridAggregate.Sum))
                            .AddColumn("sessionId", m => m.SessionId, c => c
                                .Header("Sesión")
                                .Visible(false)
                                .FilterValueLabels((ctx, ct) =>
                                {
                                    ct.ThrowIfCancellationRequested();
                                    return Record(ctx, () => probe.SessionFailure is { } failure
                                        ? throw failure()
                                        : Movements.ToDictionary(
                                            m => m.SessionId.ToString(),
                                            m => probe.SessionLabelPrefix + m.SessionId.ToString()[^6..]));
                                }))
                            .AddColumn("naturaleza", m => m.Naturaleza, c => c.Header("Naturaleza").Visible(false))
                            .AddColumn("note", m => m.Note, c => c.Visible(false).Filterable(false)),
                            (_, opts) => new InMemoryGridDataSource<Movement>(opts, async _ =>
                            {
                                Interlocked.Increment(ref probe.Loads);
                                return await probe.ExclusiveAsync<IReadOnlyList<Movement>>(() => Movements);
                            }))
                        .AddGrid<Movement>("plain", options => options
                            .AddColumn("id", m => m.Id)
                            .AddColumn("note", m => m.Note, c => c.Visible(false).Filterable(false)),
                            (_, opts) => new InMemoryGridDataSource<Movement>(opts, Movements));
                });

                webHost.Configure(app =>
                {
                    app.Use((http, next) =>
                    {
                        http.RequestServices.GetRequiredService<RequestMarker>().Path = http.Request.Path;
                        return next(http);
                    });
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapNetOpenGrid());
                });
            });

        return await builder.StartAsync();
    }
}
