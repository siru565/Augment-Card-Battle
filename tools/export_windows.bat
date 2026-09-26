@echo off
rem Windows exe export (Steam files are copied next to the exe)
set GODOT=C:\Users\hamar\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe
if not exist "%GODOT%" set GODOT=C:\Users\hamar\Desktop\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe
rem .NET SDK must be on PATH for the C# publish step
if exist "C:\Program Files\dotnet\dotnet.exe" set "PATH=C:\Program Files\dotnet;%PATH%"
if exist "C:\Program Files\dotnet\dotnet.exe" set "DOTNET_ROOT=C:\Program Files\dotnet"
rem Do not leave MSBuild/compiler helper processes running after export (they kept the script waiting for minutes)
set MSBUILDDISABLENODEREUSE=1
set DOTNET_CLI_USE_MSBUILD_SERVER=0
set UseSharedCompilation=false
cd /d "%~dp0.."
if not exist build\windows mkdir build\windows
"%GODOT%" --headless --path . --export-release "Windows Desktop" build\windows\AugmentCardBattle.exe
copy /y steam_api64.dll build\windows\ >nul
copy /y steam_appid.txt build\windows\ >nul
rem Font/asset licenses (OFL requires the license to ship with the fonts)
if not exist build\windows\licenses mkdir build\windows\licenses
copy /y assets\fonts\*.txt build\windows\licenses\ >nul
copy /y assets\ui\KENNEY_LICENSE.txt build\windows\licenses\Kenney-UI-LICENSE.txt >nul
copy /y audio\sfx\KENNEY_LICENSE.txt build\windows\licenses\Kenney-Audio-LICENSE.txt >nul
copy /y assets\textures\LICENSE.txt build\windows\licenses\ambientCG-LICENSE.txt >nul
echo Export done: %CD%\build\windows
