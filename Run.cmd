@echo off
setlocal
cd /d "%~dp0"
set "OEPS_DOTNET=dotnet"
if exist "%~dp0.tools\dotnet\dotnet.exe" set "OEPS_DOTNET=%~dp0.tools\dotnet\dotnet.exe"
if not exist "src\Oeps.RawMaterialSticker.App\bin\Release\net10.0-windows\Oeps.RawMaterialSticker.App.dll" (
  "%OEPS_DOTNET%" build src\Oeps.RawMaterialSticker.App -c Release
  if errorlevel 1 goto failed
)
"%OEPS_DOTNET%" "src\Oeps.RawMaterialSticker.App\bin\Release\net10.0-windows\Oeps.RawMaterialSticker.App.dll" %*
:failed
if errorlevel 1 pause
