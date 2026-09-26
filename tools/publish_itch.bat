@echo off
rem itch.io upload: bump version -> export exe -> butler push (itch app users get the update automatically)
setlocal
cd /d "%~dp0.."

echo [1/3] Bumping version...
powershell -NoProfile -ExecutionPolicy Bypass -File tools\bump_version.ps1 || goto :error
set /p VERSION=<tools\last_version.txt
echo      version %VERSION%

echo [2/3] Exporting Windows build...
if exist build\windows rmdir /s /q build\windows
call tools\export_windows.bat
if not exist build\windows\AugmentCardBattle.exe goto :error
copy /y tools\itch.toml build\windows\.itch.toml >nul

echo [3/3] Uploading to itch.io (hamark/sp-cardgame:windows)...
tools\butler\butler.exe push build\windows hamark/sp-cardgame:windows --userversion %VERSION% || goto :error

echo.
echo Done! v%VERSION% uploaded. itch app users will update automatically.
if /i not "%1"=="nopause" pause
exit /b 0

:error
echo.
echo Failed. Check the messages above.
if /i not "%1"=="nopause" pause
exit /b 1
