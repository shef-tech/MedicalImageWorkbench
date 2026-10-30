@echo off
setlocal

cd /d "%~dp0api"

if not exist "Workbench.Api.exe" (
    echo Backend executable is missing.
    goto failure
)

if not exist "efbundle.exe" (
    echo Database migration executable is missing.
    goto failure
)

echo.
echo Checking whether port 5050 is already in use...

powershell.exe -NoProfile -Command "try { $client = New-Object System.Net.Sockets.TcpClient; try { $task = $client.ConnectAsync('127.0.0.1',5050); if ($task.Wait(1500) -and $client.Connected) { exit 1 }; exit 0 } finally { $client.Dispose() } } catch { exit 0 }"

if errorlevel 1 (
    echo Port 5050 is already in use.
    echo Stop the existing backend before starting this package.
    goto failure
)

if not exist "App_Data" mkdir "App_Data"
if not exist "App_Data" goto failure

echo.
echo Initializing the package database...

efbundle.exe --connection "Data Source=App_Data\workbench.db"
if errorlevel 1 goto failure

echo.
echo Starting the packaged backend at http://localhost:5050
echo Keep this window open.
echo Press Ctrl+C to stop.
echo.

Workbench.Api.exe --urls http://localhost:5050
if errorlevel 1 goto failure

exit /b 0

:failure
echo.
echo The packaged backend could not start.
echo Read the message above.
pause
exit /b 1