@echo off
setlocal
rem Build CodexAppInstaller.exe from CodexAppInstaller.cs (self-contained C#)
rem Requires .NET Framework 4.x compiler + PowerShell SDK (both ship with Windows).

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found. .NET Framework 4.x required.
    exit /b 1
)

set "PSDLL=%WINDIR%\System32\WindowsPowerShell\v1.0\System.Management.Automation.dll"
if not exist "%PSDLL%" set "PSDLL=%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\System.Management.Automation\v4.0_3.0.0.0__31bf3856ad364e35\System.Management.Automation.dll"
if not exist "%PSDLL%" (
    echo [ERROR] System.Management.Automation.dll not found.
    exit /b 1
)

cd /d "%~dp0"
"%CSC%" /nologo /target:exe /optimize /platform:x64 /out:CodexAppInstaller.exe /r:"%PSDLL%" CodexAppInstaller.cs
if errorlevel 1 (
    echo [ERROR] compile failed.
    exit /b 1
)
echo [OK] CodexAppInstaller.exe built.
endlocal
