@echo off
setlocal

cd /d "%~dp0"

if not exist "desktop\Workbench.Desktop.exe" (
    echo Desktop executable is missing.
    goto failure
)

echo.
echo Checking the backend...

powershell.exe -NoProfile -Command "try { $response = Invoke-WebRequest -Uri 'http://localhost:5050/' -UseBasicParsing -TimeoutSec 5; if ($response.StatusCode -ne 200) { exit 1 }; exit 0 } catch { exit 1 }"

if errorlevel 1 (
    echo Start package-start-api.cmd first.
    goto failure
)

cd /d "%~dp0desktop"

echo Starting the packaged desktop application...
Workbench.Desktop.exe
if errorlevel 1 goto failure

exit /b 0

:failure
echo.
echo The packaged desktop could not start.
pause
exit /b 1