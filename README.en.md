<div align="center">

[![Docs: ES](https://img.shields.io/badge/docs-ES-9CA3AF?style=flat-square)](README.md)
[![Docs: EN](https://img.shields.io/badge/docs-EN-4F46E5?style=flat-square)](README.en.md)

# ⚡ NetOpenGrid

**The .NET 10 grid built on a different architecture: server-rendered HTML + precompiled delegates.**

No virtual DOM. No reflection on the hot path. No per-request expression compilation.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![C#](https://img.shields.io/badge/C%23-14-512BD4?style=for-the-badge&logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind-v4-06B6D4?style=for-the-badge&logo=tailwindcss&logoColor=white)](https://tailwindcss.com)
[![HTMX](https://img.shields.io/badge/HTMX-2-3D72D7?style=for-the-badge)](https://htmx.org)
[![Alpine.js](https://img.shields.io/badge/Alpine.js-3-77C1CB?style=for-the-badge&logo=alpinedotjs&logoColor=white)](https://alpinejs.dev)
[![Tests](https://img.shields.io/badge/tests-116%20passing-16A34A?style=for-the-badge&logo=xunit&logoColor=white)](#-testing)
[![License](https://img.shields.io/badge/License-MIT-F59E0B?style=for-the-badge)](LICENSE)
[![NuGet](https://img.shields.io/nuget/v/NetOpenGrid?style=for-the-badge&logo=nuget&logoColor=white&label=NuGet&color=004880)](https://www.nuget.org/packages/NetOpenGrid)
[![Publish](https://img.shields.io/github/actions/workflow/status/mchinchilla/NetOpenGrid/publish.yml?branch=main&style=for-the-badge&logo=githubactions&logoColor=white&label=publish)](https://github.com/mchinchilla/NetOpenGrid/actions/workflows/publish.yml)

</div>

![NetOpenGrid embedded in a Razor page: product catalog with row selection and the Excel-style Category filter open](docs/images/products-grid.png)

---

## 📖 Table of contents

- [Why it exists](#-why-it-exists)
- [✨ Features](#-features)
- [Stack](#-stack)
- [Architecture](#-architecture)
- [Installation](#-installation)
- [Quickstart](#-quickstart)
- [Razor view (embedded grid)](#-razor-view-embedded-grid)
- [Workflows](#-workflows)
- [EF Core (SQL push-down)](#-ef-core-sql-push-down)
- [Excel-style filters (per-value counts)](#-excel-style-filters-per-value-counts)
- [Pinned, reorderable, resizable & hideable columns](#-pinned-reorderable-resizable--hideable-columns)
- [Clickable rows & row actions](#️-clickable-rows--row-actions)
- [Server-side export (CSV & Excel)](#-server-side-export-csv--excel)
- [i18n](#-i18n)
- [Column grouping (nested)](#-column-grouping-nested)
- [Aggregates (SUM · AVG · MIN · MAX)](#-aggregates-sum--avg--min--max)
- [Keyboard & accessibility](#️-keyboard--accessibility)
- [Saved views](#-saved-views)
- [Virtual scroll](#-virtual-scroll)
- [Configuration reference](#-configuration-reference)
- [Column types & operators](#-column-types--operators)
- [Client ↔ server contract](#-client--server-contract)
- [JSON mode](#-json-mode)
- [Themes (Tailwind v4)](#-themes-tailwind-v4)
- [Performance](#-performance)
- [Project layout](#-project-layout)
- [Testing](#-testing)
- [Roadmap](#-roadmap)
- [License](#-license)

---

## 🧭 Why it exists

Traditional commercial grid suites ship thousands of rows to the client and pay for it with massive
JS bundles and reflection. **NetOpenGrid flips the model**: the server renders only the current page's
`<tbody>`, and all the intelligence (filter, sort, search, paging) runs on
**delegates compiled exactly once** when the grid is configured.

```mermaid
flowchart LR
    A["Traditional commercial grids"] -->|"thousands of rows to the client"| B["heavy JS + reflection"]
    C["NetOpenGrid"] -->|"only the current page (tbody)"| D["HTML + precompiled delegates"]
    B --> E["🐌 slow hydration"]
    D --> F["🚀 first render = final render"]
```

> **Golden rule of the project:** zero reflection and zero `Expression.Compile` per request.
> Everything is compiled while building the options; the hot path only invokes delegates.

---

---

## ✨ Features

| Category | Feature | Detail |
|---|---|---|
| 📊 **Data** | Pluggable sources | `InMemoryGridDataSource<T>` · `JsonGridDataSource` (`JsonElement`) · `EFCoreGridDataSource<T>` (SQL push-down) |
| | Typed, reflection-free | Delegate or expression selectors; sort/filter/search strategies compiled once at startup |
| 🔍 **Filters** | By operator | 11 operators with per-column-type whitelist (text/num/date/enum/bool) |
| | Excel-style | Distinct-value checklist **with counts** (Excel context: ignores the column's own filter) |
| | Global search | `q` with debounce, OR across `Searchable` columns |
| ↕️ **Sorting** | Stable multi-sort | Shift-click, deterministic ties (decorated array), nulls first |
| 🗂️ **Grouping** | Nested up to 3 levels | `groupby` + `expand` in the URL; chevron headers with counts; groups are paged |
| ➕ **Aggregates** | SUM · AVG · MIN · MAX | Top/bottom totals and per-group subtotals over the whole filtered set; pushed down to SQL |
| 📄 **Paging** | Server-side | `pageSize` clamping, meta via headers, full deep-linking |
| ☑️ **Selection** | Rows + export | Floating bar, select-all, CSV of selection or the full filtered dataset |
| 📌 **Columns** | Pin + reorder | Sticky with JS-computed offsets, drag & drop persisted in `localStorage` |
| 🌐 **i18n** | EN/ES presets | ~39 keys, per-key overrides, `__NETGRID__.locale` blob for the client |
| 🎨 **Theming** | Tailwind v4 | `grid`/`midnight` presets, persisted dark mode, class scanning inside C# |
| 🔌 **Self-contained** | Embedded assets | JS + HTMX + Alpine inside the assembly, served with immutable `?v={sha}` |
| 🖱️ **Rows** | Clickable + actions | `WithRowLink` and `WithRowActions` (links and `netgrid:action` events for your page) |
| 🔖 **Views** | Predefined + per user | `AddView(...)` on the server; each user saves their own in the browser |
| 🔗 **Deep links** | State in URL | `page·pageSize·sort·filter·q·groupby·expand·cols·hide` — share the exact view |
| ♿ **A11y** | Localized aria-labels | Roles, `aria-sort`, `aria-expanded`, focus rings, `x-cloak` |
| ⌨️ **Keyboard** | Roving tabindex | Arrow keys across cells, PageUp/PageDown, Enter/Space; the body is a single Tab stop |

## 🧱 Stack

| Layer | Technology | Role |
|---|---|---|
| 🔩 Runtime | **.NET 10 / C# 14** (`net10.0`) | records, collection expressions, keyed DI, `ValueTask` |
| 🖥️ Server | **ASP.NET Core** Minimal APIs | shell + fragment + embedded-asset endpoints |
| ⚡ Reactivity | **HTMX 2** | `htmx.ajax` swaps only the `<tbody>` |
| 🪶 Local state | **Alpine.js 3** | paging, filters, selection, theme — no framework |
| 🎨 Styling | **Tailwind CSS v4** (standalone CLI) | themes compiled into the assembly, dark mode |
| 🗄️ Database | **EF Core 10** (optional) | filter/sort push-down to SQL |
| ✅ Testing | **xUnit** + `WebApplicationFactory` | unit + real HTTP integration tests |

---

## 🏛️ Architecture

Dependencies point inward (DDD). The client never sees the Domain.

```mermaid
flowchart TB
    subgraph Host["🖥️ NetOpenGrid.Host / your app"]
        P["Program.cs<br/>AddNetOpenGrid().AddGrid(...)<br/>MapNetOpenGrid()"]
    end

    subgraph Infra["🛠️ Infrastructure"]
        RT["GridRuntime&lt;T&gt;<br/>(keyed DI per gridId)"]
        R["GridHtmlRenderer&lt;T&gt;<br/>pooled builders + encoder"]
        EP["Endpoints<br/>GET /netgrid/:id · /rows · /_netgrid/*"]
        A["Embedded assets<br/>netopengrid.js · htmx · alpine"]
    end

    subgraph App["⚙️ Application"]
        OB["GridOptionsBuilder&lt;T&gt;<br/>validates once, compiles strategies"]
        ENG["GridQueryEngine&lt;T&gt;"]
        PIPE["InMemoryGridPipeline<br/>filter → search → stable sort → slice"]
        PARSER["GridRequestParser<br/>whitelist + clamping"]
        JSON["JsonGridDataSource<br/>JsonElement strategies"]
    end

    subgraph Dom["💎 Domain (pure)"]
        M["GridQuery · PageResult&lt;T&gt; · GridColumn&lt;T&gt;<br/>ISortStrategy · IFilterStrategy · IGridDataSource&lt;T&gt;"]
    end

    P --> EP --> RT --> R
    RT --> PARSER --> ENG --> PIPE
    RT --> JSON
    OB -. "precompiled strategies" .-> PIPE
    PIPE --> M
    ENG --> M
```

---

## 📦 Installation

Packages are published to [NuGet](https://www.nuget.org/packages?q=NetOpenGrid). Every commit to `main`
publishes a new version and creates the matching `vX.Y.Z` tag (see [`.github/workflows/publish.yml`](.github/workflows/publish.yml)).

| Package | What it is for |
|---|---|
| [`NetOpenGrid`](https://www.nuget.org/packages/NetOpenGrid) | **The one you install in your ASP.NET Core app.** Meta-package with no assembly of its own: it just references `Infrastructure`, `Application` and `Domain`. |
| [`NetOpenGrid.Infrastructure`](https://www.nuget.org/packages/NetOpenGrid.Infrastructure) | `AddNetOpenGrid()`, `MapNetOpenGrid()`, HTML renderer, embedded assets, CSV export, i18n. Pulls in `Application` and `Domain`. |
| [`NetOpenGrid.Persistence.EFCore`](https://www.nuget.org/packages/NetOpenGrid.Persistence.EFCore) | Data source over an EF Core `IQueryable`: filters, sorting, paging and value counts run in SQL. |
| [`NetOpenGrid.Persistence.Npgsql`](https://www.nuget.org/packages/NetOpenGrid.Persistence.Npgsql) | Data source over a raw Npgsql connection: the same work pushed down to PostgreSQL, for apps that do not use EF Core - RepoDb, Dapper or plain ADO.NET. You supply the SELECT. |
| [`NetOpenGrid.Application`](https://www.nuget.org/packages/NetOpenGrid.Application) | Builders, in-memory query engine, filter strategies and JSON mode. No ASP.NET Core dependency. |
| [`NetOpenGrid.Domain`](https://www.nuget.org/packages/NetOpenGrid.Domain) | Contracts and descriptors (columns, filters, sorting, paging, groups). Dependency-free. |

```bash
dotnet add package NetOpenGrid
# Optional, when your data source is EF Core:
dotnet add package NetOpenGrid.Persistence.EFCore
# ...or PostgreSQL without EF Core:
dotnet add package NetOpenGrid.Persistence.Npgsql
```

> Versioning: `MAJOR.MINOR` is controlled by `<VersionPrefix>` in [`Directory.Build.props`](Directory.Build.props);
> the workflow bumps `PATCH` automatically on every commit. Raise `MAJOR` or `MINOR` there on API changes.

---

## 🚀 Quickstart

> Reference project: [`samples/NetOpenGrid.Example`](samples/NetOpenGrid.Example)

**1. Register the grid** (options are validated and compiled **once**, at startup):

```csharp
using NetOpenGrid.Infrastructure;

builder.Services.AddNetOpenGrid()           // no config: JS + htmx + Alpine + theme, all embedded
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

**2. Map the endpoints** (shell + fragment + component assets, theme CSS included):

```csharp
app.MapNetOpenGrid();     // GET /netgrid/:id · /rows · /_netgrid/* (includes /css/netopengrid-{theme}.css)
```

**3. Open `http://localhost:PORT/netgrid/employees`.** That's it: the component serves its own
JS and CSS (Alpine + HTMX + the Tailwind theme embedded in the assembly) — no `wwwroot`, no
`UseStaticFiles()`. To host your own CSS instead of the embedded one, see
[Bringing your own CSS](#bringing-your-own-css) above.

---

## 🧩 Razor view (embedded grid)

Each grid is served as its own HTML document at `/netgrid/{id}`. To place it inside one of **your**
Razor views, point an `<iframe>` at it with `?embed=1`: the grid drops its standalone chrome (site header,
page background, max-width, padding) and renders transparent, so it blends into your layout.

| Why an iframe | |
|---|---|
| 🧱 Isolation | The grid's Tailwind theme (with its CSS reset) never touches your site's CSS, and yours never breaks the grid. |
| 🔗 Same origin | Your page can size the frame to its content, so the page scrolls instead of the frame. |
| 🔁 Deep links | Grid state lives in the query string; forward your page's query to the frame and `/products?sort=price:desc` just works. |
| ↗️ Row actions | Links rendered with `target="_top"` navigate the whole page, not the frame; `Event` actions reach your page through `postMessage` (same origin). |

Complete, runnable Razor Pages app (`dotnet new web` + these files). Open `http://localhost:PORT/products`.

**1. Packages**

```bash
dotnet add package NetOpenGrid
```

**2. Theme CSS.** The grid loads `{CssPath}/netopengrid-{theme}.css`. Copy the precompiled
`netopengrid-grid.css` (or `netopengrid-midnight.css`) from [`samples/NetOpenGrid.Example/wwwroot/css`](https://github.com/mchinchilla/NetOpenGrid/tree/main/samples/NetOpenGrid.Example/wwwroot/css)
into your `wwwroot/css/`, or compile `themes/*.css` with the Tailwind v4 CLI (see [Themes](#-themes-tailwind-v4)).

**3. `Program.cs`**: register Razor Pages and the grid, map both.

```csharp
using System.Globalization;
using System.Net;
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Domain.Columns;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

var usd = CultureInfo.GetCultureInfo("en-US");

builder.Services.AddNetOpenGrid()
    .AddGrid<Product>("products", grid => grid
        .WithTitle("Product catalog")
        .WithTheme("grid")                          // -> /css/netopengrid-grid.css
        .WithDefaultPageSize(10)
        .WithMinHeight("")                          // the iframe decides the height
        .EnableRowSelection(p => p.Sku)
        .AddColumn(p => p.Sku, c => c.Header("SKU").Pinned())
        .AddColumn(p => p.Name, c => c.Searchable())
        .AddColumn(p => p.Category)
        .AddColumn(p => p.Price, c => c.Align(ColumnAlign.End).Format(v => v.ToString("C2", usd)))
        .AddColumn(p => p.Stock, c => c.Align(ColumnAlign.End))
        .AddColumn(p => p.ReleasedOn, c => c.Format(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
        .AddColumn("status", p => p.Stock > 0, c => c.Header("Status").RawCellHtml(p => p.Stock > 0
            ? "<span class=\"badge badge-success\">In stock</span>"
            : "<span class=\"badge badge-muted\">Sold out</span>"))
        .AddColumn("open", p => p.Sku, c => c
            .Header("")                             // blank header: a row-action column
            .Sortable(false).Filterable(false).Searchable(false)
            .RawCellHtml(p => $"<a target=\"_top\" href=\"/products/{WebUtility.UrlEncode(p.Sku)}\">Open</a>")),
        (_, options) => new InMemoryGridDataSource<Product>(options, Catalog.Products));

var app = builder.Build();

app.UseStaticFiles();     // wwwroot/css: site.css + netopengrid-grid.css
app.MapNetOpenGrid();     // /netgrid/{id} (+ /rows, /values, /export) and /_netgrid/* assets
app.MapRazorPages();

app.Run();

public enum Category { Electronics, Home, Sports, Toys, Books }

public sealed record Product(string Sku, string Name, Category Category, decimal Price, int Stock, DateOnly ReleasedOn);

public static class Catalog
{
    public static readonly IReadOnlyList<Product> Products = [.. Enumerable.Range(1, 120).Select(i => new Product(
        Sku: $"SKU-{i:D4}",
        Name: $"Product {i}",
        Category: (Category)(i % 5),
        Price: 4.99m + i * 3.25m,
        Stock: i * 7 % 40,
        ReleasedOn: new DateOnly(2024, 1, 1).AddDays(i * 5)))];
}
```

**4. `Pages/_ViewImports.cshtml` and `Pages/_ViewStart.cshtml`**

```cshtml
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

```cshtml
@{ Layout = "_Layout"; }
```

**5. `Pages/Shared/_Layout.cshtml`**: your layout, plus the small script that fits every grid frame to its content.

```cshtml
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>@ViewData["Title"]</title>
    <link rel="stylesheet" href="~/css/site.css" asp-append-version="true">
</head>
<body>
    <main class="container">@RenderBody()</main>

    <script>
        // Grid frames are same-origin: size each one to its content so the page scrolls, not the frame.
        const fitted = new WeakSet();
        const fitGrid = (frame) => {
            const doc = frame.contentDocument;
            if (!doc?.body || doc.URL === "about:blank" || fitted.has(doc)) return;
            fitted.add(doc);
            // Measure <html>, not <body>: the grid's last margin collapses out of <body>.
            const fit = () => { frame.style.height = `${Math.ceil(doc.documentElement.getBoundingClientRect().height)}px`; };
            new ResizeObserver(fit).observe(doc.documentElement);
            fit();
        };
        for (const frame of document.querySelectorAll("iframe[data-netgrid]")) {
            frame.addEventListener("load", () => fitGrid(frame));
            if (frame.contentDocument?.readyState === "complete") fitGrid(frame);
        }
    </script>
</body>
</html>
```

**6. `Pages/Products.cshtml`**: the view that hosts the grid.

```cshtml
@page
@{
    ViewData["Title"] = "Products";

    // ?embed=1 drops the standalone chrome. The grid keeps its state in the query string, so
    // forwarding this page's query makes /products?sort=price:desc&groupby=category a deep link.
    var gridSrc = "/netgrid/products?embed=1"
        + (Request.QueryString.HasValue ? "&" + Request.QueryString.Value![1..] : "");
}

<h1>Products</h1>
<p>Any Razor markup can go around the grid.</p>

<iframe class="grid-frame" data-netgrid src="@gridSrc" title="Product catalog"></iframe>
```

**7. `Pages/Product.cshtml`**: the target of the grid's row-action column.

```cshtml
@page "/products/{sku}"
@{
    var product = Catalog.Products.FirstOrDefault(p => p.Sku == (string?)RouteData.Values["sku"]);
    ViewData["Title"] = product?.Name ?? "Not found";
}

<p><a href="/products">← Products</a></p>
<h1>@(product?.Name ?? "Product not found")</h1>
@if (product is not null)
{
    <p>@product.Sku · @product.Category · @product.Price.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-US"))</p>
}
```

**8. `wwwroot/css/site.css`**

```css
body { margin: 0; font-family: system-ui, sans-serif; }
@media (prefers-color-scheme: dark) { body { background: #0a0a0a; color: #f5f5f5; } }
.container { max-width: 80rem; margin: 0 auto; padding: 0 16px; }

.grid-frame {
  display: block;
  width: 100%;
  height: 640px;          /* first paint; the layout script then fits it to the grid */
  border: 0;
  color-scheme: normal;   /* must match the grid document, or dark pages get an opaque white frame */
}
```

Things to know:

- `color-scheme: normal` on the frame matters. When your page is in dark mode and the frame's color scheme
  differs from the grid document's, browsers paint the iframe opaque white.
- `WithMinHeight("")` turns off the grid's default ≈25-row minimum height, which is meant for standalone pages.
- The row-selection bar posts the selected keys to `POST /netgrid/{id}/export` (form field `ids`). That
  endpoint belongs to your app; the full sample shows one. The toolbar's download button uses the built-in
  `GET /netgrid/{id}/export`.
- In Razor, write `src="@gridSrc"` with the URL built in code. `src="/x?embed=1@query"` is **not** evaluated,
  because Razor reads `1@query` as an e-mail address.

The [full sample](samples/NetOpenGrid.Example) adds an EF Core grid over SQLite (opens grouped by country,
with deep links), a JSON-mode grid with the `midnight` theme, and i18n driven by `appsettings.json`.

---

## 🔄 Workflows

### Workflow 1 — Startup (build time, once)

```mermaid
sequenceDiagram
    participant App as Your app (Program.cs)
    participant OB as GridOptionsBuilder<T>
    participant DI as Keyed DI
    App->>OB: AddColumn(field, selector, configure)
    OB->>OB: validates (id, duplicates, paging)
    OB->>OB: compiles ISortStrategy / IFilterStrategy / formatter
    App->>DI: AddGrid(...) → GridRuntime<T> singleton
    Note over DI: from now on: zero reflection,<br/>zero per-request compilation
```

### Workflow 2 — User interaction (the hot path)

```mermaid
sequenceDiagram
    autonumber
    participant U as User
    participant AL as Alpine (netgrid)
    participant HX as HTMX
    participant EP as GET /netgrid/:id/rows
    participant RT as GridRuntime<T>
    participant DS as IGridDataSource<T>

    U->>AL: sort / filter / search / page
    AL->>AL: updates state + URL (replaceState → deep link)
    AL->>HX: htmx.ajax("GET", rows?query)
    HX->>EP: page · pageSize · sort · filter · q
    EP->>RT: RenderRowsAsync(values)
    RT->>RT: GridRequestParser (whitelist + clamp + warnings)
    RT->>DS: LoadAsync(GridQuery) ← the only async boundary
    DS-->>RT: PageResult<T> (filter+sort+slice)
    RT-->>HX: <tbody> + X-Grid-* headers
    HX->>AL: swap innerHTML + afterRequest
    AL->>AL: meta (total/pages) from headers
```

### Workflow 3 — Filter popover (Excel-style)

```mermaid
flowchart TD
    A["🖱️ funnel click"] --> B["popover inside the &lt;th&gt;"]
    B --> C{"fits inside the card?"}
    C -->|"yes"| D["opens inward:<br/>first column → right · rest → left"]
    C -->|"no (clipped)"| E["auto-flip:<br/>left↔right · bottom→top"]
    D --> F["operator + value (per column type)"]
    E --> F
    F -->|"Apply"| G["refresh() → Workflow 2"]
    F -->|"Clear"| G
```

### Workflow 4 — Themes (Tailwind v4)

```mermaid
flowchart LR
    T["themes/*.css<br/>@import tailwindcss · @source ../src"] --> CLI["tailwindcss -i -o --minify"]
    CLI --> OUT["src/NetOpenGrid.Infrastructure/Assets/css/<br/>netopengrid-{theme}.css → EmbeddedResource"]
    OUT --> SHELL["shell: link rel=stylesheet per options.Theme"]
    CLI -. "detects classes inside the renderer's C#" .- SRC["GridHtmlRenderer.cs"]
```

### Workflow 5 — Nested grouping

```mermaid
flowchart TD
    A["Picker: Group by department, city"] --> B["groupby=department,city"]
    B --> C["Server: filters + sorts + groups<br/>pages = groups (not rows)"]
    C --> D{"group in expand=?"}
    D -->|"no"| E["collapsed header<br/>value + count"]
    D -->|"yes"| F{"last level?"}
    F -->|"no"| G["next-level sub-headers"]
    F -->|"sí"| H["group rows"]
    E --> I["chevron click → toggle path in expand= → refresh"]
    G --> I
    H --> I
```

---

## 🗄️ EF Core (SQL push-down)

`EFCoreGridDataSource<T>` composes `Where` / `OrderBy` / `Skip` / `Take` on your `IQueryable<T>`
using **the same columns, operators and parsing rules** as the in-memory grid.

```csharp
builder.Services.AddDbContext<AppDb>(o => o.UseSqlite(cs));

services.AddNetOpenGrid().AddGrid<Employee>("employees",
    options => options
        .AddColumn(e => e.FullName, c => c.Searchable())   // ← use the Expression overloads
        .AddColumn(e => e.Salary)
        .AddColumn(e => e.HiredOn),
    (sp, opts) => new EFCoreGridDataSource<Employee>(
        opts,
        sp,
        scoped => scoped.GetRequiredService<AppDb>().Set<Employee>().AsNoTracking().OrderBy(e => e.Id)));
```

Important rules:

| Rule | Detail |
|---|---|
| 🧾 **Expression columns** | For push-down, define columns with the `AddColumn(e => e.Prop, …)` overloads. If a sortable/filterable/searchable column only has a `Func`, construction fails with an error listing the offending fields. |
| 🔤 **Strings via `LIKE`** | `contains/starts-with/ends-with/equals` translate to `EF.Functions.Like` (case-insensitive under default collations; wildcards escaped). |
| 🔢 **Values parsed once** | The filter literal is parsed with the **same** invariant parser as the in-memory pipeline and embedded as a constant in the tree. |
| 🧵 **Scoped DbContext** | The data source creates an `IServiceScope` per request — a scoped `DbContext` works as-is. |
| ↩️ **Stable ordering** | SQL does not guarantee stable ties: add a sort on a unique column for deterministic paging. |

---

## 📋 Excel-style filters (per-value counts)

Text, enum and boolean columns show a **checklist of distinct values with counts** in the popover:

- Counts respect the current context (search + other filters) and **ignore the column's own filter** — classic Excel semantics.
- In-list search, *Select all* and a truncation notice (`FilterValuesLimit`, default 200).
- Applying generates an `in` filter:

```
filter=category:in:["Books","Toys"]
```

- Values endpoint (used by the popover, handy for your own UI too):

```
GET /netgrid/:id/values?field=category&<context>
→ {"field":"category","totalDistinct":5,"limit":200,"values":[{"value":"Books","count":28},…]}
```

Implemented across all three engines: in-memory (snapshot grouping), JSON (`JsonElement`) and EF Core (`GROUP BY` translated to SQL). Numeric/date columns keep the operator popover.

---

## 📌 Pinned, reorderable, resizable & hideable columns

```csharp
.AddColumn("sku", p => p.Sku, c => c.Header("SKU").Pinned())
```

- **Pinned** (left or right): `position: sticky` on `th`/`td`; the column stays visible during horizontal scroll
  (the selection column is always pinned). `.Pinned()` pins left, `.PinnedRight()` pins right, and
  `WithRowActions(a => a.Pinned()...)` pins the actions column to the right. Each header's pin button cycles
  **unpinned → left → right → unpinned** (the icon mirrors on the right and the label names the next step).
  Pinned columns gather at their edge: left + the rest + right. State persists in `localStorage`, including
  unpinning what the server pinned.
- **Reorderable**: drag a `th` to reorder. Order persists in `localStorage` per grid and travels to the server as `cols=field1,field2,…`; the renderer validates against the whitelist (unknown fields are ignored, unlisted ones are appended in default order).
- **Resizable**: drag a header's right edge; double-click fits it to the visible page's content, and `Alt+←` /
  `Alt+→` on a header button changes it by 16 px. The first resize freezes every column at its current width
  (`table-layout: fixed`, nothing jumps); what does not fit ends in "…". The minimum is what the header buttons
  need. Widths persist per grid in `localStorage`. `.WithColumnResize(false)` turns it off.
- **Show/hide**: the toolbar's columns button opens a list with one checkbox per column and "Show all" (the last
  visible column cannot be unchecked). The choice travels to the server as `hide=field1,field2,…`, so rows,
  totals, groups and the **CSV export** leave those columns out; it persists in `localStorage` and in the URL.
  `.WithColumnChooser(false)` turns it off.

## 🖱️ Clickable rows & row actions

```csharp
.EnableRowSelection(p => p.Sku)          // or .WithRowKey(p => p.Sku) if you do not want selection
.WithRowLink(p => $"/products/{p.Sku}", target: "_top")
.WithRowActions(a => a
    .Link("Details", p => $"/products/{p.Sku}", target: "_top")
    .Event("archive", "Archive", RowActionStyle.Danger, visible: p => p.Available))
```

- **Clickable row** (`WithRowLink`): a click anywhere on the row navigates. Clicks on the row's own controls
  (checkbox, links, buttons) and text selection are ignored; `Ctrl`/`⌘`+click and middle click open a new tab. From
  the keyboard, `Enter` on a cell without a control opens the link. `target: "_top"` leaves the iframe.
- **Actions** (`WithRowActions`): a trailing column with each row's links and buttons.
  - `Link(label, href, target?)` → a plain `<a href>`.
  - `Event(name, label)` → a button that raises `netgrid:action` with `{ grid, action, key }` on the grid root
    (it bubbles up to `document`). When the grid lives in an iframe, the same payload is posted **only** to a
    same-origin parent. The component runs nothing on the server: your page decides (call your API, open a modal…).
  - `style: RowActionStyle.Danger` paints them red; `visible:` hides them per row; `.Header("…")` changes the title.
  - `Event` needs a row key (`WithRowKey` or `EnableRowSelection`); without one the grid fails at configuration.
- **Refresh** after an action: `document.dispatchEvent(new CustomEvent('netgrid:refresh', { detail: { grid: 'products' } }))`,
  or from the host page: `frame.contentWindow.postMessage({ type: 'netgrid:refresh', grid: 'products' }, location.origin)`.
- **Safe URLs:** only relative, `http(s)`, `mailto` and `tel` URLs are rendered; `javascript:`, `data:` and
  `vbscript:` are dropped (for the row link and the actions).
- When a cell holds several controls, `Enter` steps into it, `←`/`→` walk them and `Escape` returns to the cell.

```js
// Host page with the grid in an iframe (see samples/NetOpenGrid.Example/Pages/Index.cshtml)
window.addEventListener("message", (e) => {
    if (e.origin !== location.origin || e.data?.type !== "netgrid:action") return;
    // e.data.action === "archive", e.data.key === "SKU-0001"
});
```

---

## 📤 Server-side export (CSV & Excel)

`GET /netgrid/:id/export?<context>` downloads the **full dataset** (all pages) with the current
filter, search, sort, column order (`cols`) and hidden columns (`hide`). `format=csv` (default) or `format=xlsx`.

```csharp
// zero config: MapNetOpenGrid() already exposes the route
// GET /netgrid/employees/export?filter=department:equals:Design&sort=id              → employees.csv
// GET /netgrid/employees/export?format=xlsx&filter=department:equals:Design&sort=id  → employees.xlsx
.WithExportFormats(GridExportFormats.Csv | GridExportFormats.Xlsx)   // default: both; None removes the export
.AddColumn(p => p.Price, c => c.ExcelFormat("\"$\"#,##0.00"))         // Excel number format
```

- The toolbar ships a download button (a CSV / Excel menu when both formats are on) that exports the current view.
- **Streaming:** iterates `MaxPageSize` pages and writes them straight to the response; memory stays bounded whatever the export size.
- **Cap:** `.WithMaxExportRows(n)` (default `100_000`; xlsx is also capped at Excel's 1,048,575 rows). Larger results get a `422` before anything is written.

**CSV** (RFC-4180)
- Cell values use the column formatters (`Format`); `RawCellHtml` is ignored for safety.
- **UTF-8 with BOM**, so Excel opens accented characters correctly.
- **Formula injection:** cells starting with `=` `+` `-` `@` (or tab/CR) get a leading `'`; numbers such as `-12.5` are left alone.

**Excel (.xlsx)** — no dependencies: the SpreadsheetML package is written directly, streaming.
- **Typed cells:** numbers as numbers, dates as Excel dates (`yyyy-mm-dd`, with the time when there is one), booleans as
  TRUE/FALSE; enums and text as text. Text goes out as *inline strings*, which Excel never evaluates as formulas.
- `.ExcelFormat("...")` per column for the number format (currency, percent…); without it, General.
- Bold, frozen header row, autofilter, widths estimated from the first page, sheet named after the grid title.
- Checked with the Open XML SDK validator (0 errors) and read back with openpyxl.

---

## 🌐 i18n

Every client-facing label (buttons, aria-labels, operators, counters, ranges) flows through
`NetOpenGridLocalizationOptions`: English defaults, a bundled **Spanish** preset, and per-key overrides.

```csharp
builder.Services.AddNetOpenGrid(
    o => { o.AssetPrefix = "/_netgrid"; },
    loc => loc.UseCulture("es")                       // bundled preset
              .Set("filter.apply", "Filtrar"));       // fine-grained override
```

- The shell injects `__NETGRID__.locale` with the effective dictionary: Alpine renders chips,
  counters and ranges with the exact same strings (EN fallbacks embedded in the JS).
- ~39 keys: `search.*`, `pager.*`, `records.*`, `range.*`, `filter.*`, `select.*`, `chips.*`, `ops.*`, `pin.aria`, `theme.aria`, `export.aria`.
- The empty message remains `WithEmptyMessage(...)` (per grid).

---

## 🗂️ Column grouping (nested)

![Orders grid over EF Core, grouped by country with United States expanded](docs/images/orders-grouped.png)

`groupby=field1,field2,…` (up to 3 levels) + `expand=` to open groups. **Groups are paged** (not rows); expanding a group reveals its sub-groups or rows (DevEx style — each level expands separately). State lives in the URL → deep links with groups open.

```
GET /netgrid/:id/rows?groupby=department,city&expand=department=Design|city=Lima
```

- **Group panel** above the table: drag a column header onto the panel to group by it; drag a chip onto
  another to reorder the levels; each chip's ✕ removes that level. `.WithGroupPanel(false)` removes it and
  leaves plain chips.
- The toolbar's "Group by..." picker stays as the keyboard path.
- Group headers with chevron, row counts and per-level indentation; null values → a "(Blanks)" bucket.
- Group order follows the grouped column's sort; remaining sorts order rows inside each group.
- Engines: in-memory and JSON group in memory; **EF Core** pushes filter+sort down to SQL and builds the tree over the matching rows.

## ➕ Aggregates (SUM · AVG · MIN · MAX)

Each numeric column declares its functions and the grid decides which rows show them. They are computed
over **the whole filtered set** (every page), not just the visible page.

```csharp
.WithAggregateRows(GridAggregateRows.Footer | GridAggregateRows.GroupHeader | GridAggregateRows.GroupFooter)
.AddColumn(p => p.Price, c => c
    .Format(v => v.ToString("C2", usd))
    .Aggregate(GridAggregate.Avg | GridAggregate.Min | GridAggregate.Max))
.AddColumn(p => p.Stock, c => c.Aggregate(GridAggregate.Sum | GridAggregate.Avg))
```

| `GridAggregateRows` | Where |
|---|---|
| `Header` | "Total" row right below the column headers |
| `Footer` | "Total" row after the last row |
| `GroupHeader` | Inside each group's row, next to its name and count (visible while collapsed) |
| `GroupFooter` | "Subtotal …" row closing each expanded group |

- Default: `Footer | GroupHeader`. `GridAggregateRows.None` turns them off; `All` shows every one.
- Values line up under their column; the leading columns without aggregates merge into the label cell.
- **Formatting:** reuses the column's `.Format(...)` (`$1,234.50`). `AVG` over integers shows up to 2 decimals.
  `.AggregateFormat((fn, value) => ...)` replaces it.
- **Nulls:** skipped, as in SQL (`AVG` divides by the non-null values). An empty set shows no totals.
- **Engines:** in-memory and JSON compute in memory; **EF Core** and **Npgsql** push the totals down to a
  single `SELECT SUM(…), AVG(…), MIN(…), MAX(…)`. Group subtotals are computed while the group tree is built.
- Numeric columns only: `.Aggregate(...)` on any other type fails when the grid is configured. EF Core needs
  the expression selector for the column.
- Localized labels: `agg.sum`, `agg.avg`, `agg.min`, `agg.max`, `agg.total`, `agg.subtotal`.

## ⌨️ Keyboard & accessibility

The table body is **a single Tab stop** (roving tabindex): row links, checkboxes and chevrons leave the Tab
order and are activated from their cell.

| Key | Action |
|---|---|
| `←` `→` `↑` `↓` | Move between cells (colspan-aware, breakpoint-hidden columns skipped) |
| `Home` / `End` | First / last cell of the row |
| `Ctrl+Home` / `Ctrl+End` | First / last cell of the page |
| `PageUp` / `PageDown` | Previous / next page, keeping the column |
| `Alt+←` / `Alt+→` on a header | Narrows / widens the column by 16 px |
| `Enter` | Activates the cell's control (link, checkbox); on a group row, expands or collapses it; on a clickable row, opens its link; with several controls, steps into the cell |
| `Space` | Selects the row; on a group row, expands or collapses it |
| `↑` on the first row | Moves up to that column's sort button; `Enter` sorts, `Shift+Enter` adds to the multi-sort |
| `↓` on a header | Moves down to the first row, same column |

- When new rows arrive (sort, page, group) focus returns to the same row and column.
- `aria-sort` on every sortable header: `ascending` / `descending` on the primary sort, `other` on the
  secondary ones of a multi-sort, `none` elsewhere. The ▲▼ arrow is `aria-hidden`.
- The table keeps its native semantics (no `role="grid"`): screen readers keep their own table navigation,
  and the header buttons (sort, filter, pin) stay reachable with `Tab`.

## 🔖 Saved views

A view is the grid state with a name: sort, filters, search, grouping, column order, hidden columns and page
size (the same state the URL already carries). The toolbar's views button has three sections:

- **Default view**: back to the grid as configured.
- **Predefined**: defined on the server, everyone sees them.
- **My views**: each user saves the current view under a name (field at the bottom of the menu); they live in
  their browser (`localStorage`), a repeated name replaces the old one, and ✕ deletes them.

```csharp
.AddView("Delivered, biggest first", "filter=status:equals:delivered&sort=total:desc")
.AddView("Pending by country", "filter=status:equals:pending&groupby=country")
.WithSavedViews(false)   // removes "My views"; predefined ones stay
```

- Applying a view resets all state first, so nothing from the previous one leaks in (a filter, a hidden column).
- The active view gets a ✓ and the button is highlighted. Page and page size do not decide which view is active.
- Predefined views are **validated when the grid is built**: names (1-60 characters, unique), allowed parameters
  (`sort · filter · q · groupby · expand · cols · hide · pageSize`) and every field being a column. A mistake
  throws `GridConfigurationException` at startup instead of silently showing something else.
- Widths and pins stay per browser, not per view.

## 📜 Virtual scroll

For long lists, instead of the pager: a fixed-height scroll area where rows arrive from the server in blocks as
you scroll down, and only the blocks near the viewport stay in the DOM.

```csharp
.WithVirtualScroll(blockSize: 100, height: "70vh")
```

- Each block is a page of the usual endpoint (`/rows?page=N&pageSize=blockSize`), so it works the same with
  in-memory, JSON, EF Core and Npgsql. The page size becomes the block size.
- The DOM keeps the visible block and one on each side; the rest are spacer rows with each block's measured
  height (estimated until measured), so the scrollbar reflects the total. Far blocks are dropped from memory too.
- The header stays on top. Top totals only open the first block and bottom totals only close the last one.
- Changing sort, filters or search goes back to the top and cancels blocks in flight.
- Keyboard: arrows cross blocks; `PageUp`/`PageDown` move one screen and `Ctrl+Home`/`Ctrl+End` go to the
  first/last row of the whole set, loading what is needed.
- Grouped views page again (expandable groups have no uniform heights).
- The container has `overflow-anchor: none`: without it Chrome "fixes" the scroll when blocks change and jumps to the end.

---

## 📚 Configuration reference

### `GridOptionsBuilder<T>`

| Method | Default | Description |
|---|---|---|
| `.WithId(id)` | — | **Required.** `^[A-Za-z][A-Za-z0-9_-]{0,63}$` |
| `.WithTitle / .WithSubtitle` | `"Data grid"` | Shell header |
| `.WithDefaultPageSize(n)` | `25` | Clamped to `[1, MaxPageSize]` |
| `.WithMaxPageSize(n)` | `500` | Hard server ceiling |
| `.WithPageSizeChoices([...])` | `[10,25,50,100]` | Sorted and deduplicated |
| `.WithDebounce(ms)` | `300` | Global search |
| `.WithTheme(name)` | `"grid"` | Compiles to `netopengrid-{name}.css` |
| `.WithMinHeight(css)` | `"64rem"` | ≈25 rows; prevents collapse while filtering. `""` disables it |
| `.WithEmptyMessage(msg)` | `"No records found."` | Centered empty state |
| `.WithMaxExportRows(n)` | `100000` | Export cap (CSV and xlsx); above it the endpoint returns `422` |
| `.AddView(name, query)` | — | Predefined view (validated at build) |
| `.WithSavedViews(bool)` | `true` | Users save their own views |
| `.WithExportFormats(formats)` | `Csv \| Xlsx` | Formats offered by the export button and endpoint |
| `.WithColumnResize(bool)` | `true` | Column resize handles |
| `.WithColumnChooser(bool)` | `true` | Show/hide columns menu |
| `.WithVirtualScroll(blockSize, height)` | off | Block-based virtual scroll instead of the pager |
| `.WithGroupPanel(bool)` | `true` | Panel to group by dragging headers |
| `.WithAggregateRows(rows)` | `Footer \| GroupHeader` | Rows that show the aggregates |
| `.EnableRowSelection(keyFn)` | off | Floating bar + export; `keyFn` must be stable and unique |
| `.WithRowKey(keyFn)` | — | Row key without turning selection on (used by `Event` actions) |
| `.WithRowLink(hrefFn, target?)` | — | The whole row is clickable |
| `.WithRowActions(a => ...)` | — | Actions column: `.Link(...)`, `.Event(...)`, `.Header(...)` |
| `.WithNavLinks(...)` | `[]` | Shell navigation |

### `GridColumnBuilder<T, TKey>`

| Method | Default | Description |
|---|---|---|
| `.Header(text)` | humanized name | `birthDate` → "Birth date" |
| `.Selector(Func)` / `.Selector(Expression)` | — | The expression is compiled **once** here (and kept for EF push-down) |
| `.Format(Func<TKey,string?>)` | `DefaultValueFormatter` | Invariant culture, no boxing |
| `.RawCellHtml(Func<T,string?>)` | — | ⚠️ Trusted HTML (badges); everything else is always encoded |
| `.Sortable / .Filterable / .Searchable` | `true/true/auto` | `auto`: search only on text columns |
| `.AllowedOps(FilterOpSet)` | `All` | **Intersected** with what the type supports |
| `.Comparer / .Parser / .SearchMatch` | built-ins | Reflection-free overrides |
| `.Align / .WidthCss / .Visible` | `Start/–/true` | Presentation |
| `.Pinned() / .PinnedRight()` | — | Pins the column to the left / right edge |
| `.DataType(...)` | inferred | `Text · Numeric · Date · Boolean · Enum · Unknown` |
| `.ExcelFormat(numFmt)` | General | The column's number format in xlsx exports |
| `.Aggregate(GridAggregate)` | `None` | `Sum \| Avg \| Min \| Max`; numeric columns only |
| `.AggregateFormat(Func<GridAggregate,decimal,string?>)` | the column's `Format` | Custom display for the aggregates |

### Assets (served by the component, embedded in the assembly)

| Option | Default | Description |
|---|---|---|
| `o.AssetPrefix` | `/_netgrid` | Prefix for `netopengrid.js` + `vendor/*` + `css/netopengrid-{theme}.css` |
| `o.CssPath` | `null` | When set, serves **your** theme css from that path instead of the embedded theme |
| `o.CssFilePrefix` | `netopengrid-` | Naming for your own css; applies only when `CssPath` is set |

Caching: `Cache-Control: immutable` + `?v={sha256-12}` — zero copying into `wwwroot` with the default embedded theme.

---

## 🎛️ Column types & operators

Popover operators are **intersected** while building the options:
`what you ask for ∩ type preset ∩ actual strategy capability`. The UI can never
request an operator the engine cannot execute.

| Type (TKey) | Preset | Operators |
|---|---|---|
| `string` | Text | `equals` · `not-equals` · `contains` · `starts-with` · `ends-with` · `is-empty` · `is-not-empty` |
| numerics, `DateOnly`, `DateTime`, `DateTimeOffset` | Numeric | `equals` · `not-equals` · `gt` · `gte` · `lt` · `lte` |
| `enum` | Equality | `equals` · `not-equals` |
| `bool` | Equality | `equals` · `not-equals` |
| others (classes) | Equality | `equals` · `not-equals` (textual comparison) |

Compact symbols accepted in the query: `=` `!=` `>` `>=` `<` `<=` `~` (contains).

---

## 📡 Client ↔ server contract

| Param | Example | Notes |
|---|---|---|
| `page` | `2` | 1-based |
| `pageSize` | `25` | clamped to `MaxPageSize` |
| `sort` | `salary:desc` (repeatable) | shift-click = stable multi-sort |
| `filter` | `city:contains:li` · `amount:>=100` · `status:is-empty` | per-column whitelist |
| `q` | `nico` | global search (OR across `Searchable` columns) |
| `groupby` | `department,city` | nested grouping (max 3 levels); groups are paged |
| `expand` | `department=Design\|city=Lima` | expanded group paths (URL-encoded values) |
| `cols` | `email,fullName` | column order (drag & drop; validated, missing ones appended) |
| `format` | `xlsx` | `/export` only: `csv` (default) or `xlsx` |
| `hide` | `email,city` | hidden columns (columns menu; validated, never hides them all) |

**Response** (`<tbody>` fragment + headers):

| Header | Meaning |
|---|---|
| `X-Grid-Total` | Total after filtering (not paging) |
| `X-Grid-Page` / `X-Grid-Page-Size` | Served page (normalized) |
| `X-Grid-Page-Count` | Total pages |
| `Cache-Control: no-store` | Always fresh |

🔗 **Free deep links**: state lives in the URL — share
`?sort=salary:desc&filter=department:equals:design` and the grid opens exactly like that
(the shell server-renders that view and Alpine seeds it).

---

## 🧊 JSON mode

Same pipeline, `JsonElement` rows. No reflection: "selectors" are property names.

```csharp
var options = new JsonGridOptionsBuilder()
    .WithId("orders")
    .AddColumn("customer", c => c.Searchable())
    .AddColumn("amount", c => c.AllowedOps(FilterOpSet.Numeric))
    .Build();

services.AddNetOpenGrid().AddGrid(options, (_, o) => new JsonGridDataSource(o, json));
// also: new JsonGridDataSource(o, streamLoader) · JsonGridDataSource.FromFileAsync(...)
```

Missing properties behave like `null` (sorts lowest, `is-empty` ✓).

---

## 🎨 Themes (Tailwind v4)

```bash
./tools/build-themes.sh              # compiles themes/*.css → Infrastructure/Assets/css
./tools/build-themes.sh --strict     # fails if the tailwindcss CLI is not on PATH
```

- The output is compiled **into the assembly** as an `EmbeddedResource` and served by
  `MapNetOpenGrid` at `{AssetPrefix}/css/netopengrid-{theme}.css`. Apps consuming the
  package need neither `wwwroot` nor Tailwind. `tools/pack.sh` always passes `--strict`,
  so a package without compiled CSS can never ship.
- Themes use `@source "../src"`: **Tailwind scans the renderer's C#** and detects the classes the
  server emits — no dead CSS, no manual safelist.
- `@custom-variant dark (&:where(.dark, .dark *))` + toggle persisted in `localStorage`
  (inline anti-FOUC script in the shell).
- Create your own by copying `themes/grid.css` and tweaking `@theme { --color-brand-*: … }`.

---

## 🏎️ Performance

| Decision | Why |
|---|---|
| Precompiled delegates per column | Zero reflection/`Compile` on the hot path |
| Closed-parser table (`int`, `decimal`, `DateOnly`, `Guid`…) | No `MakeGenericType`, no `Activator` |
| Stable multi-sort with decorated index array | 1 allocation, deterministic ties, no LINQ chains |
| Renderer: pooled `StringBuilder` + direct `HtmlEncoder` | Zero intermediate strings per cell |
| Fragment = `<tbody>` only | The smallest possible payload per interaction |
| `ValueTask` only at the data boundary | Async where it matters, CPU-bound work sync by design |
| Embedded assets with `?v=hash` | 1 request, immutable cache, no build steps |
| EF: literals parsed once, embedded as constants | SQL push-down with the same pipeline rules |

---

## 🗂️ Project layout

```
NetOpenGrid.slnx
├─ src/
│  ├─ NetOpenGrid.Domain/              💎 pure model (queries, columns, strategies, contracts)
│  ├─ NetOpenGrid.Application/         ⚙️ builders, parser, pipeline, engine, JSON mode
│  ├─ NetOpenGrid.Infrastructure/      🛠️ keyed-DI runtime, renderer, endpoints, embedded assets
│  ├─ NetOpenGrid.Persistence.EFCore/  🗄️ EFCoreGridDataSource<T> (SQL push-down)
│  └─ NetOpenGrid.Host/                🖥️ demo (employees <T> + orders JSON + CSV export)
├─ samples/NetOpenGrid.Example/        📦 Razor Pages app embedding 3 grids (in-memory, EF Core, JSON)
├─ themes/                             🎨 Tailwind v4 sources (grid, midnight)
├─ tests/                              ✅ Domain · Application · EFCore · Integration
├─ tools/                              🔧 build-themes.sh/.cmd · fetch-vendor.sh · check-js.sh
```

---

## ✅ Testing

```bash
dotnet test
```

**116 tests**: Domain (20) · Application (56) · Integration (27) · EFCore (13).

- **Domain**: paging, operators (incl. `In`), results.
- **Application**: builders (validation, pin), parser (whitelist/clamps/symbols/JSON `in`), pipeline
  (filter+sort+slice, stability, nulls-first), per-type strategies, `GridGrouper` (nested/expand/
  group-paging/blank bucket), full JSON mode.
- **EFCore**: SQL translation on in-memory SQLite (per-type filters incl. `In`, OR search, sort, paging,
  `GROUP BY` value-counts, grouping) and **result parity** against the in-memory pipeline.
- **Integration**: real HTTP over `WebApplicationFactory` — sorting, filters (operator + `in`), search,
  rendered grouping, meta headers, deep-links (shell + `cols`), 404s, CSV export, i18n (ES shell),
  embedded assets and JS-runtime health (anti-regression).

---

## 🗺️ Roadmap

### ✅ Done

- [x] `EFCoreGridDataSource<T>`: filter/sort push-down to SQL reusing the same strategies
- [x] `NpgsqlGridDataSource<T>`: PostgreSQL push-down via `GridSqlMap` / `GridSqlBuilder`
- [x] Excel-style filters with per-value counts
- [x] Pinned and reorderable columns
- [x] Client label i18n
- [x] Column grouping (nested up to 3 levels, URL-driven expand/collapse)
- [x] Generic server-side export (CSV) as part of the component

### 🐞 Fixes & hardening

- [x] CSV export: neutralize formula injection (cells starting with `=` `+` `-` `@` tab/CR)
- [x] CSV export: UTF-8 BOM so Excel opens accented characters correctly
- [x] CSV export: stream to `Response.Body` + configurable row cap (`MaxExportRows`)
- [x] Client: each grid cancels its own in-flight request (`AbortController`); several grids on one page no longer clobber each other
- [x] Client: dragging columns did not reorder anything, and with a saved order the header did not follow the rows
- [x] Client: the pin button saved the state but did not stick the cells until the next rows reload
- [x] Dark theme: a pinned column's header was translucent and the columns underneath showed through
- [ ] Client: browser history (`pushState` + `popstate`) so "back" restores the view
- [x] A11y: `aria-sort` on sortable headers
- [x] A11y: keyboard navigation across cells and pages

### ✨ Features

- [x] Per-column aggregates (`SUM` · `AVG` · `MIN` · `MAX`), pushed down to SQL
  - [x] In the table header and the table footer
  - [x] In every group: group header and group footer
- [x] Column resizing (widths persisted in `localStorage`)
- [x] Show/hide columns menu
- [x] Row actions and clickable rows (`WithRowLink` · `WithRowActions`)
- [x] Streaming Excel (xlsx) export
- [x] Saved views (named URL state): predefined + per user
- [x] Drag columns into the group panel (and reorder levels by dragging chips)
- [x] Right-side pinning (columns and the actions column)
- [x] Row virtualization (block-based virtual scroll)

### 🧪 Quality

- [ ] End-to-end tests for the JS client with Playwright over `samples/NetOpenGrid.Example`
- [ ] BenchmarkDotNet benchmarks (hot path, rendering, in-memory pipeline)
- [ ] `NpgsqlGridDataSource` tests against real PostgreSQL (Testcontainers)

---

## 📄 License

[MIT](LICENSE) — use, modify and distribute freely; attribution lives in the file.

---

<div align="center">

**NetOpenGrid** — *server-first grids for .NET 10*

`dotnet run --project samples/NetOpenGrid.Example` → `http://localhost:5188`

[![Docs: ES](https://img.shields.io/badge/docs-ES-9CA3AF?style=flat-square)](README.md)
[![Docs: EN](https://img.shields.io/badge/docs-EN-4F46E5?style=flat-square)](README.en.md)

</div>
