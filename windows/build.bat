@echo off
rem Builds QR-bridge.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem Nothing needs to be installed.
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4 is required ^(built into Windows 10/11^).
  pause
  exit /b 1
)
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:QR-bridge.exe ^
  /win32icon:qr-bridge.ico ^
  /resource:..\shared\page.html,QrBridge.page.html ^
  /resource:..\shared\strings.ini,QrBridge.strings.ini ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  src\*.cs
if errorlevel 1 (
  echo.
  echo BUILD FAILED - please report the error text above.
  pause
  exit /b 1
)
echo.
echo OK: QR-bridge.exe created.
if /i not "%1"=="nostart" start "" QR-bridge.exe
