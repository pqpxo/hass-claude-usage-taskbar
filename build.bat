@REM version 7
@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK was not found.
    echo Install the .NET 8 SDK or a newer SDK and try again.
    exit /b 1
)

echo Restoring dependencies from NuGet.org...
dotnet restore ClaudeUsageTray.sln --configfile NuGet.Config
if errorlevel 1 goto :error

echo Building Release configuration...
dotnet build ClaudeUsageTray.sln --configuration Release --no-restore
if errorlevel 1 goto :error

echo.
echo Build complete.
echo Output: ClaudeUsageTray\bin\Release\net8.0-windows\win-x64\ClaudeUsageTray.exe
exit /b 0

:error
echo.
echo Build failed. No successful output has been reported.
exit /b 1
