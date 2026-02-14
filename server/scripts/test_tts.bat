@echo off
setlocal

set "INSTALL_DIR=%~dp0.."
set "ESPEAK_DATA_PATH=%INSTALL_DIR%\espeak-ng\espeak-ng-data"
set "PATH=%INSTALL_DIR%\espeak-ng;%PATH%"

call "%INSTALL_DIR%\venv\Scripts\activate.bat"
python "%INSTALL_DIR%\scripts\test_server.py" %*
