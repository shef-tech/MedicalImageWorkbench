@echo off
setlocal

cd /d "%~dp0"

if not exist "src\Workbench.Desktop\bin\Release\net10.0\Workbench.Desktop.dll" (
    echo Release build not found. Run setup.cmd first.
    pause
    exit /b 1
)

echo.
echo Checking the backend connection...

powershell.exe -NoProfile -Command "try { $response = Invoke-WebRequest -Uri 'http://localhost:5050/' -UseBasicParsing -TimeoutSec 5; if ($response.StatusCode -ne 200) { exit 1 }; exit 0 } catch { exit 1 }"

if errorlevel 1 (
    echo.
    echo Cannot reach the backend.
    echo Run start-api.cmd in another window first.
    pause
    exit /b 1
)

echo.
echo Starting the desktop application...
echo.

dotnet run --project src\Workbench.Desktop --configuration Release --no-build

if errorlevel 1 (
    echo.
    echo Desktop application stopped with an error.
    pause
    exit /b 1
)

exit /b 0