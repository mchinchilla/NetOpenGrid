@echo off
setlocal

set "ROOT=%~dp0.."
set "SUFFIX=%~1"
set "OUT=%ROOT%\artifacts"

set "DOTNET_CLI_DISABLE_NODE_REUSE=1"
set "MSBUILDDISABLENODEREUSE=1"

echo [netopengrid] Shutting down stale build servers...
dotnet build-server shutdown || exit /b 1

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
