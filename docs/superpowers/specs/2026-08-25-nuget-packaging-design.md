# NetOpenGrid — NuGet Packaging Design

- **Date:** 2026-08-25
- **Status:** Approved, pending implementation plan
- **Scope:** Make NetOpenGrid consumable from other projects as NuGet packages, distributed initially through a local folder feed.

## Problem

NetOpenGrid is currently consumable only by `ProjectReference`. Two things block package distribution:

1. **No packaging metadata.** No `PackageId`, version, license, or authorship on any project. `Host` and the test projects would pack by accident if packing were enabled repo-wide.

2. **Theme CSS cannot reach a consumer.** `tools/build-themes.{sh,cmd}` compiles `themes/*.css` with the Tailwind CLI directly into `src/NetOpenGrid.Host/wwwroot/css` and `samples/NetOpenGrid.Example/wwwroot/css`. `GridHtmlRenderer.AppendDocumentStart` then emits `<link rel="stylesheet" href="/css/netopengrid-{theme}.css">`, resolved against the *host app's* static files. A package consumer receives no stylesheet and renders an unstyled grid.

   Consumers cannot practically rebuild the CSS themselves either: `themes/*.css` uses `@source "../src"`, so Tailwind scans the renderer's **C# source** for utility class names. From a `.nupkg` that source is not present.

The JavaScript side is already package-ready — `netopengrid.js`, `htmx.min.js` and `alpine.min.js` are `EmbeddedResource`s in `NetOpenGrid.Infrastructure`, served by `MapNetOpenGrid` from `EmbeddedGridAssets`. The CSS design below follows that existing, proven mechanism rather than inventing a second one.

## Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Granularity | Four packages, layered | Honest layering; `Microsoft.EntityFrameworkCore` stays opt-in for non-EF consumers |
| Package IDs | Renamed; bare `NetOpenGrid` is the install target | The memorable ID names what people install, not an internal layer |
| Assembly names / namespaces | **Unchanged** | The rename is `PackageId` only: zero source churn, no break for existing `ProjectReference` users |
| CSS delivery | Embedded resource, served by `MapNetOpenGrid` | Matches the JS mechanism; consumer needs no build tooling, no static-file middleware, no `wwwroot` copying |
| Distribution | Local folder feed (`./artifacts`), no CI | Fastest path to real use; nuget.org / GitHub Packages stays a later, separate decision |
| Versioning | `0.1.0`, lockstep, `-dev.N` suffix for iteration | One property to bump; distinct suffixed versions never collide with NuGet's id+version cache |

## 1. Package layout

| Project | PackageId | Packable | Package dependencies |
|---|---|---|---|
| `src/NetOpenGrid.Domain` | `NetOpenGrid.Abstractions` | yes | — |
| `src/NetOpenGrid.Application` | `NetOpenGrid.Core` | yes | `NetOpenGrid.Abstractions` |
| `src/NetOpenGrid.Infrastructure` | `NetOpenGrid` | yes | `NetOpenGrid.Core` |
| `src/NetOpenGrid.Persistence.EFCore` | `NetOpenGrid.EntityFrameworkCore` | yes | `NetOpenGrid.Core`, `Microsoft.EntityFrameworkCore` |
| `src/NetOpenGrid.Host` | — | **no** | demo app |
| `samples/NetOpenGrid.Example` | — | **no** | demo app |
| `tests/**` | — | **no** | tests |

Existing `ProjectReference`s become correctly-versioned package dependencies automatically during `dotnet pack`. No hand-written `.nuspec` is needed.

`NetOpenGrid.Infrastructure` currently references both `Application` and `Domain`; `Persistence.EFCore` does the same. The `Domain` reference is transitively implied in both cases. Dropping the redundant reference keeps the generated dependency graph matching the intended layering. This is optional cleanup, not required for correctness.

### Metadata placement

- **`Directory.Build.props` (root)** — add `<VersionPrefix>0.1.0</VersionPrefix>` and `<IsPackable>false</IsPackable>` as the repo-wide default. Every project is non-packable unless it opts in.

  It must be `VersionPrefix`, not `Version`: `dotnet pack --version-suffix` is **silently ignored** when `Version` is set explicitly, and composes only with `VersionPrefix`. Setting `Version` here would make the `-dev.N` iteration workflow in §3 fail quietly, producing `0.1.0` every time.
- **`src/Directory.Build.props` (new)** — imports the parent, sets `<IsPackable>true</IsPackable>` and the shared package metadata:
  - `Authors`, `Company`, `Product`, `Description` (per-project `Description` overrides where useful)
  - `PackageLicenseExpression=MIT`, matching the existing `LICENSE`
  - `PackageProjectUrl` / `RepositoryUrl` = `https://github.com/mchinchilla/NetOpenGrid`, `RepositoryType=git`
  - `PackageTags`, e.g. `grid datagrid aspnetcore htmx alpinejs tailwind server-rendered`
  - `PackageReadmeFile=README.md`, packed from the repo root via a `None` item with `Pack=true` and `PackagePath=\`
  - `IncludeSymbols=true`, `SymbolPackageFormat=snupkg`, `EmbedUntrackedSources=true`
  - `GenerateDocumentationFile=true` with `<NoWarn>$(NoWarn);CS1591</NoWarn>`
- **`src/NetOpenGrid.Host/NetOpenGrid.Host.csproj`** — re-asserts `<IsPackable>false</IsPackable>`, since it sits under `src/` and would otherwise inherit `true`.

**Why `CS1591` is suppressed:** the repo sets `TreatWarningsAsErrors=true`. Enabling `GenerateDocumentationFile` without suppressing `CS1591` turns every undocumented public member into a build error, forcing an API-wide documentation audit before the first package can build. Suppressing it ships the XML docs that already exist without that blocker.

## 2. CSS delivery

### Build-time

- `tools/build-themes.sh` and `tools/build-themes.cmd` change their output to the single directory `src/NetOpenGrid.Infrastructure/Assets/css/`, producing `netopengrid-{theme}.css`.
- The scripts gain a `--strict` flag that **fails** when the `tailwindcss` CLI is absent. Without the flag the behavior stays warn-and-skip, so a clean clone builds without Tailwind installed; `tools/pack.*` always passes `--strict`, so a release can never silently ship a CSS-less assembly.
- The `CompileTailwindThemes` target moves from `NetOpenGrid.Host.csproj` to `NetOpenGrid.Infrastructure.csproj`, running `BeforeTargets="BeforeBuild"`. Building the *library* refreshes the themes, rather than building the demo app.
- Compiled CSS is **committed** under `Assets/css/`, exactly as it is committed under `wwwroot/css` today. This matters for MSBuild correctness: the `<EmbeddedResource Include="Assets\**" />` glob is expanded during evaluation, before any target runs, so the files must already exist on disk. Because resource *content* is read at compile time, a regeneration during `BeforeBuild` is still picked up within the same build.

### Runtime

- `EmbeddedGridAssets.Load` takes a content type parameter; it currently hardcodes `text/javascript; charset=utf-8`.
- `EmbeddedGridAssets` gains a `Themes` lookup (`IReadOnlyDictionary<string, EmbeddedAsset>`, ordinal-ignore-case), populated by scanning manifest resource names with the prefix `NetOpenGrid.Infrastructure.Assets.css.` and the suffix `.css`. Keys are **theme names** — the literal `netopengrid-` prefix and the `.css` suffix are stripped, so `…Assets.css.netopengrid-grid.css` is keyed `grid`, matching the values `GridOptions.Theme` takes. Adding `themes/foo.css` then requires no C# change.
- `MapNetOpenGrid` maps `{AssetPrefix}/css/netopengrid-{theme}.css`, resolving `{theme}` from `Themes` and returning **404** for an unknown name. It reuses the existing `MapAsset` immutable-cache treatment.
- The embedded route always uses the literal filename `netopengrid-{theme}.css`. `NetOpenGridAssetOptions.CssFilePrefix` stays as it is but becomes meaningful **only** when `CssPath` is set — it renames files the consumer is self-hosting, and cannot rename a resource compiled into the assembly. This is documented on the property.
- `NetOpenGridAssetOptions.CssPath` becomes `string?` with default `null`, meaning *serve the embedded theme from the asset prefix*. Assigning a value preserves today's behavior verbatim — the escape hatch for self-hosted or fully custom CSS.
- `GridHtmlRenderer.AppendDocumentStart`: when `CssPath` is `null` and the theme is embedded, emit `{AssetPrefix}/css/netopengrid-{theme}.css?v={hash}`, consistent with the `?v=` cache-busting the scripts already use. When `CssPath` is set, emit exactly what it emits today.

### Demo apps

`src/NetOpenGrid.Host/wwwroot/css/` and `samples/NetOpenGrid.Example/wwwroot/css/` are **deleted**, and both apps fall through to the embedded route.

This is deliberate. If the demo apps keep serving CSS from their own `wwwroot`, the package delivery path is exercised by nothing in the repository and can break without any test or manual run noticing. Making the demos consume the same route a package consumer uses is what keeps that path honest.

## 3. Pack tooling and local feed

`tools/pack.sh` and `tools/pack.cmd`:

1. Compile themes in strict mode — abort if `tailwindcss` is missing.
2. `dotnet test` — abort on failure.
3. `dotnet pack -c Release -o artifacts`, passing `--version-suffix` when an argument is supplied.

`./tools/pack.sh dev.3` produces `0.1.0-dev.3` for all four packages. `artifacts/` is added to `.gitignore`.

**Why the suffix matters:** NuGet caches packages by id+version in the global packages folder. Re-packing an unchanged version number leaves consuming projects silently pinned to the stale copy. A distinct suffix per iteration avoids that entirely, without anyone needing to remember `dotnet nuget locals`.

Consuming project setup:

```xml
<!-- nuget.config in the consuming project -->
<configuration>
  <packageSources>
    <add key="netopengrid-local" value="D:\NetOpenGrid\artifacts" />
  </packageSources>
</configuration>
```

```
dotnet add package NetOpenGrid --version 0.1.0-dev.3
```

## 4. Testing and verification

Automated, added to `tests/NetOpenGrid.Integration.Tests`:

- `GET {AssetPrefix}/css/netopengrid-grid.css` returns 200, `text/css`, non-empty body.
- The same for `netopengrid-midnight.css`.
- An unknown theme name returns 404.
- The rendered document contains a `<link>` to the embedded CSS path including a `?v=` token.
- Setting `CssPath` explicitly restores the previous `<link>` output.
- `EmbeddedGridAssets.Themes` contains both `grid` and `midnight`. **This is the guard** that fails the build if an assembly is produced without compiled themes.

Manual end-to-end proof, performed before the work is reported complete:

1. Run `./tools/pack.sh dev.1`.
2. Scaffold a throwaway ASP.NET app in the scratchpad directory.
3. Give it a `nuget.config` pointing **only** at `./artifacts` plus nuget.org, so a missed transitive dependency fails loudly rather than resolving from local build output.
4. `dotnet add package NetOpenGrid`, wire a minimal grid, run it.
5. Confirm the grid renders **with styles applied** and the CSS request returns 200.

Building a package is not the same as the package working. Step 5 is the acceptance criterion.

## 5. Documentation

`README.md` (Spanish) and `README.en.md` (English) both get:

- An **Installation** section: the four package IDs, which one to install, the local-feed `nuget.config`, and a minimal `AddNetOpenGrid` / `MapNetOpenGrid` example.
- A note on overriding `CssPath` for self-hosted or custom themes.
- An update to the existing themes section, which currently documents `wwwroot/css` as the compilation output.

## Out of scope

Explicitly deferred until the package has been used in a real project:

- CI workflow, nuget.org or GitHub Packages publishing, package signing
- Automatic versioning from git tags (MinVer / Nerdbank.GitVersioning)
- Multi-targeting beyond `net10.0`
- Assembly merging or ILRepack
- Any public API freeze or compatibility guarantee — `0.1.0` signals the surface is still movable

## Risks and accepted trade-offs

| Risk | Mitigation / acceptance |
|---|---|
| The `CssPath` default flip is a public behavior change | Accepted at `0.1.0`; the old behavior stays reachable by assigning `CssPath` |
| Deleting the demo apps' `wwwroot/css` changes how Host and Example run | Intended — it is what keeps the package path continuously verified |
| Packing without Tailwind installed would ship a broken package | Strict mode in `tools/pack.*`, plus the `Themes` assertion test |
| Theme CSS is fixed at package build time | Correct by construction: the utility classes come from the renderer's C#, fixed in the same build. Custom restyling is served by the `CssPath` escape hatch |
| Local feed staleness via NuGet's id+version cache | A distinct `-dev.N` suffix per iteration |
