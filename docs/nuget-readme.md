# NetOpenGrid

**Server-rendered HTML data grid for ASP.NET Core (.NET 10): HTMX + Alpine.js on the client, precompiled delegates on the server.**

[![NuGet](https://img.shields.io/nuget/v/NetOpenGrid?style=flat-square&logo=nuget&label=NuGet)](https://www.nuget.org/packages/NetOpenGrid)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-22c55e?style=flat-square)](https://github.com/mchinchilla/NetOpenGrid/blob/main/LICENSE)

No virtual DOM. No reflection on the hot path. No per-request expression compilation. The server renders
only the current page's `<tbody>`; filtering, sorting, searching, paging and grouping run on delegates
compiled exactly once when the grid is configured.

Full documentation (English and Spanish), architecture diagrams, the configuration reference and a runnable
sample live on GitHub: <https://github.com/mchinchilla/NetOpenGrid>

## Install

```bash
dotnet add package NetOpenGrid                      # meta-package: Infrastructure + Application + Domain
dotnet add package NetOpenGrid.Persistence.EFCore   # optional: EF Core data source (SQL push-down)
```

Or reference only the pieces you need. All packages share the same version number.

| Package | Contents |
|---|---|
| `NetOpenGrid` | Meta-package with no assembly of its own. References the three packages below. |
| `NetOpenGrid.Infrastructure` | `AddNetOpenGrid()`, `MapNetOpenGrid()`, HTML renderer, endpoints, embedded JS/HTMX/Alpine assets, CSV export, i18n |
| `NetOpenGrid.Application` | `GridOptionsBuilder<T>`, request parser, in-memory pipeline (filter, search, sort, page, group), JSON mode. No ASP.NET Core dependency |
| `NetOpenGrid.Domain` | Contracts and descriptors: columns, filters, sorting, paging, grouping, `IGridDataSource<T>`. Dependency-free |
| `NetOpenGrid.Persistence.EFCore` | `EFCoreGridDataSource<T>`: translates filters, sorting, paging and value counts into `IQueryable` expressions |

## Quick start

**1. Register the grid.** Options are validated and compiled once, at startup:

```csharp
using NetOpenGrid.Infrastructure;

builder.Services.AddNetOpenGrid(o =>        // optional: asset/css routes
{
    o.AssetPrefix = "/_netgrid";            // js + htmx + alpine served by the component
    o.CssPath = "/css";                     // where YOUR compiled theme css lives
})
.AddGrid<Employee>("employees",
    options => options
        .WithTitle("Employees")
        .WithTheme("grid")
        .WithDefaultPageSize(10)
        .EnableRowSelection(e => e.Id.ToString("D"))
        .AddColumn("fullName", e => e.FullName, c => c.Header("Full name").Searchable())
        .AddColumn("salary", e => e.Salary, c => c
            .Header("Salary")
            .Align(ColumnAlign.End)
            .Format(v => v.ToString("C0", CultureInfo.GetCultureInfo("en-US"))))
        .AddColumn("hiredOn", e => e.HiredOn, c => c.Format(d => d.ToString("yyyy-MM-dd"))),
    (_, opts) => new InMemoryGridDataSource<Employee>(opts, EmployeeData.All));
```

**2. Map the endpoints.** Shell, row fragment and component assets:

```csharp
app.UseStaticFiles();     // only for your theme css
app.MapNetOpenGrid();     // GET /netgrid/:id · /rows · /values · /export · /_netgrid/*
```

**3. Open `http://localhost:PORT/netgrid/employees`.** The component serves its own JS (Alpine + HTMX
embedded in the assembly); there is nothing to copy into `wwwroot`. Compile a Tailwind v4 theme to
`wwwroot/css/netopengrid-{theme}.css`; the repository ships the `grid` and `midnight` sources under
`themes/` and a `tools/build-themes.sh` script.

## EF Core (SQL push-down)

`EFCoreGridDataSource<T>` composes `Where` / `OrderBy` / `Skip` / `Take` on your `IQueryable<T>` using the
same columns, operators and parsing rules as the in-memory grid.

```csharp
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite(cs));

builder.Services.AddNetOpenGrid().AddGrid<Employee>("employees",
    options => options
        .AddColumn(e => e.FullName, c => c.Searchable())   // use the Expression overloads
        .AddColumn(e => e.Salary)
        .AddColumn(e => e.HiredOn),
    (sp, opts) => new EFCoreGridDataSource<Employee>(
        opts,
        sp,
        (sp, db) => db.Set<Employee>().AsNoTracking()));
```

- Define columns with the `AddColumn(e => e.Prop, ...)` expression overloads. A sortable, filterable or
  searchable column that only has a `Func` fails at construction with the offending fields listed.
- String operators translate to `EF.Functions.Like` with escaped wildcards.
- Filter literals are parsed once with the same invariant parser as the in-memory pipeline and embedded
  as constants in the expression tree.
- The data source opens an `IServiceScope` per request, so a scoped `DbContext` works as-is.
- SQL does not guarantee stable ties: add a sort on a unique column for deterministic paging.

## JSON mode

Same pipeline over `JsonElement` rows. Selectors are property names; no reflection.

```csharp
var options = new JsonGridOptionsBuilder()
    .WithId("orders")
    .AddColumn("customer", c => c.Searchable())
    .AddColumn("amount", c => c.AllowedOps(FilterOpSet.Numeric))
    .Build();

builder.Services.AddNetOpenGrid().AddGrid(options, (_, o) => new JsonGridDataSource(o, json));
// also: new JsonGridDataSource(o, streamLoader) · JsonGridDataSource.FromFileAsync(...)
```

Missing properties behave like `null` (sort lowest, match `is-empty`).

## Features

- Pluggable data sources: in-memory, JSON (`JsonElement`) and EF Core with SQL push-down.
- 11 filter operators with a per-column-type whitelist (text, numeric, date, enum, bool). Compact
  symbols `=` `!=` `>` `>=` `<` `<=` `~` are accepted in the query string.
- Excel-style filters: distinct-value checklist with counts that ignore the column's own filter.
- Global search with debounce, OR across `Searchable` columns.
- Stable multi-sort (shift-click) with deterministic ties and nulls first.
- Nested grouping up to 3 levels (`groupby=` + `expand=` in the URL). Groups are paged, not rows.
- Server-side paging with meta headers: `X-Grid-Total`, `X-Grid-Page`, `X-Grid-Page-Size`, `X-Grid-Page-Count`.
- Row selection with a floating action bar. CSV export (RFC 4180) of the selection or of the full
  filtered dataset, honouring the column formatters and the current column order.
- Pinned (sticky) columns and drag-and-drop reordering persisted in `localStorage`.
- i18n: English defaults, bundled Spanish preset, per-key overrides.
- Deep links: `page`, `pageSize`, `sort`, `filter`, `q`, `groupby`, `expand` and `cols` all live in the URL.
- Embedded assets (JS + HTMX + Alpine) served with immutable caching (`?v={sha}`).
- Tailwind v4 theming with `grid` and `midnight` presets and persisted dark mode.

## Client / server contract

| Param | Example | Notes |
|---|---|---|
| `page` / `pageSize` | `2` / `25` | 1-based; `pageSize` is clamped to `MaxPageSize` |
| `sort` | `salary:desc` (repeatable) | stable multi-sort |
| `filter` | `city:contains:li` · `amount:>=100` · `category:in:["Books","Toys"]` | per-column whitelist |
| `q` | `nico` | global search, OR across `Searchable` columns |
| `groupby` | `department,city` | nested grouping, max 3 levels |
| `expand` | `department=Design` | expanded group paths, levels joined with a pipe character |
| `cols` | `email,fullName` | column order; validated, missing fields appended |

Endpoints mapped by `MapNetOpenGrid()`: `GET /netgrid/:id` (shell), `/rows` (`<tbody>` fragment),
`/values?field=...` (distinct values with counts), `/export` (CSV of the current context) and
`/_netgrid/*` (assets).

## i18n

```csharp
builder.Services.AddNetOpenGrid(
    o => { o.AssetPrefix = "/_netgrid"; },
    loc => loc.UseCulture("es")                       // bundled preset
              .Set("filter.apply", "Filtrar"));       // fine-grained override
```

## Requirements and notes

- .NET 10 / ASP.NET Core. `NetOpenGrid.Persistence.EFCore` targets EF Core 10.
- Themes are plain Tailwind v4 CSS that you compile and serve from `o.CssPath` as
  `netopengrid-{theme}.css`. Copy `themes/grid.css` from the repository as a starting point.
- `RawCellHtml` is the only place where trusted HTML is emitted; every other cell value is HTML-encoded.

## License

NetOpenGrid is released under the MIT license. HTMX and Alpine.js are embedded in the assembly and keep
their own licenses.
