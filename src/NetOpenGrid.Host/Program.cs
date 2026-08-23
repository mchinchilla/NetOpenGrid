using System.Globalization;
using System.Text;
using System.Text.Json;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Application.Json;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Domain.GridQuerying;
using NetOpenGrid.Host.Samples;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;

var builder = WebApplication.CreateBuilder(args);

var ordersOptions = new JsonGridOptionsBuilder()
    .WithId("orders")
    .WithTitle("Orders")
    .WithSubtitle("JSON mode via JsonElement")
    .WithTheme("grid")
    .WithDefaultPageSize(8)
    .WithPageSizeChoices([8, 16, 32])
    .AddColumn("id", c => c.Header("Order"))
    .AddColumn("customer", c => c.Header("Customer").Searchable())
    .AddColumn("product", c => c.Header("Product"))
    .AddColumn("amount", c => c
        .Header("Amount")
        .Align(ColumnAlign.End)
        .AllowedOps(FilterOpSet.Numeric))
    .AddColumn("status", c => c.Header("Status").RawCellHtml(row =>
        row.GetProperty("status").GetString() is { } status
            ? $"<span class=\"badge badge-{status}\">{status}</span>"
            : string.Empty))
    .AddColumn("placedAt", c => c.Header("Placed at").Sortable())
    .Build();

builder.Services.AddNetOpenGrid()
    .AddGrid<Employee>("employees", options => options
        .WithTitle("Employees")
        .WithSubtitle("Typed <T> source, precompiled strategies")
        .WithTheme("grid")
        .WithDefaultPageSize(10)
        .WithPageSizeChoices([10, 25, 50])
        .WithDebounce(300)
        .EnableRowSelection(e => e.Id.ToString("D"))
        .AddColumn("fullName", e => e.FullName, c => c.Header("Full name").Searchable())
        .AddColumn("email", e => e.Email)
        .AddColumn("department", e => e.Department, c => c.Header("Department"))
        .AddColumn("salary", e => e.Salary, c => c
            .Header("Salary")
            .Align(ColumnAlign.End)
            .Format(s => s.ToString("C0", CultureInfo.GetCultureInfo("en-US"))))
        .AddColumn("hiredOn", e => e.HiredOn, c => c
            .Header("Hired on")
            .Format(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
        .AddColumn("score", e => e.PerformanceScore, c => c.Header("Score").Align(ColumnAlign.Center))
        .AddColumn("active", e => e.IsActive, c => c.Header("Status").RawCellHtml(e =>
            $"<span class=\"badge {(e.IsActive ? "badge-success" : "badge-muted")}\">{(e.IsActive ? "Active" : "Inactive")}</span>")),
        (_, options) => new InMemoryGridDataSource<Employee>(options, EmployeeData.All))
    .AddGrid(ordersOptions, (_, options) => new JsonGridDataSource(options, OrdersPayload.Json));

var app = builder.Build();

app.UseStaticFiles();
app.MapNetOpenGrid();

app.MapGet("/", () => Results.Redirect("/netgrid/employees"));

app.MapPost("/netgrid/{gridId}/export", async (string gridId, HttpRequest request, CancellationToken cancellationToken) =>
{
    await Task.Yield();
    var ids = request.Form["ids"].ToString()
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    if (ids.Length == 0 || gridId != "employees")
    {
        return Results.BadRequest("No rows selected.");
    }

    var selected = ids.ToHashSet(StringComparer.Ordinal);
    var rows = EmployeeData.All.Where(e => selected.Contains(e.Id.ToString("D")));
    return Results.File(Encoding.UTF8.GetBytes(EmployeeData.ToCsv(rows)), "text/csv", "employees.csv");
});

app.Run();

public partial class Program;
