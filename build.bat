@echo off
REM Rebuild NetSpeedTray.exe using the C# compiler built into Windows (.NET Framework).
REM No SDK or downloads required.
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu ^
  /win32manifest:"%~dp0app.manifest" ^
  /win32icon:"%~dp0app.ico" ^
  /out:"%~dp0NetSpeedTray.exe" ^
  /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Core.dll ^
  "%~dp0Program.cs"
if %errorlevel%==0 (echo Build OK: %~dp0NetSpeedTray.exe) else (echo Build FAILED)
pause
