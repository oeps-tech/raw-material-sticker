@echo off
setlocal
cd /d "%~dp0"
set "OEPS_DOTNET=dotnet"
if exist "%~dp0.tools\dotnet\dotnet.exe" set "OEPS_DOTNET=%~dp0.tools\dotnet\dotnet.exe"
"%OEPS_DOTNET%" "src\Oeps.RawMaterialSticker.App\bin\Release\net10.0-windows\Oeps.RawMaterialSticker.App.dll" --printer-test
if errorlevel 1 pause
