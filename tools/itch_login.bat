@echo off
rem One-time itch.io login for butler. A browser window will open - approve it there.
cd /d "%~dp0"
butler\butler.exe login
pause
