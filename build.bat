@echo off
set CSC="C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist %CSC% (
    echo [ERROR] csc.exe not found at %CSC%
    pause
    exit /b 1
)

echo Building DeskMonitor.exe with icon and manifest...
%CSC% /nologo /target:winexe /optimize+ /platform:anycpu /win32icon:app.ico /win32manifest:app.manifest /out:DeskMonitor.exe /r:System.dll,System.Drawing.dll,System.Windows.Forms.dll AssemblyInfo.cs AppSettings.cs SystemMetrics.cs GpuMonitor.cs NetworkHelper.cs ProcessCpuTracker.cs TopProcessesForm.cs SettingsForm.cs MainForm.cs Program.cs

if %ERRORLEVEL% equ 0 (
    echo [SUCCESS] Build succeeded: DeskMonitor.exe
) else (
    echo [ERROR] Build failed!
    exit /b %ERRORLEVEL%
)
