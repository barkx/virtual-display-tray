@echo off
net session >nul 2>&1
if %errorlevel% neq 0 (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Command "$ps=((Get-Content -LiteralPath '%~f0') | Select-Object -Skip 9) -join [Environment]::NewLine; $SelfCmdPath='%~f0'; Invoke-Expression $ps"
goto :eof
:PS1_BEGIN (not used for parsing - human marker only)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$InstallDir = Join-Path $env:ProgramData 'VirtualDisplayTray'
$LogPath    = Join-Path $InstallDir 'VirtualDisplayTray.log'
$HWID       = 'Root\IddSampleDriver'

function Write-Log {
    param([string]$Message)
    $line = "[{0}] {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    try {
        if (-not (Test-Path $InstallDir)) { New-Item -Path $InstallDir -ItemType Directory -Force | Out-Null }
        Add-Content -Path $LogPath -Value $line -Encoding UTF8
    } catch {}
}

Write-Log '--------------------------------------------------'
Write-Log 'Standalone cleanup tool starting.'

if (-not ('IddDeviceInstaller' -as [type])) {
$csharp = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class IddDeviceInstaller
{
    private const int DIGCF_PRESENT      = 0x02;
    private const int SPDRP_HARDWAREID   = 0x01;
    private const int DIF_REMOVE         = 0x05;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiCallClassInstaller(int InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, int MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, int Property, out int PropertyRegDataType, byte[] PropertyBuffer, int PropertyBufferSize, out int RequiredSize);

    private static readonly Guid GUID_DEVCLASS_DISPLAY = new Guid("4D36E968-E325-11CE-BFC1-08002BE10318");
    private const string HWID = "Root\\IddSampleDriver";

    private static bool FindDevice(out IntPtr devInfoSetOut, out SP_DEVINFO_DATA dataOut)
    {
        Guid classGuid = GUID_DEVCLASS_DISPLAY;
        IntPtr devInfoSet = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
        devInfoSetOut = devInfoSet;
        dataOut = new SP_DEVINFO_DATA();

        if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1))
            return false;

        int index = 0;
        while (true)
        {
            SP_DEVINFO_DATA data = new SP_DEVINFO_DATA();
            data.cbSize = Marshal.SizeOf(data);
            if (!SetupDiEnumDeviceInfo(devInfoSet, index, ref data))
                break;

            int regType, requiredSize;
            byte[] buffer = new byte[4096];
            if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref data, SPDRP_HARDWAREID, out regType, buffer, buffer.Length, out requiredSize) && requiredSize > 0)
            {
                string multi = Encoding.Unicode.GetString(buffer, 0, requiredSize);
                if (multi.ToUpperInvariant().Contains(HWID.ToUpperInvariant()))
                {
                    dataOut = data;
                    return true;
                }
            }
            index++;
        }

        SetupDiDestroyDeviceInfoList(devInfoSet);
        devInfoSetOut = IntPtr.Zero;
        return false;
    }

    public static int CountDevices()
    {
        int count = 0;
        Guid classGuid = GUID_DEVCLASS_DISPLAY;
        IntPtr devInfoSet = SetupDiGetClassDevs(ref classGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT);
        if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1))
            return 0;
        try
        {
            int index = 0;
            while (true)
            {
                SP_DEVINFO_DATA data = new SP_DEVINFO_DATA();
                data.cbSize = Marshal.SizeOf(data);
                if (!SetupDiEnumDeviceInfo(devInfoSet, index, ref data))
                    break;

                int regType, requiredSize;
                byte[] buffer = new byte[4096];
                if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref data, SPDRP_HARDWAREID, out regType, buffer, buffer.Length, out requiredSize) && requiredSize > 0)
                {
                    string multi = Encoding.Unicode.GetString(buffer, 0, requiredSize);
                    if (multi.ToUpperInvariant().Contains(HWID.ToUpperInvariant()))
                        count++;
                }
                index++;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(devInfoSet); }
        return count;
    }

    public static int RemoveAllDevices()
    {
        int removed = 0;
        int safety = 0;
        while (safety++ < 25)
        {
            IntPtr devInfoSet;
            SP_DEVINFO_DATA data;
            if (!FindDevice(out devInfoSet, out data))
                break;
            try
            {
                if (SetupDiCallClassInstaller(DIF_REMOVE, devInfoSet, ref data))
                    removed++;
                else
                    throw new Exception("SetupDiCallClassInstaller(REMOVE) failed (Win32 error " + Marshal.GetLastWin32Error() + ")");
            }
            finally { SetupDiDestroyDeviceInfoList(devInfoSet); }
        }
        return removed;
    }
}
'@
Add-Type -TypeDefinition $csharp -Language CSharp
}

function Remove-DriverPackage {
    try {
        $enum = & pnputil.exe /enum-drivers 2>&1
        $pending = $null
        $publishedNames = @()
        foreach ($line in $enum) {
            if ($line -match '^Published Name\s*:\s*(oem\d+\.inf)') { $pending = $Matches[1] }
            elseif ($line -match '^Original Name\s*:\s*(.+)$') {
                if ($Matches[1].Trim().ToLower() -eq 'iddsampledriver.inf' -and $pending) { $publishedNames += $pending; $pending = $null }
            }
        }
        foreach ($name in $publishedNames) {
            Write-Log "Removing driver package $name from driver store..."
            & pnputil.exe /delete-driver $name /uninstall /force 2>&1 | ForEach-Object { Write-Log "pnputil: $_" }
        }
        return $publishedNames.Count
    } catch {
        Write-Log "Driver package cleanup skipped: $_"
        return 0
    }
}

try {
    $before = [IddDeviceInstaller]::CountDevices()
    Write-Log "Found $before existing virtual display device(s) before cleanup."

    $removedDevices = [IddDeviceInstaller]::RemoveAllDevices()
    Write-Log "Removed $removedDevices device instance(s)."

    $removedPackages = Remove-DriverPackage

    $after = [IddDeviceInstaller]::CountDevices()
    Write-Log "Devices remaining after cleanup: $after"

    $msg = if ($before -eq 0) {
        "No existing virtual display devices were found - nothing to remove."
    } else {
        "Removed $removedDevices old virtual display device(s) and cleaned up $removedPackages driver package entry/entries.`n`nYou can now use the tray app to turn the virtual display back on with a clean single device."
    }
    [System.Windows.Forms.MessageBox]::Show($msg, 'Virtual Display Cleanup', 'OK', 'Information') | Out-Null
} catch {
    Write-Log "CLEANUP FAILED: $_"
    [System.Windows.Forms.MessageBox]::Show("Cleanup failed: $($_.Exception.Message)`n`nSee the log for details:`n$LogPath", 'Virtual Display Cleanup', 'OK', 'Error') | Out-Null
}
