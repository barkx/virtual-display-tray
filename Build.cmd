@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo Looking for the .NET Framework C# compiler (csc.exe)...
set "CSC="
for %%P in (
    "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
) do (
    if exist %%P (
        set "CSC=%%~P"
    )
)

if "%CSC%"=="" (
    echo.
    echo Could not find csc.exe under %WINDIR%\Microsoft.NET\...
    echo This ships with .NET Framework 4, present on Windows 7 SP1 and later.
    echo If you are seeing this, your Windows install may be missing .NET
    echo Framework 4.x - install it from Windows Update / Optional Features
    echo and try again.
    echo.
    if not defined CI pause
    exit /b 1
)

echo Using compiler: %CSC%
echo.

for %%F in (
    driver\IddSampleDriver.inf
    driver\IddSampleDriver.dll
    driver\iddsampledriver.cat
    driver\Virtual_Display_Driver.cer
    driver\option.txt
    src\app.manifest
    src\Program.cs
    assets\virtual-desktop.ico
) do (
    if not exist "%%F" (
        echo Missing required file: %%F
        echo Run Build.cmd from the repository root - it expects the src, driver and assets folders.
        if not defined CI pause
        exit /b 1
    )
)

if not exist bin mkdir bin

"%CSC%" /nologo /target:winexe /platform:anycpu /out:bin\VirtualDisplayTray.exe ^
    /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
    /resource:driver\IddSampleDriver.inf,DriverFiles.IddSampleDriver.inf ^
    /resource:driver\IddSampleDriver.dll,DriverFiles.IddSampleDriver.dll ^
    /resource:driver\iddsampledriver.cat,DriverFiles.iddsampledriver.cat ^
    /resource:driver\Virtual_Display_Driver.cer,DriverFiles.Virtual_Display_Driver.cer ^
    /resource:driver\option.txt,DriverFiles.option.txt ^
    /win32manifest:src\app.manifest ^
    /win32icon:assets\virtual-desktop.ico ^
    src\Program.cs

if errorlevel 1 (
    echo.
    echo Build FAILED. See the compiler errors above.
    if not defined CI pause
    exit /b 1
)

echo.
echo ============================================================
echo  Build succeeded: bin\VirtualDisplayTray.exe
echo.
echo  Double-click bin\VirtualDisplayTray.exe to install and run it.
echo  The .exe is self-contained - the driver files are embedded.
echo ============================================================
echo.
if not defined CI pause
