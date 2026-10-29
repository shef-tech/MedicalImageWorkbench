@echo off
setlocal

cd /d "%~dp0"

if not exist "src\Workbench.Api\bin\Release\net10.0\Workbench.Api.dll" (
    echo Release build not found. Run setup.cmd first.
    pause
    exit /b 1
)

if not exist "src\Workbench.Api\App_Data\workbench.db" (
    echo Database not found. Run setup.cmd first.
    pause
    exit /b 1
)

echo.
echo Starting the backend at http://localhost:5050
echo Keep this window open while using the desktop application.
echo Press Ctrl+C to stop the backend.
echo.

dotnet run --project src\Workbench.Api --configuration Release --no-build --no-launch-profile --urls http://localhost:5050

if errorlevel 1 (
    echo.
    echo Backend stopped with an error.
    pause
    exit /b 1
)

exit /b 0