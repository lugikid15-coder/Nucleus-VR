@echo off
setlocal EnableExtensions DisableDelayedExpansion

for %%P in ("%~dp0.") do set "NUCLEUS_PROJECT=%%~fP"
set "NUCLEUS_VERSION="
if not exist "%NUCLEUS_PROJECT%\ProjectSettings\ProjectVersion.txt" goto missing_project
for /f "usebackq tokens=1,2" %%A in ("%NUCLEUS_PROJECT%\ProjectSettings\ProjectVersion.txt") do (
    if "%%A"=="m_EditorVersion:" set "NUCLEUS_VERSION=%%B"
)
if not defined NUCLEUS_VERSION goto missing_project
set "NUCLEUS_DOWNLOAD=https://unity.com/releases/editor/whats-new/%NUCLEUS_VERSION%"

set "NUCLEUS_CHECK_ONLY="
if /i "%~1"=="--check" (
    set "NUCLEUS_CHECK_ONLY=1"
    shift
)
if /i "%~1"=="--install" goto open_installer

set "NUCLEUS_EDITOR=%~1"
if not defined NUCLEUS_EDITOR set "NUCLEUS_EDITOR=%ProgramFiles%\Unity\Hub\Editor\%NUCLEUS_VERSION%\Editor\Unity.exe"
if not exist "%NUCLEUS_EDITOR%" goto missing_editor
for %%E in ("%NUCLEUS_EDITOR%") do set "NUCLEUS_REFERENCES=%%~dpEData\UnityReferenceAssemblies\unity-4.8-api"

if not exist "%NUCLEUS_REFERENCES%\Facades\*.dll" (
    echo Missing Unity Editor references:
    echo   "%NUCLEUS_REFERENCES%\Facades\*.dll"
    goto incomplete_editor
)
for %%D in (mscorlib.dll System.dll System.Core.dll) do (
    if not exist "%NUCLEUS_REFERENCES%\%%D" (
        echo Missing Unity Editor reference:
        echo   "%NUCLEUS_REFERENCES%\%%D"
        goto incomplete_editor
    )
)

if defined NUCLEUS_CHECK_ONLY (
    echo Unity Editor reference files found at "%NUCLEUS_REFERENCES%".
    exit /b 0
)
start "" "%NUCLEUS_EDITOR%" -projectPath "%NUCLEUS_PROJECT%"
exit /b %errorlevel%

:missing_project
echo Keep Open-NucleusVR.cmd in the extracted NUCLEUS VR project folder.
if /i not "%~1"=="--check" pause
exit /b 3

:missing_editor
echo Unity.exe was not found at "%NUCLEUS_EDITOR%".
echo Install Unity %NUCLEUS_VERSION%: %NUCLEUS_DOWNLOAD%
echo For a custom location, pass the full path to Editor\Unity.exe.
if not defined NUCLEUS_CHECK_ONLY pause
exit /b 1

:incomplete_editor
echo Unity checks these Editor files before importing the project.
echo Reinstall Unity %NUCLEUS_VERSION% through Unity Hub or install a fresh copy:
echo   %NUCLEUS_DOWNLOAD%
echo For a fresh copy in another folder, pass its full Editor\Unity.exe path.
if not defined NUCLEUS_CHECK_ONLY pause
exit /b 2

:open_installer
start "" "%NUCLEUS_DOWNLOAD%"
exit /b %errorlevel%
