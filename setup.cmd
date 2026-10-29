@echo off
setlocal

cd /d "%~dp0"

echo.
echo === Checking .NET SDK ===
dotnet --version
if errorlevel 1 goto failure

echo.
echo === Preparing local tools ===
if not exist ".config" mkdir ".config"

if not exist ".config\dotnet-tools.json" (
    dotnet new tool-manifest
    if errorlevel 1 goto failure

    dotnet tool install dotnet-ef --version "10.*"
    if errorlevel 1 goto failure
) else (
    dotnet tool restore
    if errorlevel 1 goto failure
)

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