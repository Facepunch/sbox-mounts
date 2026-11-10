@echo off
setlocal enabledelayedexpansion

set "ROOT=%CD%"
set "PROJECTS="

for /r "%ROOT%" %%F in (*.sbproj) do set PROJECTS=!PROJECTS! "%%F"

if not defined PROJECTS (
    echo No .sbproj files found under %ROOT%
    exit /b 1
)

cd /d "%FACEPUNCH_ENGINE%\..\"
if errorlevel 1 exit /b 1

dotnet run --project .\engine\Tools\SboxBuild\SboxBuild.csproj -- build-mount --project !PROJECTS!
exit /b %errorlevel%