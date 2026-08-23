# NetOpenGrid

Grid component for **.NET 10 / C#** inspired by DevExtreme/XtraGrid, but rebuilt around a different thesis:
**server-rendered fragments + precompiled delegates**. No virtual DOM, no reflection at request time,
no per-request expression compilation.

Reactivity: **HTMX** (partial swaps) + **Alpine.js** (local state). Styling: **Tailwind CSS v4** compiled
with its standalone CLI (`tailwindcss` in PATH) into local `wwwroot/css` dists.

```
dotnet run --project src/NetOpenGrid.Host     # http://localhost:5177/netgrid/employees
./tools/build-themes.sh                       # recompile Tailwind themes
dotnet test                                   # 52 tests
```

## Architecture (DDD)

```
NetOpenGrid.slnx
├─ src/
│  ├─ NetOpenGrid.Domain/          Pure model: GridQuery, PageResult<T>, GridColumn<T>,
│  │                               ISortStrategy/IFilterStrategy/ISearchStrategy, IGridDataSource<T>
│  ├─ NetOpenGrid.Application/     GridOptionsBuilder<T> (fluent/Options), GridRequestParser,
│  │                               InMemoryGridPipeline (filter→search→stable sort→slice),
│  │                               GridQueryEngine<T>, InMemoryGridDataSource<T>, JSON mode
│  │                               (JsonGridDataSource, JsonColumnBuilder over JsonElement)
│  ├─ NetOpenGrid.Infrastructure/  GridRuntime<T> (keyed DI), GridHtmlRenderer<T> (pooled writers),
│  │                               MapNetOpenGrid() HTMX endpoints, request mapping
│  └─ NetOpenGrid.Host/            Demo app: employees (<T>) + orders (JSON), CSV export
└─ tests/                          Domain.Tests, Application.Tests, Integration.Tests (WebApplicationFactory)
```

Dependencies flow inward only: `Host → Infrastructure → Application → Domain`.

## Performance decisions

- **No reflection on the hot path.** Column selectors are plain delegates (`Func<T,TKey>`); sort/filter/search
  strategies are built once at options-build time and reused for every request. Expression selectors are
  compiled once at startup (ergonomic sugar, never per-request).
- **Hardcoded scalar parser table** (`DefaultValueParsers<TKey>`): int/long/decimal/double/DateTime/DateOnly/
  DateTimeOffset/Guid/etc. via closed generic delegate casts. No `MakeGenericType`, no `Activator`.
- **Stable multi-key sort**: decorated-index single array sort with a composite `Comparison` — deterministic
  ties, no LINQ iterator chains.
- **Rendering**: pooled `StringBuilder` + `HtmlEncoder.Encode(TextWriter, ...)` writing straight into the
  buffer; zero intermediate strings per cell. Rows fragment = pure `<tbody>` innerHTML.
- **Async everywhere**: data source is the only async boundary (`ValueTask`); CPU work is synchronous by design.
- **Culture-invariant** parsing/formatting; whitelisted fields/operators; paging clamped server-side.

## Usage

```csharp
builder.Services.AddNetOpenGrid()
    .AddGrid<Employee>("employees", options => options
        .WithTitle("Employees")
        .WithTheme("grid")
        .WithDefaultPageSize(10)
        .EnableRowSelection(e => e.Id.ToString("D"))
        .AddColumn("fullName", e => e.FullName, c => c.Header("Full name").Searchable())
        .AddColumn("salary", e => e.Salary, c => c.Header("Salary").Align(ColumnAlign.End))
        .AddColumn("hiredOn", e => e.HiredOn, c => c.Format(d => d.ToString("yyyy-MM-dd"))),
        (_, opts) => new InMemoryGridDataSource<Employee>(opts, EmployeeData.All));

app.MapNetOpenGrid();   // GET /netgrid/{id} shell · GET /netgrid/{id}/rows fragment
```

JSON mode (same pipeline, `JsonElement` rows):

```csharp
var options = new JsonGridOptionsBuilder().WithId("orders")
    .AddColumn("customer", c => c.Searchable())
    .AddColumn("amount", c => c.AllowedOps(FilterOpSet.Numeric))
    .Build();

services.AddNetOpenGrid().AddGrid(options, (_, o) => new JsonGridDataSource(o, json));
```

### Request contract (client ↔ server)

| Param      | Example                        | Meaning                          |
|------------|--------------------------------|----------------------------------|
| `page`     | `2`                            | 1-based page                     |
| `pageSize` | `25`                           | clamped to `MaxPageSize`         |
| `sort`     | `salary:desc` (repeatable)     | field:asc\|desc, shift-click = multi |
| `filter`   | `city:contains:li`, `amount:>=100`, `status:is-empty` | whitelist-enforced |
| `q`        | `nico`                         | global search across searchable columns |

Meta comes back as headers: `X-Grid-Total`, `X-Grid-Page`, `X-Grid-Page-Size`, `X-Grid-Page-Count`.
Deep links work: the URL query string is the client state (seeded on load, updated on change).

## Themes

Tailwind v4 sources live in `themes/*.css` (they `@source "../src"` so utility classes used inside the C#
renderer are detected). Each theme compiles to `src/NetOpenGrid.Host/wwwroot/css/netopengrid-{theme}.css`
via `tools/build-themes.sh` (or `.cmd` on Windows). The Host build target runs it automatically.
Dark mode toggles a `.dark` class on `<html>`, persisted in `localStorage`.

## Self-contained client runtime

The component ships its own browser assets embedded in the assembly:
`netopengrid.js` (Alpine component + HTMX wiring), `htmx.min.js` and `alpine.min.js`.
`MapNetOpenGrid()` serves them automatically at `/_netgrid/...` with
`Cache-Control: immutable` and content-hashed `?v=` URLs — **no files to copy into
your wwwroot**.

```csharp
builder.Services.AddNetOpenGrid(o =>      // optional
{
    o.AssetPrefix = "/_netgrid";          // where the component serves js/vendor
    o.CssPath = "/css";                   // where YOUR compiled theme css lives
    o.CssFilePrefix = "netopengrid-";     // css file naming: netopengrid-{theme}.css
});
```

Only the Tailwind theme CSS stays app-side (each app compiles its own with the
`tailwindcss` CLI; see Themes). To refresh vendor libs: `tools/fetch-vendor.sh`
(downloads into `src/NetOpenGrid.Infrastructure/Assets/vendor/`), then rebuild.

### Column filter popovers

Each filterable column renders its own popover anchored inside the `<th>` and
opens **inward** (first filterable column opens rightward, the rest leftward).
A runtime auto-flip guard measures the opened popover against the table card and
flips horizontally/vertically when it would be clipped by the scroll container.

### Minimum height

The table card keeps a minimum height (`MinHeight`, default `64rem` ≈ 25 rows) so
filtering down to a few rows never collapses the grid or clips the filter popover.
The empty state centers its message using the same height. Disable with
`.WithMinHeight("")` or set any CSS value (`.WithMinHeight("32rem")`).

## Notes

- `RawCellHtml(...)` emits trusted server-side HTML (badges, links) — never feed user input into it;
  normal cells are always HTML-encoded.
- Filter operators are intersected at build time: column type presets ∩ requested ops, so the UI can never
  produce an operator the strategy cannot execute.
