# NetOpenGrid.Example

Minimal consumer project showing how to wire the grid in a real app.

## Run

```bash
dotnet run --project samples/NetOpenGrid.Example
# http://localhost:5188/netgrid/products
```

## What it demonstrates

| Feature | Where |
|---|---|
| Typed `<T>` grid (`Product`) with fluent `AddGrid` | `Program.cs` |
| Computed column (`Price * Stock`) sortable/filterable, no reflection | `inventoryValue` column |
| Custom cell formatting (currency, dates) | `Format(...)` |
| Trusted raw HTML cells (status badges) | `RawCellHtml(...)` |
| Row selection + CSV export endpoint | `EnableRowSelection` + `MapPost(.../export)` |
| Second grid in pure JSON mode (`JsonElement`) | `currenciesOptions` + `JsonGridDataSource` |
| Navigation between grids via shell header | `WithNavLinks(...)` |

Static assets: the component serves its own JS runtime, HTMX and Alpine.js
embedded from the assembly at `/_netgrid/*` (zero setup). Only the Tailwind theme
CSS lives under `wwwroot/css`, compiled for both Host and Example by
`tools/build-themes.sh` (also invoked automatically when `NetOpenGrid.Host` builds).
