@echo off
setlocal

cd /d "%~dp0"

echo.
echo === Checking .NET SDK ===
dotnet --version
if errorlevel 1 goto failure

echo.
echo === Finding the local tool manifest ===

set "WorkbenchToolManifest="

if exist ".config\dotnet-tools.json" (
    set "WorkbenchToolManifest=.config\dotnet-tools.json"
)

if exist "dotnet-tools.json" (
    set "WorkbenchToolManifest=dotnet-tools.json"
)

if defined WorkbenchToolManifest goto manifestReady

echo Creating a tool manifest...
dotnet new tool-manifest
if errorlevel 1 goto failure

if exist ".config\dotnet-tools.json" (
    set "WorkbenchToolManifest=.config\dotnet-tools.json"
)

if exist "dotnet-tools.json" (
    set "WorkbenchToolManifest=dotnet-tools.json"
)

if not defined WorkbenchToolManifest (
    echo Could not locate the created tool manifest.
    goto failure
)

:manifestReady
echo Using %WorkbenchToolManifest%

echo.
echo === Checking Entity Framework tool registration ===

powershell.exe -NoProfile -Command "try { $manifest = Get-Content -LiteralPath $env:WorkbenchToolManifest -Raw | ConvertFrom-Json; if ($null -eq $manifest.tools.'dotnet-ef') { exit 1 }; exit 0 } catch { exit 2 }"

if errorlevel 2 goto failure
if errorlevel 1 goto installEfTool
goto restoreTools

:installEfTool
dotnet tool install dotnet-ef --version "10.*" --tool-manifest "%WorkbenchToolManifest%"
if errorlevel 1 goto failure

:restoreTools
echo.
echo === Restoring local tools ===
dotnet tool restore --tool-manifest "%WorkbenchToolManifest%"
if errorlevel 1 goto failure

echo.
echo === Checking Entity Framework tool ===
dotnet ef --version
if errorlevel 1 goto failure

echo.
echo === Restoring dependencies ===
dotnet restore MedicalImageWorkbench.slnx
if errorlevel 1 goto failure

echo.
echo === Building Release configuration ===
dotnet build MedicalImageWorkbench.slnx --configuration Release --no-restore
if errorlevel 1 goto failure

echo.
echo === Running automated tests ===
dotnet test tests\Workbench.Tests\Workbench.Tests.csproj --configuration Release --no-build --no-restore
if errorlevel 1 goto failure

echo.
echo === Initializing the application database ===
dotnet ef database update --project src\Workbench.Infrastructure --startup-project src\Workbench.Api --configuration Release --no-build
if errorlevel 1 goto failure

echo.
echo Setup completed successfully.
echo.
echo Run start-api.cmd first.
echo Then run start-desktop.cmd in another Command Prompt.
echo.
pause
exit /b 0

:failure
echo.
echo Setup failed. Read the error above.
echo Stop any running Workbench applications before retrying.
echo.
pause
exit /b 1