# NetOpenGrid NuGet Packaging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make NetOpenGrid installable as NuGet packages from a local folder feed, with the theme CSS travelling inside the package so a consuming app renders a styled grid with no build tooling of its own.

**Architecture:** Four of the five `src/` projects become packable with renamed `PackageId`s (assembly names and namespaces are untouched). The Tailwind-compiled theme CSS moves from each demo app's `wwwroot/css` into `NetOpenGrid.Infrastructure/Assets/css/`, where it is embedded into the assembly and served by `MapNetOpenGrid` — the exact mechanism `netopengrid.js`, htmx and Alpine already use. `NetOpenGridAssetOptions.CssPath` flips to nullable, defaulting to the embedded route, with the old self-hosted behavior preserved by assigning it.

**Tech Stack:** .NET 10, MSBuild (`Directory.Build.props`, central package management), Tailwind CSS v4 standalone CLI, xUnit + `Microsoft.AspNetCore.Mvc.Testing`.

**Spec:** `docs/superpowers/specs/2026-08-25-nuget-packaging-design.md`

## Global Constraints

- Target framework is `net10.0` only. Do not multi-target.
- `TreatWarningsAsErrors=true` is set repo-wide in `Directory.Build.props`. **Any** new warning fails the build.
- `ManagePackageVersionsCentrally=true`. Every `PackageReference` gets its version from `Directory.Packages.props` — never inline a `Version` attribute on a `PackageReference`.
- `Nullable=enable` and `ImplicitUsings=enable` everywhere.
- Assembly names, root namespaces and public namespaces **do not change**. Only `PackageId` changes.
- Version is `0.1.0`, expressed as `VersionPrefix` (never `Version` — see Task 1 Step 3), identical across all four packages.
- The four package IDs are exactly: `NetOpenGrid.Abstractions`, `NetOpenGrid.Core`, `NetOpenGrid`, `NetOpenGrid.EntityFrameworkCore`.
- Do not add a CI workflow, do not push to nuget.org, do not add MinVer. Out of scope.
- Work on a branch, not `main`.

---

### Task 0: Branch

**Files:** none

- [ ] **Step 1: Create the working branch**

```bash
git checkout -b feat/nuget-packaging
```

- [ ] **Step 2: Commit the pre-existing CRLF-only CSS churn so it does not pollute later diffs**

The four files under `wwwroot/css` show as modified with line-ending-only changes. They are deleted in Task 4 anyway; commit them now so every later diff is meaningful.

```bash
git add samples/NetOpenGrid.Example/wwwroot/css src/NetOpenGrid.Host/wwwroot/css
git commit -m "chore: normalize line endings on compiled theme css"
```

- [ ] **Step 3: Commit the design spec**

```bash
git add docs/superpowers/specs/2026-08-25-nuget-packaging-design.md docs/superpowers/plans/2026-08-25-nuget-packaging.md
git commit -m "docs: add nuget packaging design and implementation plan"
```

---

### Task 1: Package metadata and packability

Turns four projects into packages and keeps everything else out. No behavior change — CSS still comes from `wwwroot` after this task.

**Files:**
- Modify: `Directory.Build.props`
- Create: `src/Directory.Build.props`
- Modify: `src/NetOpenGrid.Domain/NetOpenGrid.Domain.csproj`
- Modify: `src/NetOpenGrid.Application/NetOpenGrid.Application.csproj`
- Modify: `src/NetOpenGrid.Infrastructure/NetOpenGrid.Infrastructure.csproj`
- Modify: `src/NetOpenGrid.Persistence.EFCore/NetOpenGrid.Persistence.EFCore.csproj`
- Modify: `src/NetOpenGrid.Host/NetOpenGrid.Host.csproj`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: an `artifacts/` folder containing `NetOpenGrid.0.1.0.nupkg`, `NetOpenGrid.Core.0.1.0.nupkg`, `NetOpenGrid.Abstractions.0.1.0.nupkg`, `NetOpenGrid.EntityFrameworkCore.0.1.0.nupkg` plus matching `.snupkg` files. Task 5 and Task 6 depend on these exact IDs. `VersionPrefix` (not `Version`) is the property Task 5's `--version-suffix` composes with.

- [ ] **Step 1: Write the failing check**

There is no unit test framework for MSBuild metadata; the check is the pack command itself. Run it now to establish the failing baseline.

```bash
dotnet pack NetOpenGrid.slnx -c Release -o artifacts
ls -1 artifacts
```

- [ ] **Step 2: Confirm it fails the requirement**

Expected: the command succeeds but produces the **wrong** set — packages named `NetOpenGrid.Domain.1.0.0.nupkg`, `NetOpenGrid.Application.1.0.0.nupkg`, `NetOpenGrid.Infrastructure.1.0.0.nupkg`, `NetOpenGrid.Persistence.EFCore.1.0.0.nupkg`, **plus** unwanted `NetOpenGrid.Host` / `NetOpenGrid.Example` / test packages. Zero of the four required IDs exist.

Record what you saw, then clean up:

```bash
rm -rf artifacts
```

- [ ] **Step 3: Add version and packability defaults to the root props**

Modify `Directory.Build.props`. Add the two properties below to the existing `<PropertyGroup>`, leaving every existing property untouched:

```xml
    <VersionPrefix>0.1.0</VersionPrefix>
    <IsPackable>false</IsPackable>
```

**`VersionPrefix`, not `Version` — this is load-bearing.** `dotnet pack --version-suffix` is silently ignored when `Version` is set explicitly; it composes only with `VersionPrefix`. Using `Version` here would make Task 5's `-dev.N` workflow emit plain `0.1.0` every time, and consuming projects would silently keep restoring a stale cached copy.

`IsPackable=false` is the repo-wide default so tests and samples can never pack by accident. `src/Directory.Build.props` opts the library projects back in.

- [ ] **Step 4: Create the src-level packaging props**

Create `src/Directory.Build.props`:

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />

  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <Authors>NetOpenGrid Contributors</Authors>
    <Company>NetOpenGrid</Company>
    <Product>NetOpenGrid</Product>
    <Copyright>Copyright (c) NetOpenGrid Contributors</Copyright>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/mchinchilla/NetOpenGrid</PackageProjectUrl>
    <RepositoryUrl>https://github.com/mchinchilla/NetOpenGrid</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageTags>grid;datagrid;aspnetcore;htmx;alpinejs;tailwind;server-rendered;table</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <None Include="$(MSBuildThisFileDirectory)../README.md" Pack="true" PackagePath="\" Visible="false" />
  </ItemGroup>
</Project>
```

Two notes on why these specific values:

- The `Import` of the parent props is required. A `Directory.Build.props` closest to the project wins and MSBuild stops searching upward, so without this import every project under `src/` would lose `TargetFramework`, `Nullable`, `TreatWarningsAsErrors` and central package management.
- `CS1591`/`CS1573` are the "missing XML documentation" warnings. With `TreatWarningsAsErrors=true`, turning on `GenerateDocumentationFile` without suppressing them makes every undocumented public member a build **error**, which would block this task behind an API-wide documentation audit.

- [ ] **Step 5: Set the four package IDs**

`src/NetOpenGrid.Domain/NetOpenGrid.Domain.csproj` — replace the whole file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>NetOpenGrid.Abstractions</PackageId>
    <Description>Core abstractions, column and query models for NetOpenGrid. Referenced transitively; install NetOpenGrid instead.</Description>
  </PropertyGroup>
</Project>
```

`src/NetOpenGrid.Application/NetOpenGrid.Application.csproj` — add the property group, keep the existing `ItemGroup`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>NetOpenGrid.Core</PackageId>
    <Description>Query engine, builders and filtering strategies for NetOpenGrid. Referenced transitively; install NetOpenGrid instead.</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../NetOpenGrid.Domain/NetOpenGrid.Domain.csproj" />
  </ItemGroup>
</Project>
```

`src/NetOpenGrid.Infrastructure/NetOpenGrid.Infrastructure.csproj` — add the property group and drop the redundant `Domain` reference (it arrives transitively through `Application`, and leaving it would put a needless direct dependency in the published package graph):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>NetOpenGrid</PackageId>
    <Description>Server-rendered data grid for ASP.NET Core, powered by htmx, Alpine.js and Tailwind CSS. Ships its client runtime and themes inside the package.</Description>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Include="Assets\**" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../NetOpenGrid.Application/NetOpenGrid.Application.csproj" />
  </ItemGroup>
</Project>
```

`src/NetOpenGrid.Persistence.EFCore/NetOpenGrid.Persistence.EFCore.csproj` — same treatment:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>NetOpenGrid.EntityFrameworkCore</PackageId>
    <Description>Entity Framework Core data source for NetOpenGrid, translating grid queries to IQueryable.</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/NetOpenGrid.Application/NetOpenGrid.Application.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Keep the demo host out of the packages**

`src/NetOpenGrid.Host/NetOpenGrid.Host.csproj` lives under `src/` and would inherit `IsPackable=true`. Add a property group as the first child of `<Project>`, leaving the existing `ItemGroup` and `Target` exactly as they are:

```xml
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
```

- [ ] **Step 7: Ignore the artifacts folder**

Append to `.gitignore`:

```
# NuGet packages produced by tools/pack
artifacts/
```

- [ ] **Step 8: Run the check and verify it now passes**

```bash
dotnet pack NetOpenGrid.slnx -c Release -o artifacts
ls -1 artifacts
```

Expected — exactly these eight files, and nothing else:

```
NetOpenGrid.0.1.0.nupkg
NetOpenGrid.0.1.0.snupkg
NetOpenGrid.Abstractions.0.1.0.nupkg
NetOpenGrid.Abstractions.0.1.0.snupkg
NetOpenGrid.Core.0.1.0.nupkg
NetOpenGrid.Core.0.1.0.snupkg
NetOpenGrid.EntityFrameworkCore.0.1.0.nupkg
NetOpenGrid.EntityFrameworkCore.0.1.0.snupkg
```

No `NetOpenGrid.Host`, no `NetOpenGrid.Example`, no `*.Tests` packages.

- [ ] **Step 9: Verify the dependency graph inside the main package**

```bash
cd artifacts && unzip -o -q NetOpenGrid.0.1.0.nupkg -d nupkg-inspect && cat nupkg-inspect/NetOpenGrid.nuspec && cd ..
```

Expected in the `<dependencies>` group for `.NETCoreApp10.0`: a single `<dependency id="NetOpenGrid.Core" version="0.1.0" ... />`. Also expected: `<readme>README.md</readme>`, `<license type="expression">MIT</license>`, and `<frameworkReference name="Microsoft.AspNetCore.App" />`.

If `NetOpenGrid.Abstractions` appears as a *direct* dependency here, Step 5's removal of the redundant `ProjectReference` did not take.

```bash
rm -rf artifacts
```

- [ ] **Step 10: Confirm the build is still clean**

```bash
dotnet build NetOpenGrid.slnx -c Release
```

Expected: build succeeded, 0 warnings. If `CS1591` errors appear, `NoWarn` in `src/Directory.Build.props` is not being applied — check that the `Import` in Step 4 resolves.

- [ ] **Step 11: Commit**

```bash
git add Directory.Build.props src/Directory.Build.props src/NetOpenGrid.Domain src/NetOpenGrid.Application src/NetOpenGrid.Infrastructure src/NetOpenGrid.Persistence.EFCore src/NetOpenGrid.Host/NetOpenGrid.Host.csproj .gitignore
git commit -m "build: make library projects packable with renamed package ids"
```

---

### Task 2: Embed the compiled theme CSS in the assembly

Moves theme compilation output into the Infrastructure assembly and exposes it in C#. Nothing serves it yet — that is Task 3.

**Files:**
- Modify: `tools/build-themes.sh`
- Modify: `tools/build-themes.cmd`
- Modify: `src/NetOpenGrid.Infrastructure/NetOpenGrid.Infrastructure.csproj`
- Modify: `src/NetOpenGrid.Host/NetOpenGrid.Host.csproj`
- Create: `src/NetOpenGrid.Infrastructure/Assets/css/netopengrid-grid.css` (generated, committed)
- Create: `src/NetOpenGrid.Infrastructure/Assets/css/netopengrid-midnight.css` (generated, committed)
- Modify: `src/NetOpenGrid.Infrastructure/Assets/EmbeddedGridAssets.cs`
- Test: `tests/NetOpenGrid.Integration.Tests/EmbeddedThemeAssetTests.cs` (create)

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `EmbeddedGridAssets.Themes`, an `IReadOnlyDictionary<string, EmbeddedAsset>` keyed by **theme name** (`"grid"`, `"midnight"` — no `netopengrid-` prefix, no `.css` suffix), ordinal-ignore-case. Task 3 iterates it to map routes; Task 4 does `TryGetValue(theme, out var asset)` and reads `asset.Version`. Also produces `tools/build-themes.{sh,cmd} --strict`, which Task 5 calls.

- [ ] **Step 1: Write the failing test**

Create `tests/NetOpenGrid.Integration.Tests/EmbeddedThemeAssetTests.cs`:

```csharp
using NetOpenGrid.Infrastructure.Assets;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class EmbeddedThemeAssetTests
{
    [Fact]
    public void Themes_ContainsEveryCompiledTailwindTheme()
    {
        Assert.True(EmbeddedGridAssets.Themes.ContainsKey("grid"), "theme 'grid' is not embedded in the assembly");
        Assert.True(EmbeddedGridAssets.Themes.ContainsKey("midnight"), "theme 'midnight' is not embedded in the assembly");
    }

    [Fact]
    public void Themes_AreNonEmptyCssWithStableVersions()
    {
        Assert.NotEmpty(EmbeddedGridAssets.Themes);

        foreach (var (name, asset) in EmbeddedGridAssets.Themes)
        {
            Assert.NotEmpty(asset.Bytes);
            Assert.Equal("text/css; charset=utf-8", asset.ContentType);
            Assert.Equal(12, asset.Version.Length);
            Assert.DoesNotContain('.', name);
        }
    }

    [Fact]
    public void Themes_LookupIsCaseInsensitive()
    {
        Assert.True(EmbeddedGridAssets.Themes.ContainsKey("GRID"));
    }
}
```

This is the guard the spec calls out: it fails the build if an assembly is ever produced without compiled themes.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/NetOpenGrid.Integration.Tests --filter FullyQualifiedName~EmbeddedThemeAssetTests
```

Expected: compile error — `'EmbeddedGridAssets' does not contain a definition for 'Themes'`.

- [ ] **Step 3: Point the theme compiler at the Infrastructure assets folder and add strict mode**

Replace `tools/build-themes.sh` entirely:

```bash
#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
THEMES_DIR="$ROOT/themes"
OUT="$ROOT/src/NetOpenGrid.Infrastructure/Assets/css"

STRICT=0
for arg in "$@"; do
  case "$arg" in
    --strict) STRICT=1 ;;
    *) echo "[netopengrid] ERROR: unknown argument '$arg'" >&2; exit 2 ;;
  esac
done

mkdir -p "$OUT"

if ! command -v tailwindcss >/dev/null 2>&1; then
  if [ "$STRICT" -eq 1 ]; then
    echo "[netopengrid] ERROR: 'tailwindcss' CLI not found in PATH. Refusing to package without freshly compiled themes." >&2
    exit 1
  fi
  echo "[netopengrid] WARNING: 'tailwindcss' CLI not found in PATH; keeping the committed theme CSS." >&2
  exit 0
fi

for theme in "$THEMES_DIR"/*.css; do
  name="$(basename "$theme" .css)"
  echo "[netopengrid] Compiling theme '$name'..."
  tailwindcss -i "$theme" -o "$OUT/netopengrid-$name.css" --minify --silent
done

echo "[netopengrid] Themes written to: $OUT"
```

Replace `tools/build-themes.cmd` entirely:

```bat
@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0.."
set "THEMES_DIR=%ROOT%\themes"
set "OUT=%ROOT%\src\NetOpenGrid.Infrastructure\Assets\css"

set "STRICT=0"
if /i "%~1"=="--strict" set "STRICT=1"

if not exist "%OUT%" mkdir "%OUT%"

where tailwindcss >nul 2>nul
if errorlevel 1 (
  if "%STRICT%"=="1" (
    echo [netopengrid] ERROR: 'tailwindcss' CLI not found in PATH. Refusing to package without freshly compiled themes. 1>&2
    exit /b 1
  )
  echo [netopengrid] WARNING: 'tailwindcss' CLI not found in PATH; keeping the committed theme CSS. 1>&2
  exit /b 0
)

for %%F in ("%THEMES_DIR%\*.css") do (
  set "name=%%~nF"
  echo [netopengrid] Compiling theme '!name!'...
  tailwindcss -i "%%F" -o "%OUT%\netopengrid-!name!.css" --minify --silent || exit /b 1
)

echo [netopengrid] Themes written to %OUT%
```

The `@source "../src"` directive inside `themes/*.css` is resolved relative to the **input** file, so changing the output directory does not affect Tailwind's scan of the renderer's C#. Do not touch `themes/*.css`.

- [ ] **Step 4: Move the compile target from the demo host to the library**

Delete the entire `<Target Name="CompileTailwindThemes">` element from `src/NetOpenGrid.Host/NetOpenGrid.Host.csproj`, and add it to `src/NetOpenGrid.Infrastructure/NetOpenGrid.Infrastructure.csproj` as the last child of `<Project>`:

```xml
  <Target Name="CompileTailwindThemes" BeforeTargets="BeforeBuild">
    <Exec Condition="'$(OS)' == 'Windows_NT'" Command="tools\build-themes.cmd" WorkingDirectory="$(MSBuildThisFileDirectory)../.." />
    <Exec Condition="'$(OS)' != 'Windows_NT'" Command="./tools/build-themes.sh" WorkingDirectory="$(MSBuildThisFileDirectory)../.." />
  </Target>
```

Building the library now refreshes the themes, rather than building the demo app.

- [ ] **Step 5: Generate the theme CSS and confirm the resource names**

```bash
./tools/build-themes.sh --strict
ls -la src/NetOpenGrid.Infrastructure/Assets/css
```

Expected: `netopengrid-grid.css` and `netopengrid-midnight.css`, each roughly 34 KB.

Now confirm the manifest resource names MSBuild will generate, rather than assuming them:

```bash
dotnet build src/NetOpenGrid.Infrastructure -c Debug
grep -a -o 'NetOpenGrid\.Infrastructure\.Assets\.css\.[A-Za-z0-9._-]*' \
  src/NetOpenGrid.Infrastructure/bin/Debug/net10.0/NetOpenGrid.Infrastructure.dll | sort -u
```

Expected output — exactly these two lines:

```
NetOpenGrid.Infrastructure.Assets.css.netopengrid-grid.css
NetOpenGrid.Infrastructure.Assets.css.netopengrid-midnight.css
```

MSBuild derives the manifest name from `$(RootNamespace)` plus the path with separators replaced by dots, and it can rewrite characters that are invalid in an identifier. `css` is a valid segment and the hyphen sits in the file name, so both should survive — but confirm rather than assume. If the names differ, set `ThemeResourcePrefix` in the next step to the prefix that actually appears.

- [ ] **Step 6: Add the Themes lookup**

Replace `src/NetOpenGrid.Infrastructure/Assets/EmbeddedGridAssets.cs` entirely:

```csharp
using System.Reflection;
using System.Security.Cryptography;

namespace NetOpenGrid.Infrastructure.Assets;

public sealed record EmbeddedAsset(byte[] Bytes, string Version, string ContentType);

/// <summary>
/// Client runtime, vendor libraries and compiled theme stylesheets embedded in the
/// assembly, loaded once per process. The version (short SHA-256) enables immutable
/// caching via ?v= URLs.
/// </summary>
public static class EmbeddedGridAssets
{
    private const string ResourceRoot = "NetOpenGrid.Infrastructure.Assets.";
    private const string ThemeResourcePrefix = ResourceRoot + "css.netopengrid-";
    private const string CssExtension = ".css";
    private const string JavaScriptContentType = "text/javascript; charset=utf-8";
    private const string CssContentType = "text/css; charset=utf-8";

    public static readonly EmbeddedAsset ClientRuntime = Load("netopengrid.js", JavaScriptContentType);
    public static readonly EmbeddedAsset Htmx = Load("vendor/htmx.min.js", JavaScriptContentType);
    public static readonly EmbeddedAsset Alpine = Load("vendor/alpine.min.js", JavaScriptContentType);

    /// <summary>
    /// Compiled Tailwind themes keyed by theme name (the value <c>GridOptions.Theme</c>
    /// takes), with the <c>netopengrid-</c> prefix and <c>.css</c> suffix stripped.
    /// Adding a file to <c>themes/</c> makes it appear here with no code change.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, EmbeddedAsset> Themes = LoadThemes();

    private static EmbeddedAsset Load(string relativePath, string contentType)
    {
        var assembly = typeof(EmbeddedGridAssets).Assembly;
        var logicalName = ResourceRoot + relativePath.Replace('/', '.');

        return LoadResource(assembly, logicalName, contentType);
    }

    private static IReadOnlyDictionary<string, EmbeddedAsset> LoadThemes()
    {
        var assembly = typeof(EmbeddedGridAssets).Assembly;
        var themes = new Dictionary<string, EmbeddedAsset>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ThemeResourcePrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(CssExtension, StringComparison.Ordinal))
            {
                continue;
            }

            var theme = resourceName[ThemeResourcePrefix.Length..^CssExtension.Length];
            themes[theme] = LoadResource(assembly, resourceName, CssContentType);
        }

        return themes;
    }

    private static EmbeddedAsset LoadResource(Assembly assembly, string logicalName, string contentType)
    {
        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Embedded asset '{logicalName}' is missing from the assembly.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var version = Convert.ToHexString(SHA256.HashData(bytes))[..12].ToLowerInvariant();

        return new EmbeddedAsset(bytes, version, contentType);
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test tests/NetOpenGrid.Integration.Tests --filter FullyQualifiedName~EmbeddedThemeAssetTests
```

Expected: 3 passed.

- [ ] **Step 8: Run the full suite to confirm nothing regressed**

```bash
dotnet test NetOpenGrid.slnx
```

Expected: all green. The demo apps still serve CSS from their own `wwwroot`, so rendering is unchanged at this point.

- [ ] **Step 9: Commit**

```bash
git add tools/build-themes.sh tools/build-themes.cmd src/NetOpenGrid.Infrastructure src/NetOpenGrid.Host/NetOpenGrid.Host.csproj tests/NetOpenGrid.Integration.Tests/EmbeddedThemeAssetTests.cs
git commit -m "feat: embed compiled tailwind themes in the infrastructure assembly"
```

---

### Task 3: Serve the embedded themes from MapNetOpenGrid

**Files:**
- Modify: `src/NetOpenGrid.Infrastructure/Endpoints/NetOpenGridEndpointExtensions.cs:36-38`
- Test: `tests/NetOpenGrid.Integration.Tests/ThemeCssEndpointTests.cs` (create)

**Interfaces:**
- Consumes: `EmbeddedGridAssets.Themes` from Task 2.
- Produces: a `GET {AssetPrefix}/css/netopengrid-{theme}.css` route for every embedded theme. Task 4's renderer emits URLs pointing at it; Task 6 curls it.

- [ ] **Step 1: Write the failing test**

Create `tests/NetOpenGrid.Integration.Tests/ThemeCssEndpointTests.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using NetOpenGrid.Infrastructure.Assets;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

public class ThemeCssEndpointTests : IClassFixture<HostFactory>
{
    private readonly HostFactory _factory;

    public ThemeCssEndpointTests(HostFactory factory) => _factory = factory;

    [Theory]
    [InlineData("grid")]
    [InlineData("midnight")]
    public async Task ThemeCss_IsServedFromTheEmbeddedAssets(string theme)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/_netgrid/css/netopengrid-{theme}.css");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);

        var css = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(css);
        Assert.Equal(EmbeddedGridAssets.Themes[theme].Bytes.Length, response.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task ThemeCss_IsImmutablyCacheable()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/_netgrid/css/netopengrid-grid.css");

        response.EnsureSuccessStatusCode();
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ThemeCss_UnknownTheme_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/_netgrid/css/netopengrid-doesnotexist.css");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

`HostFactory` already exists in `tests/NetOpenGrid.Integration.Tests/GridEndpointTests.cs:9` — reuse it, do not redeclare it.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/NetOpenGrid.Integration.Tests --filter FullyQualifiedName~ThemeCssEndpointTests
```

Expected: the two `ThemeCss_IsServedFromTheEmbeddedAssets` cases and `ThemeCss_IsImmutablyCacheable` fail with 404 (no such route). `ThemeCss_UnknownTheme_ReturnsNotFound` passes trivially.

- [ ] **Step 3: Map a route per embedded theme**

In `src/NetOpenGrid.Infrastructure/Endpoints/NetOpenGridEndpointExtensions.cs`, immediately after the three existing `MapAsset` calls for the JS assets, add:

```csharp
        foreach (var (theme, asset) in EmbeddedGridAssets.Themes)
        {
            MapAsset(endpoints, $"{assetPrefix}/css/netopengrid-{theme}.css", asset);
        }
```

Mapping one route per known theme — rather than a single `{file}` route with a lookup — gets the 404 for unknown themes from routing itself, with no handler branch to write or test.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test tests/NetOpenGrid.Integration.Tests --filter FullyQualifiedName~ThemeCssEndpointTests
```

Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add src/NetOpenGrid.Infrastructure/Endpoints/NetOpenGridEndpointExtensions.cs tests/NetOpenGrid.Integration.Tests/ThemeCssEndpointTests.cs
git commit -m "feat: serve embedded theme stylesheets from MapNetOpenGrid"
```

---

### Task 4: Default the renderer to the embedded stylesheet

The behavior switch. After this task a consumer needs no `wwwroot` of their own.

**Files:**
- Modify: `src/NetOpenGrid.Infrastructure/Runtime/NetOpenGridAssetOptions.cs`
- Modify: `src/NetOpenGrid.Infrastructure/Rendering/GridHtmlRenderer.cs:242-247`
- Modify: `src/NetOpenGrid.Host/Program.cs:39`
- Delete: `src/NetOpenGrid.Host/wwwroot/css/netopengrid-grid.css`
- Delete: `src/NetOpenGrid.Host/wwwroot/css/netopengrid-midnight.css`
- Delete: `samples/NetOpenGrid.Example/wwwroot/css/netopengrid-grid.css`
- Delete: `samples/NetOpenGrid.Example/wwwroot/css/netopengrid-midnight.css`
- Test: `tests/NetOpenGrid.Integration.Tests/ThemeCssLinkTests.cs` (create)

**Interfaces:**
- Consumes: `EmbeddedGridAssets.Themes` (Task 2) and the routes from Task 3.
- Produces: `NetOpenGridAssetOptions.CssPath` is now `string?` defaulting to `null`, and `internal string? NormalizedCssPath`. Task 7 documents both.

- [ ] **Step 1: Write the failing test**

Create `tests/NetOpenGrid.Integration.Tests/ThemeCssLinkTests.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetOpenGrid.Infrastructure.Assets;
using NetOpenGrid.Infrastructure.Runtime;
using Xunit;

namespace NetOpenGrid.Integration.Tests;

/// <summary>Host with the default asset options: the theme comes from the assembly.</summary>
public class ThemeCssLinkTests : IClassFixture<HostFactory>
{
    private readonly HostFactory _factory;

    public ThemeCssLinkTests(HostFactory factory) => _factory = factory;

    [Fact]
    public async Task Document_LinksTheEmbeddedStylesheetWithACacheBustingVersion()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/netgrid/employees");

        var version = EmbeddedGridAssets.Themes["grid"].Version;
        Assert.Contains($"<link rel=\"stylesheet\" href=\"/_netgrid/css/netopengrid-grid.css?v={version}\">", html);
    }
}

/// <summary>Host that opts back in to serving its own stylesheet from wwwroot.</summary>
public sealed class SelfHostedCssFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<NetOpenGridAssetOptions>();
            services.AddSingleton(new NetOpenGridAssetOptions
            {
                AssetPrefix = "/_netgrid",
                CssPath = "/css",
            });
        });
}

public class SelfHostedCssLinkTests : IClassFixture<SelfHostedCssFactory>
{
    private readonly SelfHostedCssFactory _factory;

    public SelfHostedCssLinkTests(SelfHostedCssFactory factory) => _factory = factory;

    [Fact]
    public async Task Document_LinksTheHostStylesheet_WhenCssPathIsSet()
    {
        var client = _factory.CreateClient();

        var html = await client.GetStringAsync("/netgrid/employees");

        Assert.Contains("<link rel=\"stylesheet\" href=\"/css/netopengrid-grid.css\">", html);
        Assert.DoesNotContain("/_netgrid/css/netopengrid-grid.css", html);
    }
}
```

The second fixture is what proves the escape hatch still works. `GridRuntime` instances resolve `NetOpenGridAssetOptions` from the container when first requested, so replacing the singleton in `ConfigureServices` reaches them.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test tests/NetOpenGrid.Integration.Tests --filter FullyQualifiedName~CssLinkTests
```

Expected: `Document_LinksTheEmbeddedStylesheetWithACacheBustingVersion` fails — the document still contains `href="/css/netopengrid-grid.css"` with no version. `Document_LinksTheHostStylesheet_WhenCssPathIsSet` passes already, which is the point: it is the regression guard for the behavior you are about to move off the default path.

- [ ] **Step 3: Make CssPath opt-in**

Replace `src/NetOpenGrid.Infrastructure/Runtime/NetOpenGridAssetOptions.cs` entirely:

```csharp
namespace NetOpenGrid.Infrastructure.Runtime;

/// <summary>
/// Controls where the component's client runtime and theme stylesheet are served from.
/// The JS runtime, vendor libraries and compiled themes are all embedded in the assembly
/// and served by <c>MapNetOpenGrid</c> automatically.
/// </summary>
public sealed class NetOpenGridAssetOptions
{
    /// <summary>Route prefix the embedded assets are served under.</summary>
    public string AssetPrefix { get; set; } = "/_netgrid";

    /// <summary>
    /// When <c>null</c> (the default) the theme compiled into the assembly is served from
    /// <see cref="AssetPrefix"/>/css, so the host application needs no stylesheet of its own.
    /// Set this to serve your own stylesheet from the host application instead
    /// (for example <c>"/css"</c>), in which case <see cref="CssFilePrefix"/> applies.
    /// </summary>
    public string? CssPath { get; set; }

    /// <summary>
    /// File name prefix for a self-hosted stylesheet. Applies only when <see cref="CssPath"/>
    /// is set: it cannot rename a stylesheet compiled into the assembly.
    /// </summary>
    public string CssFilePrefix { get; set; } = "netopengrid-";

    internal string NormalizedAssetPrefix => Normalize(AssetPrefix);
    internal string? NormalizedCssPath => CssPath is null ? null : Normalize(CssPath);

    private static string Normalize(string path)
    {
        var trimmed = path.TrimEnd('/');
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }
}
```

- [ ] **Step 4: Branch the stylesheet link in the renderer**

In `src/NetOpenGrid.Infrastructure/Rendering/GridHtmlRenderer.cs`, inside `AppendDocumentStart`, replace these six lines:

```csharp
        w.Write("<link rel=\"stylesheet\" href=\"");
        w.Write(_assetOptions.NormalizedCssPath);
        w.Write('/');
        w.Write(_assetOptions.CssFilePrefix);
        AppendEncoded(w, _options.Theme);
        w.Write(".css\">");
```

with:

```csharp
        w.Write("<link rel=\"stylesheet\" href=\"");
        if (_assetOptions.NormalizedCssPath is { } cssPath)
        {
            w.Write(cssPath);
            w.Write('/');
            w.Write(_assetOptions.CssFilePrefix);
            AppendEncoded(w, _options.Theme);
            w.Write(".css");
        }
        else
        {
            w.Write(prefix);
            w.Write("/css/netopengrid-");
            AppendEncoded(w, _options.Theme);
            w.Write(".css");

            if (Assets.EmbeddedGridAssets.Themes.TryGetValue(_options.Theme, out var themeAsset))
            {
                w.Write("?v=");
                w.Write(themeAsset.Version);
            }
        }
        w.Write("\">");
```

`prefix` is the existing local at the top of the method (`GridHtmlRenderer.cs:234`). The `TryGetValue` guard means a custom theme name that is not embedded still emits a usable link rather than a broken `?v=` — it will 404, which is the honest signal that the theme was never compiled in.

- [ ] **Step 5: Stop the demo host from overriding the default**

In `src/NetOpenGrid.Host/Program.cs`, delete this single line from the `AddNetOpenGrid` configuration lambda:

```csharp
        o.CssPath = "/css";
```

Leave `o.AssetPrefix = "/_netgrid";` in place. `samples/NetOpenGrid.Example/Program.cs:31` calls `AddNetOpenGrid()` with no configuration, so it picks up the new default with no edit.

- [ ] **Step 6: Delete the demo apps' stylesheet copies**

```bash
git rm -r src/NetOpenGrid.Host/wwwroot/css samples/NetOpenGrid.Example/wwwroot/css
```

Leave `app.UseStaticFiles()` in both `Program.cs` files. Both apps may serve other static content, and removing it is unrelated to this change.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test tests/NetOpenGrid.Integration.Tests --filter FullyQualifiedName~CssLinkTests
```

Expected: 2 passed.

- [ ] **Step 8: Run the full suite**

```bash
dotnet test NetOpenGrid.slnx
```

Expected: all green.

- [ ] **Step 9: Confirm the demo host actually renders styled**

```bash
dotnet run --project src/NetOpenGrid.Host --urls http://localhost:5188 &
sleep 8
curl -s http://localhost:5188/netgrid/employees | grep -o '<link rel="stylesheet"[^>]*>'
curl -s -o /dev/null -w '%{http_code} %{content_type}\n' "http://localhost:5188/_netgrid/css/netopengrid-grid.css"
kill %1
```

Expected: the link tag points at `/_netgrid/css/netopengrid-grid.css?v=<12 hex chars>`, and the CSS request returns `200 text/css; charset=utf-8`.

- [ ] **Step 10: Commit**

```bash
git add src/NetOpenGrid.Infrastructure/Runtime/NetOpenGridAssetOptions.cs src/NetOpenGrid.Infrastructure/Rendering/GridHtmlRenderer.cs src/NetOpenGrid.Host/Program.cs tests/NetOpenGrid.Integration.Tests/ThemeCssLinkTests.cs
git commit -m "feat: serve the embedded theme by default, keeping CssPath as an opt-in override"
```

---

### Task 5: Pack tooling

**Files:**
- Create: `tools/pack.sh`
- Create: `tools/pack.cmd`

**Interfaces:**
- Consumes: `tools/build-themes.{sh,cmd} --strict` (Task 2) and the `VersionPrefix` property (Task 1).
- Produces: `./artifacts/*.nupkg` at version `0.1.0` or `0.1.0-<suffix>`. Task 6 restores from this folder.

- [ ] **Step 1: Write the failing check**

```bash
./tools/pack.sh dev.1
```

Expected: `bash: ./tools/pack.sh: No such file or directory`.

- [ ] **Step 2: Write the pack script**

Create `tools/pack.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SUFFIX="${1:-}"
OUT="$ROOT/artifacts"

echo "[netopengrid] Compiling themes (strict)..."
"$ROOT/tools/build-themes.sh" --strict

echo "[netopengrid] Running tests..."
dotnet test "$ROOT/NetOpenGrid.slnx" -c Release

rm -rf "$OUT"

if [ -n "$SUFFIX" ]; then
  echo "[netopengrid] Packing 0.1.0-$SUFFIX..."
  dotnet pack "$ROOT/NetOpenGrid.slnx" -c Release -o "$OUT" --version-suffix "$SUFFIX"
else
  echo "[netopengrid] Packing release version..."
  dotnet pack "$ROOT/NetOpenGrid.slnx" -c Release -o "$OUT"
fi

echo "[netopengrid] Packages in $OUT:"
ls -1 "$OUT"
```

Create `tools/pack.cmd`:

```bat
@echo off
setlocal

set "ROOT=%~dp0.."
set "SUFFIX=%~1"
set "OUT=%ROOT%\artifacts"

echo [netopengrid] Compiling themes (strict)...
call "%ROOT%\tools\build-themes.cmd" --strict || exit /b 1

echo [netopengrid] Running tests...
dotnet test "%ROOT%\NetOpenGrid.slnx" -c Release || exit /b 1

if exist "%OUT%" rmdir /s /q "%OUT%"

if "%SUFFIX%"=="" (
  echo [netopengrid] Packing release version...
  dotnet pack "%ROOT%\NetOpenGrid.slnx" -c Release -o "%OUT%" || exit /b 1
) else (
  echo [netopengrid] Packing 0.1.0-%SUFFIX%...
  dotnet pack "%ROOT%\NetOpenGrid.slnx" -c Release -o "%OUT%" --version-suffix "%SUFFIX%" || exit /b 1
)

echo [netopengrid] Packages in %OUT%:
dir /b "%OUT%"
```

- [ ] **Step 3: Make the shell script executable**

```bash
chmod +x tools/pack.sh
git update-index --chmod=+x tools/pack.sh
```

- [ ] **Step 4: Run the check and verify the suffix is applied**

```bash
./tools/pack.sh dev.1
```

Expected: themes compile, tests pass, and `artifacts/` contains exactly `NetOpenGrid.0.1.0-dev.1.nupkg`, `NetOpenGrid.Core.0.1.0-dev.1.nupkg`, `NetOpenGrid.Abstractions.0.1.0-dev.1.nupkg`, `NetOpenGrid.EntityFrameworkCore.0.1.0-dev.1.nupkg` and their `.snupkg` counterparts.

**If the files come out as plain `0.1.0`,** `Version` is set somewhere instead of `VersionPrefix` — recheck Task 1 Step 3.

- [ ] **Step 5: Verify strict mode actually fails**

```bash
PATH=/usr/bin:/bin ./tools/build-themes.sh --strict; echo "exit=$?"
```

Expected: the error message about `tailwindcss` not being found, and `exit=1`. This is what stops a CSS-less package from ever shipping.

- [ ] **Step 6: Commit**

```bash
git add tools/pack.sh tools/pack.cmd
git commit -m "build: add pack scripts producing a local folder feed"
```

---

### Task 6: End-to-end consumer verification

The acceptance criterion. Building a package is not the same as the package working.

**Files:**
- Create (throwaway, outside the repo): a consumer app under the scratchpad directory.

**Interfaces:**
- Consumes: `./artifacts` from Task 5.
- Produces: nothing consumed by later tasks. Its output is the evidence that the work is done.

- [ ] **Step 1: Pack a fresh iteration**

```bash
./tools/pack.sh dev.1
```

- [ ] **Step 2: Scaffold the consumer outside the repository**

```bash
SCRATCH="/c/Users/eduardo/AppData/Local/Temp/claude/D--NetOpenGrid/4a3c60c7-77ff-451a-8920-beed7b1ce79e/scratchpad"
rm -rf "$SCRATCH/GridConsumer"
mkdir -p "$SCRATCH/GridConsumer"
cd "$SCRATCH/GridConsumer"
dotnet new web -n GridConsumer -o .
```

Use the forward-slash form above. `$LOCALAPPDATA` expands to a backslash path, which Git Bash will not treat as a directory separator in `mkdir -p`.

It must live outside `D:\NetOpenGrid` so it does not inherit the repo's `Directory.Build.props` or `Directory.Packages.props` — a consumer in the wild has neither.

- [ ] **Step 3: Point it at the local feed only**

Create `nuget.config` in the consumer directory:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="netopengrid-local" value="D:\NetOpenGrid\artifacts" />
  </packageSources>
</configuration>
```

`<clear />` plus an explicit nuget.org entry means a missed transitive dependency fails loudly instead of silently resolving from some other configured source.

- [ ] **Step 4: Install the package**

```bash
dotnet add package NetOpenGrid --version 0.1.0-dev.1
```

Expected: it resolves `NetOpenGrid`, and pulls `NetOpenGrid.Core` and `NetOpenGrid.Abstractions` transitively. If NuGet reports it cannot find `NetOpenGrid.Core`, the dependency IDs in the nuspec do not match the packed IDs — return to Task 1 Step 9.

- [ ] **Step 5: Write a minimal consumer app**

Replace the generated `Program.cs` entirely:

```csharp
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;

var people = new Person[]
{
    new(1, "Ada Lovelace", "ada@example.com", "Engineering"),
    new(2, "Alan Turing", "alan@example.com", "Research"),
    new(3, "Grace Hopper", "grace@example.com", "Engineering"),
    new(4, "Katherine Johnson", "katherine@example.com", "Mathematics"),
};

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNetOpenGrid()
    .AddGrid<Person>(
        "people",
        options => options
            .WithTitle("People")
            .WithTheme("grid")
            .WithDefaultPageSize(10)
            .AddColumn("name", p => p.Name, c => c.Header("Name").Searchable())
            .AddColumn("email", p => p.Email, c => c.Header("Email"))
            .AddColumn("team", p => p.Team, c => c.Header("Team")),
        (_, options) => new InMemoryGridDataSource<Person>(options, people));

var app = builder.Build();

app.MapNetOpenGrid();
app.MapGet("/", () => Results.Redirect("/netgrid/people"));

app.Run();

public sealed record Person(int Id, string Name, string Email, string Team);
```

Note there is **no** `app.UseStaticFiles()` and **no** `wwwroot`. That absence is the thing being proven.

- [ ] **Step 6: Run it**

```bash
dotnet run --urls http://localhost:5299 &
sleep 10
```

- [ ] **Step 7: Verify the grid renders and the stylesheet resolves**

```bash
curl -s http://localhost:5299/netgrid/people > page.html
grep -o '<link rel="stylesheet"[^>]*>' page.html
grep -c 'Ada Lovelace' page.html
curl -s -o /dev/null -w 'css: %{http_code} %{content_type} %{size_download} bytes\n' \
  "http://localhost:5299/_netgrid/css/netopengrid-grid.css"
curl -s -o /dev/null -w 'js:  %{http_code} %{content_type}\n' \
  "http://localhost:5299/_netgrid/netopengrid.js"
```

Expected, all four:
- the link tag points at `/_netgrid/css/netopengrid-grid.css?v=<12 hex chars>`
- `Ada Lovelace` appears at least once
- `css: 200 text/css; charset=utf-8` with roughly 34000 bytes
- `js:  200 text/javascript; charset=utf-8`

- [ ] **Step 8: Confirm visually**

Open `http://localhost:5299/netgrid/people` in a browser. The grid must be **styled** — real table layout, spacing, header treatment — not unstyled HTML. An unstyled grid with a 200 on the CSS means the link URL and the route disagree.

- [ ] **Step 9: Tear down**

```bash
kill %1
cd /d/NetOpenGrid
```

Keep the consumer directory in the scratchpad until Task 7 is done; the README snippets should match what you actually ran.

- [ ] **Step 10: Record the result**

No commit for this task — nothing in the repo changed. Report in the task summary: the exact `curl` output from Step 7 and whether Step 8 looked correct.

---

### Task 7: Documentation

**Files:**
- Modify: `README.md` (Spanish)
- Modify: `README.en.md` (English)

**Interfaces:**
- Consumes: the verified install flow from Task 6.
- Produces: nothing.

- [ ] **Step 1: Locate the insertion points**

```bash
grep -n "^## \|^### " README.md | head -30
grep -n "^## \|^### " README.en.md | head -30
grep -n "wwwroot/css\|build-themes" README.md README.en.md
```

The installation section goes immediately before the existing quick-start / usage section. The theme section (`## 🎨 Temas (Tailwind v4)` around `README.md:494`) needs its output path corrected.

- [ ] **Step 2: Add the installation section to README.md**

Insert before the quick-start section:

````markdown
## 📦 Instalación

NetOpenGrid se distribuye en cuatro paquetes. **Instala solo el primero** — los demás llegan como dependencias transitivas:

| Paquete | Contenido |
|---|---|
| `NetOpenGrid` | Integración con ASP.NET Core: DI, endpoints, renderer. **Este es el que instalas.** |
| `NetOpenGrid.Core` | Motor de consultas, builders y estrategias de filtrado (transitivo) |
| `NetOpenGrid.Abstractions` | Abstracciones, modelo de columnas y de consulta (transitivo) |
| `NetOpenGrid.EntityFrameworkCore` | Origen de datos para EF Core. Opcional, instálalo aparte |

### Desde el feed local

Genera los paquetes y publícalos en `./artifacts`:

```bash
./tools/pack.sh dev.1        # produce 0.1.0-dev.1
```

En el proyecto que consume, crea un `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="netopengrid-local" value="D:\NetOpenGrid\artifacts" />
  </packageSources>
</configuration>
```

```bash
dotnet add package NetOpenGrid --version 0.1.0-dev.1
```

> **Sobre el sufijo `-dev.N`:** NuGet cachea los paquetes por id + versión. Si vuelves a empaquetar con el mismo número de versión, el proyecto consumidor seguirá usando la copia cacheada sin avisar. Usa un sufijo distinto en cada iteración.

### Uso mínimo

```csharp
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNetOpenGrid()
    .AddGrid<Person>(
        "people",
        options => options
            .WithTitle("People")
            .AddColumn("name", p => p.Name, c => c.Header("Name").Searchable())
            .AddColumn("email", p => p.Email, c => c.Header("Email")),
        (_, options) => new InMemoryGridDataSource<Person>(options, people));

var app = builder.Build();
app.MapNetOpenGrid();
app.Run();
```

No hace falta `wwwroot`, ni `UseStaticFiles()`, ni herramientas de build. El runtime JS, htmx, Alpine y el CSS de los temas van **dentro del paquete** y los sirve `MapNetOpenGrid`.

### CSS propio

Por defecto `CssPath` es `null` y el tema compilado se sirve desde `{AssetPrefix}/css`. Si prefieres alojar tu propia hoja de estilos, asígnalo:

```csharp
builder.Services.AddNetOpenGrid(o =>
{
    o.AssetPrefix = "/_netgrid";
    o.CssPath = "/css";          // sirve wwwroot/css/netopengrid-{tema}.css desde tu app
    o.CssFilePrefix = "netopengrid-";
});
```

`CssFilePrefix` solo aplica cuando `CssPath` está asignado: no puede renombrar un recurso compilado dentro del ensamblado.
````

- [ ] **Step 3: Add the same section to README.en.md**

Insert the English equivalent at the matching position:

````markdown
## 📦 Installation

NetOpenGrid ships as four packages. **Install only the first** — the rest arrive transitively:

| Package | Contents |
|---|---|
| `NetOpenGrid` | ASP.NET Core integration: DI, endpoints, renderer. **This is the one you install.** |
| `NetOpenGrid.Core` | Query engine, builders and filter strategies (transitive) |
| `NetOpenGrid.Abstractions` | Abstractions, column and query models (transitive) |
| `NetOpenGrid.EntityFrameworkCore` | EF Core data source. Optional, install separately |

### From the local feed

Build the packages into `./artifacts`:

```bash
./tools/pack.sh dev.1        # produces 0.1.0-dev.1
```

In the consuming project, add a `nuget.config`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="netopengrid-local" value="D:\NetOpenGrid\artifacts" />
  </packageSources>
</configuration>
```

```bash
dotnet add package NetOpenGrid --version 0.1.0-dev.1
```

> **About the `-dev.N` suffix:** NuGet caches packages by id + version. Re-packing the same version number leaves the consuming project silently pinned to the cached copy. Use a distinct suffix for each iteration.

### Minimal usage

```csharp
using NetOpenGrid.Application.DataSources;
using NetOpenGrid.Infrastructure;
using NetOpenGrid.Infrastructure.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNetOpenGrid()
    .AddGrid<Person>(
        "people",
        options => options
            .WithTitle("People")
            .AddColumn("name", p => p.Name, c => c.Header("Name").Searchable())
            .AddColumn("email", p => p.Email, c => c.Header("Email")),
        (_, options) => new InMemoryGridDataSource<Person>(options, people));

var app = builder.Build();
app.MapNetOpenGrid();
app.Run();
```

No `wwwroot`, no `UseStaticFiles()`, no build tooling. The JS runtime, htmx, Alpine and the theme CSS all live **inside the package** and are served by `MapNetOpenGrid`.

### Bringing your own CSS

`CssPath` defaults to `null`, meaning the theme compiled into the assembly is served from `{AssetPrefix}/css`. To host your own stylesheet instead, set it:

```csharp
builder.Services.AddNetOpenGrid(o =>
{
    o.AssetPrefix = "/_netgrid";
    o.CssPath = "/css";          // serves wwwroot/css/netopengrid-{theme}.css from your app
    o.CssFilePrefix = "netopengrid-";
});
```

`CssFilePrefix` applies only when `CssPath` is set: it cannot rename a resource compiled into the assembly.
````

- [ ] **Step 4: Correct the themes section in both READMEs**

Five exact replacements. The old text is what is on disk today; use it to locate each site.

**4a — `README.md:247`, mermaid output node.** Replace:

```
    CLI --> OUT["wwwroot/css/netopengrid-{theme}.css<br/>(Host y Example)"]
```

with:

```
    CLI --> OUT["src/NetOpenGrid.Infrastructure/Assets/css/<br/>netopengrid-{theme}.css → EmbeddedResource"]
```

**4b — `README.en.md:247`, same node.** Replace:

```
    CLI --> OUT["wwwroot/css/netopengrid-{theme}.css<br/>(Host and Example)"]
```

with:

```
    CLI --> OUT["src/NetOpenGrid.Infrastructure/Assets/css/<br/>netopengrid-{theme}.css → EmbeddedResource"]
```

**4c — `README.md:497`, the build command.** Replace:

```bash
./tools/build-themes.sh     # compila themes/*.css → wwwroot/css (Host y Example)
```

with:

```bash
./tools/build-themes.sh              # compila themes/*.css → Infrastructure/Assets/css
./tools/build-themes.sh --strict     # falla si el CLI de tailwindcss no está en el PATH
```

**4d — `README.en.md:498`, same command.** Replace:

```bash
./tools/build-themes.sh     # compiles themes/*.css → wwwroot/css (Host and Example)
```

with:

```bash
./tools/build-themes.sh              # compiles themes/*.css → Infrastructure/Assets/css
./tools/build-themes.sh --strict     # fails if the tailwindcss CLI is not on PATH
```

**4e — add a bullet to the themes list in each README**, as the new first item (before the existing `@source "../src"` bullet).

`README.md`:

```markdown
- La salida se compila **dentro del ensamblado** como `EmbeddedResource` y la sirve
  `MapNetOpenGrid` en `{AssetPrefix}/css/netopengrid-{tema}.css`. Las apps que consumen
  el paquete no necesitan `wwwroot` ni Tailwind. `tools/pack.sh` siempre usa `--strict`,
  así que nunca se publica un paquete sin CSS compilado.
```

`README.en.md`:

```markdown
- The output is compiled **into the assembly** as an `EmbeddedResource` and served by
  `MapNetOpenGrid` at `{AssetPrefix}/css/netopengrid-{theme}.css`. Apps consuming the
  package need neither `wwwroot` nor Tailwind. `tools/pack.sh` always passes `--strict`,
  so a package without compiled CSS can never ship.
```

Finally, `README.en.md:101` has a table cell reading `themes compiled to `wwwroot/css`, dark mode` — change `wwwroot/css` to `the assembly`. Check `README.md` around line 101 for the Spanish equivalent and make the matching edit.

- [ ] **Step 5: Verify the documented commands are the ones that work**

Re-read your own snippets against what you actually ran in Task 6. The package IDs, the version string, the `nuget.config` shape and the `Program.cs` must match. Any drift here is a bug report from a future user.

- [ ] **Step 6: Commit**

```bash
git add README.md README.en.md
git commit -m "docs: document nuget installation and embedded theme delivery"
```

- [ ] **Step 7: Clean up the throwaway consumer**

```bash
rm -rf "/c/Users/eduardo/AppData/Local/Temp/claude/D--NetOpenGrid/4a3c60c7-77ff-451a-8920-beed7b1ce79e/scratchpad/GridConsumer"
```

---

## Final verification

- [ ] `dotnet build NetOpenGrid.slnx -c Release` — succeeds with 0 warnings
- [ ] `dotnet test NetOpenGrid.slnx` — all tests pass, including the new `EmbeddedThemeAssetTests`, `ThemeCssEndpointTests`, `ThemeCssLinkTests` and `SelfHostedCssLinkTests`
- [ ] `./tools/pack.sh dev.2` — produces four `.nupkg` at `0.1.0-dev.2` and nothing else
- [ ] `git status` — clean; no stray `artifacts/`, no leftover `wwwroot/css`
- [ ] Task 6 Step 7 output recorded, and Task 6 Step 8 confirmed visually
