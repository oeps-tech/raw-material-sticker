@echo off
setlocal
set "OEPS_MSI=%~dp0Oeps.RawMaterialSticker-0.1.5-setup-win-x64.msi"
set "OEPS_LOG=%TEMP%\OEPS-install.log"
if not exist "%OEPS_MSI%" (
  echo Put this file next to Oeps.RawMaterialSticker-0.1.5-setup-win-x64.msi and run it again.
  pause
  exit /b 1
)
echo Windows account: %USERDOMAIN%\%USERNAME%
echo Expected installation folder: %LOCALAPPDATA%\OEPS Raw Material Sticker Installer
echo Select Repair if Windows offers it. This records the installation details.
start "" /wait msiexec.exe /i "%OEPS_MSI%" /L*V "%OEPS_LOG%"
set "OEPS_RESULT=%ERRORLEVEL%"
echo Installer exit code: %OEPS_RESULT%
echo Log: %OEPS_LOG%
if exist "%LOCALAPPDATA%\OEPS Raw Material Sticker Installer\launcher\Oeps.RawMaterialSticker.Launcher.exe" (
  echo Launcher found in the expected installation folder.
) else (
  echo Launcher is missing from the expected installation folder.
)
if exist "%OEPS_LOG%" explorer.exe /select,"%OEPS_LOG%"
pause
