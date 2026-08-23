@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0.."
set "THEMES_DIR=%ROOT%\themes"
set "OUT1=%ROOT%\src\NetOpenGrid.Host\wwwroot\css"
set "OUT2=%ROOT%\samples\NetOpenGrid.Example\wwwroot\css"

if not exist "%OUT1%" mkdir "%OUT1%"
if not exist "%OUT2%" mkdir "%OUT2%"

where tailwindcss >nul 2>nul
if errorlevel 1 (
  echo [netopengrid] WARNING: 'tailwindcss' CLI not found in PATH; skipping theme compilation. 1>&2
  exit /b 0
)

for %%F in ("%THEMES_DIR%\*.css") do (
  set "name=%%~nF"
  echo [netopengrid] Compiling theme '!name!'...
  tailwindcss -i "%%F" -o "%OUT1%\netopengrid-!name!.css" --minify --silent || exit /b 1
  tailwindcss -i "%%F" -o "%OUT2%\netopengrid-!name!.css" --minify --silent || exit /b 1
)

echo [netopengrid] Themes written to %OUT1% and %OUT2%
