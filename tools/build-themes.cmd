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

if exist "%OUT%\netopengrid-*.css" del /q "%OUT%\netopengrid-*.css"

for %%F in ("%THEMES_DIR%\*.css") do (
  set "name=%%~nF"
  echo [netopengrid] Compiling theme '!name!'...
  tailwindcss -i "%%F" -o "%OUT%\netopengrid-!name!.css" --minify --silent || exit /b 1
)

echo [netopengrid] Themes written to %OUT%
