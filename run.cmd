@echo off
cd /d "%~dp0"
if exist "release-v0.12.3\Mica.exe" (
 start "" "%~dp0release-v0.12.3\Mica.exe"
 exit /b 0
)
if exist "release-v0.12.0\Mica.exe" (
 start "" "%~dp0release-v0.12.0\Mica.exe"
 exit /b 0
)
if exist "release-v0.11.3\Mica.exe" (
 start "" "%~dp0release-v0.11.3\Mica.exe"
 exit /b 0
)
if exist "release-v0.11.2\Mica.exe" (
 start "" "%~dp0release-v0.11.2\Mica.exe"
 exit /b 0
)
if exist "release-v0.11.1\Mica.exe" (
 start "" "%~dp0release-v0.11.1\Mica.exe"
 exit /b 0
)
if exist "release-v0.11\Mica.exe" (
 start "" "%~dp0release-v0.11\Mica.exe"
 exit /b 0
)
if exist "release-v0.10.1\Mica.exe" (
 start "" "%~dp0release-v0.10.1\Mica.exe"
 exit /b 0
)
if exist "release-v0.10\Mica.exe" (
 start "" "%~dp0release-v0.10\Mica.exe"
 exit /b 0
)
if exist "release-v0.9.2\Mica.exe" (
 start "" "%~dp0release-v0.9.2\Mica.exe"
 exit /b 0
)
if exist "release-v0.9.1\Mica.exe" (
 start "" "%~dp0release-v0.9.1\Mica.exe"
 exit /b 0
)
if exist "release-v0.9\Mica.exe" (
 start "" "%~dp0release-v0.9\Mica.exe"
 exit /b 0
)
if exist "release-v0.8.4\Mica.exe" (
 start "" "%~dp0release-v0.8.4\Mica.exe"
 exit /b 0
)
if exist "release-v0.8.3\Mica.exe" (
 start "" "%~dp0release-v0.8.3\Mica.exe"
 exit /b 0
)
if exist "release-v0.8.2\Mica.exe" (
 start "" "%~dp0release-v0.8.2\Mica.exe"
 exit /b 0
)
if exist "release-v0.8.1\Mica.exe" (
 start "" "%~dp0release-v0.8.1\Mica.exe"
 exit /b 0
)
if exist "release-v0.8\Mica.exe" (
 start "" "%~dp0release-v0.8\Mica.exe"
 exit /b 0
)
if exist "release-v0.7\Mica.exe" (
 start "" "%~dp0release-v0.7\Mica.exe"
 exit /b 0
)
if exist "release-v0.6.1\Mica.exe" (
 start "" "%~dp0release-v0.6.1\Mica.exe"
 exit /b 0
)
if exist "release-v0.6\Mica.exe" (
 start "" "%~dp0release-v0.6\Mica.exe"
 exit /b 0
)
if exist "release-v0.5\Mica.exe" (
 start "" "%~dp0release-v0.5\Mica.exe"
 exit /b 0
)
if exist "release-v0.4\Mica.exe" (
 start "" "%~dp0release-v0.4\Mica.exe"
 exit /b 0
)
if exist "release-v0.3\Mica.exe" (
 start "" "%~dp0release-v0.3\Mica.exe"
 exit /b 0
)
if exist "release-v0.2\Mica.exe" (
 start "" "%~dp0release-v0.2\Mica.exe"
 exit /b 0
)
if exist "release\Mica.exe" (
 start "" "%~dp0release\Mica.exe"
 exit /b 0
)
if not exist "dist\Mica.exe" (
 powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
 if errorlevel 1 exit /b 1
)
start "" "%~dp0dist\Mica.exe"
