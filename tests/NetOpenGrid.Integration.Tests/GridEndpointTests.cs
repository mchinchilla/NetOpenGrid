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
