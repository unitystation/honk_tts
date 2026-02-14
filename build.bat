@echo off
setlocal

set "ROOT=%~dp0"
set "OUT=%ROOT%build"

echo Cleaning output directory...
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"
mkdir "%OUT%\scripts"

echo Copying server files...
copy "%ROOT%server\tts_server.py" "%OUT%\" >nul
copy "%ROOT%server\requirements.txt" "%OUT%\" >nul
copy "%ROOT%server\scripts\start_tts.bat" "%OUT%\scripts\" >nul
copy "%ROOT%server\scripts\start_tts.sh" "%OUT%\scripts\" >nul
copy "%ROOT%server\scripts\test_server.py" "%OUT%\scripts\" >nul
copy "%ROOT%server\scripts\test_tts.bat" "%OUT%\scripts\" >nul
copy "%ROOT%server\scripts\test_tts.sh" "%OUT%\scripts\" >nul

echo Building installer (Release, Native AOT, win-x64)...
dotnet publish "%ROOT%installer\src\HonkTTS.Installer\HonkTTS.Installer.csproj" ^
    -r win-x64 -c Release ^
    -o "%OUT%" ^
    --nologo

if %errorlevel% neq 0 (
    echo Build failed.
    exit /b 1
)

echo.
echo Build complete: %OUT%
dir /b "%OUT%"
