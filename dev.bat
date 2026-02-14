@echo off
setlocal

set "ROOT=%~dp0"
set "TARGET=%ROOT%tts"

call "%ROOT%build.bat"
if errorlevel 1 exit /b %errorlevel%

"%ROOT%build\HonkTTS.Installer.exe" "%TARGET%"
if errorlevel 1 exit /b %errorlevel%

call "%ROOT%tts\test_tts.bat" --keep %*
exit /b %errorlevel%
