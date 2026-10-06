@echo off
setlocal
cd /d "%~dp0"
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if exist ClaudeVpnGuard.exe del /f /q ClaudeVpnGuard.exe >nul 2>&1
if exist ClaudeVpnGuard.exe (
    if exist ClaudeVpnGuard.old.exe del /f /q ClaudeVpnGuard.old.exe >nul 2>&1
    ren ClaudeVpnGuard.exe ClaudeVpnGuard.old.exe
)
%CSC% /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /out:ClaudeVpnGuard.exe /win32manifest:src\app.manifest /win32icon:src\app.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll /r:System.Core.dll src\*.cs
if errorlevel 1 exit /b 1
copy /y src\app.config ClaudeVpnGuard.exe.config >nul
