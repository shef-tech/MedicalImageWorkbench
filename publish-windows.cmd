@echo off
setlocal

cd /d "%~dp0"

rem Use a unique output folder so existing packages are preserved.
for /f %%G in ('powershell.exe -NoProfile -Command "[guid]::NewGuid().ToString('N')"') do set "WorkbenchPackageId=%%G"

if not defined WorkbenchPackageId goto failure

set "WorkbenchPackage=%CD%\artifacts\windows-x64-%WorkbenchPackageId%"

echo.
echo Package destination:
echo %WorkbenchPackage%

echo.
echo === Restoring local tools ===
dotnet tool restore
if errorlevel 1 goto failure

echo.
echo === Running Release tests ===
dotnet test tests\Workbench.Tests\Workbench.Tests.csproj --configuration Release
if errorlevel 1 goto failure

echo.
echo === Publishing the backend ===
dotnet publish src\Workbench.Api\Workbench.Api.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false --output "%WorkbenchPackage%\api"
if errorlevel 1 goto failure

echo.
echo === Publishing the desktop ===
dotnet publish src\Workbench.Desktop\Workbench.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false --output "%WorkbenchPackage%\desktop"
if errorlevel 1 goto failure

echo.
echo === Checking package contents ===

rem Do not distribute a package containing runtime application data.
if exist "%WorkbenchPackage%\api\App_Data" (
    echo Runtime application data was included unexpectedly.
    echo Package verification failed. Do not distribute this folder.
    goto failure
)

if exist "%WorkbenchPackage%\desktop\App_Data" (
    echo Runtime application data was included unexpectedly.
    echo Package verification failed. Do not distribute this folder.
    goto failure
)

if not exist "%WorkbenchPackage%\api\Workbench.Api.exe" (
    echo Backend executable is missing.
    goto failure
)

if not exist "%WorkbenchPackage%\desktop\Workbench.Desktop.exe" (
    echo Desktop executable is missing.
    goto failure
)

echo.
echo === Creating the standalone database migration tool ===
dotnet ef migrations bundle --project src\Workbench.Infrastructure --startup-project src\Workbench.Api --configuration Release --self-contained --target-runtime win-x64 --output "%WorkbenchPackage%\api\efbundle.exe"
if errorlevel 1 goto failure

echo.
echo === Saving the package location ===
> "%CD%\artifacts\latest-package.txt" echo %WorkbenchPackage%

echo.
echo Publish completed successfully.
echo.
echo Package location:
echo %WorkbenchPackage%
echo.
echo Keep all published files together.
echo Launch scripts will be added next.
echo.
pause
exit /b 0

:failure
echo.
echo Publishing did not complete.
echo Read the error above before retrying.
echo Existing files have not been deleted.
echo.
pause
exit /b 1