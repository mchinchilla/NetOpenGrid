# NetOpenGrid.Example

A Razor Pages app that embeds three NetOpenGrid grids in its own views, the way a real app would.

## Run

```bash
dotnet run --project samples/NetOpenGrid.Example
# http://localhost:5188
```

## Pages

| Page | Grid | What it shows |
|---|---|---|
| `/` | `products` | Typed in-memory source, computed column (`Price * Stock`), currency and date formats, status badges, pinned SKU, row selection with a selection-export endpoint, and a blank-header *Details* column that opens `/products/{sku}` |
| `/orders` | `orders` | EF Core over in-memory SQLite: filters, sorting, paging, Excel-style value counts and grouping run in SQL. Opens grouped by country; the page's query string is forwarded to the grid, so links such as `/orders?filter=status:equals:pending&sort=total:desc` are deep links |
| `/currencies` | `currencies` | JSON mode (`JsonElement` rows) with the `midnight` theme |
| `/products/{sku}` | none | Plain Razor page reached from the grid's row-action column |

Every grid also works standalone at `/netgrid/{id}`, for example `http://localhost:5188/netgrid/orders`.

## How the embedding works

- Each page renders `<iframe data-netgrid src="/netgrid/{id}?embed=1">`. `embed=1` drops the standalone
  chrome and makes the grid background transparent.
- The script at the end of `Pages/Shared/_Layout.cshtml` resizes every `data-netgrid` frame to its content.
  It can do that because the grid is served from the same origin.
- `wwwroot/css/site.css` styles the host pages only. The grid is styled by `wwwroot/css/netopengrid-{theme}.css`
  inside its frame, so neither stylesheet can affect the other.
- Grids use `WithMinHeight("")` because the frame, not the grid, decides the height.

## Features by file

| Feature | Where |
|---|---|
| Grid registration, EF Core wiring, seeding, selection-export endpoint | `Program.cs` |
| Humanized headers from expression columns (`ReleasedOn` becomes "Released on") | `AddColumn(p => p.ReleasedOn, ...)` |
| Blank header and trusted HTML for a row-action column | `details` column, `Header("")` + `RawCellHtml` |
| EF Core entity, `DbContext` and demo data | `Samples/Order.cs`, `Samples/ShopDb.cs`, `Samples/OrderData.cs` |
| UI language (`en` or `es`) | `appsettings.json`, key `NetOpenGrid:Culture` |
| Host layout, navigation and frame sizing | `Pages/Shared/_Layout.cshtml` |

The component serves its own JS runtime, HTMX and Alpine.js from the assembly at `/_netgrid/*`. Only the
Tailwind theme CSS lives under `wwwroot/css`; `tools/build-themes.sh` recompiles it.

To use the published packages instead of the project references in the `.csproj`:

```bash
dotnet add package NetOpenGrid
dotnet add package NetOpenGrid.Persistence.EFCore
```
