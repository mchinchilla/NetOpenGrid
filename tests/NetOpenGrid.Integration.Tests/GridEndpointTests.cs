using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using NetOpenGrid.Host.Samples;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public sealed class HostFactory : WebApplicationFactory<Program>;

public class GridEndpointTests : IClassFixture<HostFactory>
{
    private readonly HostFactory _factory;

    public GridEndpointTests(HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Rows_ReturnsSortedPage_WithMetaHeaders()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/netgrid/employees/rows?page=1&pageSize=5&sort=salary:desc");

        response.EnsureSuccessStatusCode();
        Assert.Equal("5", response.Headers.GetValues("X-Grid-Page-Size").Single());
        Assert.Equal("1", response.Headers.GetValues("X-Grid-Page").Single());
        Assert.Equal(EmployeeData.All.Count.ToString(), response.Headers.GetValues("X-Grid-Total").Single());

        var html = await response.Content.ReadAsStringAsync();
        var expectedTop = EmployeeData.All.OrderByDescending(e => e.Salary).Take(5).Select(e => e.FullName);

        foreach (var name in expectedTop)
        {
            Assert.Contains(name, html);
        }

        Assert.Equal(5, Regex.Matches(html, "<tr").Count);
    }

    [Fact]
    public async Task Rows_AppliesEnumFilter_CaseInsensitive()
    {
        var client = _factory.CreateClient();
        var expected = EmployeeData.All.Count(e => e.Department == Department.Design);

        var response = await client.GetAsync("/netgrid/employees/rows?filter=department:equals:design");
        response.EnsureSuccessStatusCode();

        Assert.Equal(expected.ToString(), response.Headers.GetValues("X-Grid-Total").Single());
    }

    [Fact]
    public async Task Rows_GlobalSearch_FiltersByEmailAndName()
    {
        var client = _factory.CreateClient();
        var probe = EmployeeData.All[7];
        var term = probe.FullName.Split(' ')[0];

        var expected = EmployeeData.All.Count(e =>
            e.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            e.Email.Contains(term, StringComparison.OrdinalIgnoreCase));

        var response = await client.GetAsync($"/netgrid/employees/rows?q={Uri.EscapeDataString(term)}");
        response.EnsureSuccessStatusCode();

        Assert.Equal(expected.ToString(), response.Headers.GetValues("X-Grid-Total").Single());
    }

    [Fact]
    public async Task Rows_IgnoresUnknownFields_Safely()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/netgrid/employees/rows?sort=;drop--&filter=nope:equals:x&q=");

        response.EnsureSuccessStatusCode();
        Assert.Equal(EmployeeData.All.Count.ToString(), response.Headers.GetValues("X-Grid-Total").Single());
    }

    [Fact]
    public async Task Shell_RendersDocument_WithInitialRowsAndState()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/netgrid/employees");

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains("<!doctype html>", html);
        Assert.Contains("data-netgrid-card style=\"min-height:64rem\"", html);
        Assert.Contains("/_netgrid/vendor/htmx.min.js?v=", html);
        Assert.Contains("/_netgrid/vendor/alpine.min.js?v=", html);
        Assert.Contains("/_netgrid/netopengrid.js?v=", html);
        Assert.Contains("/css/netopengrid-grid.css", html);
        Assert.Contains("__NETGRID__.initial[\"employees\"]", html);
        Assert.Contains("];__NETGRID__.locale=", html);   // state script: columns array closed before locale blob
        Assert.StartsWith("<tbody id=\"employees-body\">", html[(html.IndexOf("<tbody", StringComparison.Ordinal))..]);
    }

    [Fact]
    public async Task Orders_JsonGrid_FiltersByStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/netgrid/orders/rows?filter=status:equals:delivered");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal("10", response.Headers.GetValues("X-Grid-Total").Single());
        Assert.DoesNotContain("badge-pending", html);
        Assert.Contains("badge-delivered", html);
    }

    [Fact]
    public async Task ValuesEndpoint_ReturnsCounts_ExcludingOwnFilter()
    {
        var client = _factory.CreateClient();

        var context = $"filter={Uri.EscapeDataString("""department:in:["Design"]""")}";
        var response = await client.GetAsync($"/netgrid/employees/values?field=department&{context}");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var values = doc.RootElement.GetProperty("values");

        Assert.Equal(6, doc.RootElement.GetProperty("totalDistinct").GetInt32());
        Assert.Equal(6, values.GetArrayLength());

        var design = values.EnumerateArray().Single(v => v.GetProperty("value").GetString() == "Design");
        var expected = EmployeeData.All.Count(e => e.Department == Department.Design);
        Assert.Equal(expected, design.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Rows_InFilter_MatchesAnyValue()
    {
        var client = _factory.CreateClient();
        var expected = EmployeeData.All.Count(e => e.Department is Department.Design or Department.Finance);

        var filter = $"filter={Uri.EscapeDataString("""department:in:["Design","Finance"]""")}";
        var response = await client.GetAsync($"/netgrid/employees/rows?{filter}&pageSize=5");

        response.EnsureSuccessStatusCode();
        Assert.Equal(expected.ToString(), response.Headers.GetValues("X-Grid-Total").Single());
    }

    [Fact]
    public async Task Shell_MarksListModeColumns()
    {
        var client = _factory.CreateClient();
        var html = await (await client.GetAsync("/netgrid/employees")).Content.ReadAsStringAsync();

        Assert.Contains("\"mode\":\"list\"", html);   // fullName (text) · department (enum) · active (bool)
        Assert.Contains("\"mode\":\"op\"", html);     // salary / hiredOn / score
    }

    [Fact]
    public async Task Rows_RespectColsParam_Order()
    {
        var client = _factory.CreateClient();
        var first = EmployeeData.All[0];

        var response = await client.GetAsync($"/netgrid/employees/rows?sort=id&pageSize=1&cols={Uri.EscapeDataString("email,fullName")}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains(first.Email, html);
        Assert.Contains(first.FullName, html);
        Assert.True(
            html.IndexOf(first.Email, StringComparison.Ordinal) < html.IndexOf(first.FullName, StringComparison.Ordinal),
            "email column should render before fullName when cols=email,fullName");
    }

    [Fact]
    public async Task Rows_UnknownCols_FallBackToDefaultOrder()
    {
        var client = _factory.CreateClient();
        var first = EmployeeData.All[0];

        var response = await client.GetAsync($"/netgrid/employees/rows?sort=id&pageSize=1&cols={Uri.EscapeDataString("hacker,email")}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // "hacker" is dropped; "email" is honored first, remaining columns appended in default order.
        Assert.True(html.IndexOf(first.Email, StringComparison.Ordinal) < html.IndexOf(first.FullName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shell_RendersPinnedColumnMarkup()
    {
        var client = _factory.CreateClient();
        var html = await (await client.GetAsync("/netgrid/employees")).Content.ReadAsStringAsync();

        Assert.Contains("data-pin=\"fullName\"", html);
        Assert.Contains("data-pin=\"__select\"", html);
        Assert.Contains("draggable=\"true\"", html);
        Assert.Contains("sticky z-30", html);
        Assert.Contains("\"pin\":true", html);
    }

    [Fact]
    public async Task Shell_RespectColsParam_DeepLink()
    {
        var client = _factory.CreateClient();
        var first = EmployeeData.All[0];

        var cols = Uri.EscapeDataString("email,fullName");
        var html = await (await client.GetAsync($"/netgrid/employees?sort=id&pageSize=1&cols={cols}")).Content.ReadAsStringAsync();

        Assert.True(
            html.IndexOf(first.Email, StringComparison.Ordinal) < html.IndexOf(first.FullName, StringComparison.Ordinal),
            "shell should honor cols deep-link ordering");
    }

    [Fact]
    public async Task Export_ReturnsFullFilteredDataset_AsCsv()
    {
        var client = _factory.CreateClient();
        var expected = EmployeeData.All.Count(e => e.Department == Department.Design);

        var filter = $"filter={Uri.EscapeDataString("department:equals:Design")}";
        var response = await client.GetAsync($"/netgrid/employees/export?{filter}&sort=id");

        response.EnsureSuccessStatusCode();
        Assert.Contains("text/csv", response.Content.Headers.ContentType?.MediaType);

        var disposition = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        Assert.Equal("employees.csv", disposition);

        var csv = await response.Content.ReadAsStringAsync();
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("Full name,Email,Department,Salary,Hired on,Score,Status", lines[0]);
        Assert.Equal(expected + 1, lines.Length);   // header + matching rows (full dataset, not a page)
    }

    [Fact]
    public async Task Export_RespectsColsOrder()
    {
        var client = _factory.CreateClient();
        var cols = Uri.EscapeDataString("email,fullName");

        var csv = await (await client.GetAsync($"/netgrid/employees/export?sort=id&pageSize=1&cols={cols}")).Content.ReadAsStringAsync();
        var header = csv.Split("\r\n")[0];

        Assert.StartsWith("Email,Full name,", header);
    }

    private sealed record CsvRow(string Name, string City);

    [Fact]
    public void CsvExporter_EscapesQuotesCommasAndNewlines()
    {
        var options = new NetOpenGrid.Application.Builders.GridOptionsBuilder<CsvRow>()
            .WithId("csv")
            .AddColumn("name", r => r.Name)
            .AddColumn("city", r => r.City)
            .Build();

        var rows = new List<CsvRow>
        {
            new("Ana, jr", "Madrid"),
            new("Say \"hi\"", "Li\nma")
        };

        var csv = NetOpenGrid.Infrastructure.Export.GridCsvExporter.Build(options.Columns, rows);
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Name,City", lines[0]);
        Assert.Equal("\"Ana, jr\",Madrid", lines[1]);
        Assert.Equal("\"Say \"\"hi\"\"\",\"Li\nma\"", lines[2]);
    }

    [Fact]
    public async Task Rows_GroupedByDepartment_RendersGroupHeaders()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/netgrid/employees/rows?groupby=department&pageSize=10");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-group-path=\"department=", html);
        Assert.Contains("toggleGroup('department=", html);   // exact single-quoted JS call
        Assert.DoesNotContain("toggleGroup(''", html);        // regression: doubled quotes break the expression
        Assert.Contains("aria-expanded=\"false\"", html);

        var expectedGroups = EmployeeData.All.Select(e => e.Department).Distinct().Count();
        Assert.Equal(expectedGroups.ToString(), response.Headers.GetValues("X-Grid-Total").Single());
    }

    [Fact]
    public async Task Rows_GroupExpanded_ShowsNestedRows()
    {
        var client = _factory.CreateClient();
        var design = EmployeeData.All.Where(e => e.Department == Department.Design).ToList();

        var expand = Uri.EscapeDataString("department=Design");
        var html = await (await client.GetAsync($"/netgrid/employees/rows?groupby=department&expand={expand}&pageSize=10"))
            .Content.ReadAsStringAsync();

        Assert.Contains("aria-expanded=\"true\"", html);
        Assert.Contains("data-group-path=\"department=Design\"", html);

        foreach (var employee in design)
        {
            Assert.Contains(employee.FullName, html);
        }
    }

    [Fact]
    public async Task ClientRuntime_IsHealthy_NoCorruptedBlocks()
    {
        var client = _factory.CreateClient();
        var js = await (await client.GetAsync("/_netgrid/netopengrid.js")).Content.ReadAsStringAsync();

        // Regression guards: a stray duplicated block closer once broke the whole runtime
        // (every Alpine expression on the page failed with "netgrid is not defined").
        Assert.DoesNotContain("},\n      },\n\n      seedFromUrl", js);
        Assert.Contains("Alpine.data('netgrid'", js);
        Assert.Contains("document.addEventListener('alpine:init'", js);
        Assert.Contains("toggleGroup(", js);
        Assert.Contains("applyPinnedOffsets(", js);
    }

    [Fact]
    public async Task UnknownGridId_Returns404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/netgrid/nope/rows");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_PostsSelectedIds_AsCsv()
    {
        var client = _factory.CreateClient();
        var employee = EmployeeData.All[0];

        var form = new Dictionary<string, string> { ["ids"] = employee.Id.ToString() };
        var response = await client.PostAsync("/netgrid/employees/export", new FormUrlEncodedContent(form));

        response.EnsureSuccessStatusCode();
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Id,Full name,Email", csv);
        Assert.Contains(employee.FullName, csv);
    }

    [Fact]
    public async Task ComponentAssets_AreServedEmbedded_WithImmutableCaching()
    {
        var client = _factory.CreateClient();

        string[] paths =
        [
            "/_netgrid/netopengrid.js",
            "/_netgrid/vendor/htmx.min.js",
            "/_netgrid/vendor/alpine.min.js"
        ];

        foreach (var path in paths)
        {
            var response = await client.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode, $"Expected 200 for {path}");
            Assert.StartsWith("text/javascript", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("immutable", response.Headers.CacheControl?.ToString());
        }
    }
}
