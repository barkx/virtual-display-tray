using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace VirtualDisplayTrayApp
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "Global\\VirtualDisplayTray_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "Virtual Display Tray is already running (check your system tray).",
                        "Virtual Display Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (var context = new TrayAppContext())
                {
                    Application.Run(context);
                }

                mutex.ReleaseMutex();
            }
        }
    }

    internal sealed class TrayAppContext : ApplicationContext
    {
        private readonly string _installDir;
        private readonly string _logPath;
        private readonly string _taskName = "VirtualDisplayTray";
        private readonly string _infPath;
        private readonly string _installedExePath;
        private bool _isFirstRun;

        private NotifyIcon _notifyIcon;
        private ToolStripMenuItem _toggleItem;
        private Icon _iconOn;
        private Icon _iconOff;
        private Icon _iconBusy;
        private string _state = "unknown";

        public TrayAppContext()
        {
            _installDir = Path.Combine(Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData", "VirtualDisplayTray");
            _logPath = Path.Combine(_installDir, "VirtualDisplayTray.log");
            _infPath = Path.Combine(_installDir, "IddSampleDriver.inf");
            _installedExePath = Path.Combine(_installDir, "VirtualDisplayTray.exe");

            Log("--------------------------------------------------");
            Log("Starting VirtualDisplayTray.exe");

            try
            {
                EnsureUnpacked();
            }
            catch (Exception ex)
            {
                Log("First-run setup FAILED: " + ex);
                MessageBox.Show(
                    "Setup failed: " + ex.Message + "\n\nSee the log for details:\n" + _logPath,
                    "Virtual Display Tray", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            BuildTrayIcon();
            UpdateTrayState();

            if (_isFirstRun)
            {
                ShowBalloon("Virtual Display Tray",
                    "Installed. Click the tray icon anytime to turn the virtual display on or off.",
                    ToolTipIcon.Info);
            }

            Log("Tray ready. Initial state: " + _state);
        }

        // -----------------------------------------------------------------
        // Logging
        // -----------------------------------------------------------------
        private void Log(string message)
        {
            try
            {
                if (!Directory.Exists(_installDir)) Directory.CreateDirectory(_installDir);
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine;
                File.AppendAllText(_logPath, line);
            }
            catch
            {
                // Never let logging failures crash the app.
            }
        }

        // -----------------------------------------------------------------
        // First-run setup: unpack embedded driver files, trust cert,
        // register the logon scheduled task.
        // -----------------------------------------------------------------
        private void EnsureUnpacked()
        {
            string dllPath = Path.Combine(_installDir, "IddSampleDriver.dll");
            _isFirstRun = !File.Exists(dllPath);
            if (!_isFirstRun) return;

            Log("First run detected - unpacking driver files...");
            Directory.CreateDirectory(_installDir);

            ExtractResource("DriverFiles.IddSampleDriver.inf", _infPath);
            ExtractResource("DriverFiles.IddSampleDriver.dll", dllPath);
            ExtractResource("DriverFiles.iddsampledriver.cat", Path.Combine(_installDir, "iddsampledriver.cat"));
            string cerPath = Path.Combine(_installDir, "Virtual_Display_Driver.cer");
            ExtractResource("DriverFiles.Virtual_Display_Driver.cer", cerPath);
            ExtractResource("DriverFiles.option.txt", Path.Combine(_installDir, "option.txt"));

            try
            {
                string currentExe = Assembly.GetExecutingAssembly().Location;
                if (!string.Equals(Path.GetFullPath(currentExe), Path.GetFullPath(_installedExePath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(currentExe, _installedExePath, true);
                }
            }
            catch (Exception ex)
            {
                Log("Could not copy exe into install dir: " + ex.Message);
            }

            Log("Trusting driver certificate...");
            RunHidden("certutil.exe", "-addstore -f root \"" + cerPath + "\"");
            RunHidden("certutil.exe", "-addstore -f TrustedPublisher \"" + cerPath + "\"");

            Log("Registering logon scheduled task...");
            try
            {
                string exeForTask = File.Exists(_installedExePath) ? _installedExePath : Assembly.GetExecutingAssembly().Location;
                RunHidden("schtasks.exe",
                    "/Create /TN \"" + _taskName + "\" /TR \"\\\"" + exeForTask + "\\\"\" /SC ONLOGON /RL HIGHEST /F");
            }
            catch (Exception ex)
            {
                Log("Scheduled task registration failed: " + ex.Message);
            }
        }

        private static void ExtractResource(string resourceName, string destPath)
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new Exception("Embedded resource not found: " + resourceName);
                using (var fileStream = File.Create(destPath))
                {
                    stream.CopyTo(fileStream);
                }
            }
        }

        private int RunHidden(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (!string.IsNullOrEmpty(stdout.Trim())) Log(exe + " stdout: " + stdout.Trim());
                    if (!string.IsNullOrEmpty(stderr.Trim())) Log(exe + " stderr: " + stderr.Trim());
                    Log(exe + " " + args + " => exit code " + p.ExitCode);
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Log("Failed to run " + exe + ": " + ex.Message);
                return -1;
            }
        }

        private string RunHiddenCaptureOutput(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    return stdout;
                }
            }
            catch (Exception ex)
            {
                Log("Failed to run " + exe + ": " + ex.Message);
                return "";
            }
        }

        // -----------------------------------------------------------------
        // Driver package cleanup from the driver store (best-effort)
        // -----------------------------------------------------------------
        private void RemoveDriverPackage()
        {
            try
            {
                string output = RunHiddenCaptureOutput("pnputil.exe", "/enum-drivers");
                string pending = null;
                var names = new List<string>();
                foreach (var rawLine in output.Split('\n'))
                {
                    string line = rawLine.TrimEnd('\r');
                    var m1 = Regex.Match(line, @"^Published Name\s*:\s*(oem\d+\.inf)", RegexOptions.IgnoreCase);
                    if (m1.Success) { pending = m1.Groups[1].Value; continue; }
                    var m2 = Regex.Match(line, @"^Original Name\s*:\s*(.+)$", RegexOptions.IgnoreCase);
                    if (m2.Success && pending != null &&
                        m2.Groups[1].Value.Trim().ToLowerInvariant() == "iddsampledriver.inf")
                    {
                        names.Add(pending);
                        pending = null;
                    }
                }
                foreach (var name in names)
                {
                    Log("Removing driver package " + name + " from driver store...");
                    RunHidden("pnputil.exe", "/delete-driver " + name + " /uninstall /force");
                }
            }
            catch (Exception ex)
            {
                Log("Driver package cleanup skipped: " + ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // High-level actions
        // -----------------------------------------------------------------
        private bool EnableVirtualDisplay()
        {
            try
            {
                int existing = IddDeviceInstaller.CountDevices();
                if (existing > 0)
                {
                    Log("Enable requested but " + existing + " device(s) already present - skipping duplicate create.");
                    ShowBalloon("Virtual Display Driver", "Virtual display is already enabled.", ToolTipIcon.Info);
                    return true;
                }

                Log("Installing device with INF: " + _infPath);
                string result = IddDeviceInstaller.InstallDevice(_infPath);
                Log("Install result: " + result);

                ShowBalloon("Virtual Display Driver",
                    result == "installed-reboot-required"
                        ? "Installed. A reboot may be required for it to appear."
                        : "Virtual display enabled.",
                    ToolTipIcon.Info);
                return true;
            }
            catch (Exception ex)
            {
                Log("ENABLE FAILED: " + ex);
                ShowBalloon("Virtual Display Driver", "Failed to enable: " + ex.Message, ToolTipIcon.Error);
                MessageBox.Show(
                    "Could not install the virtual display driver.\n\n" + ex.Message + "\n\nSee the log for details:\n" + _logPath,
                    "Virtual Display Tray", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool DisableVirtualDisplay()
        {
            try
            {
                Log("Removing device(s)...");
                bool removed = IddDeviceInstaller.RemoveAllDevices() > 0;
                Log("Remove result: " + removed);
                RemoveDriverPackage();
                ShowBalloon("Virtual Display Driver", "Virtual display disabled.", ToolTipIcon.Info);
                return true;
            }
            catch (Exception ex)
            {
                Log("DISABLE FAILED: " + ex);
                ShowBalloon("Virtual Display Driver", "Failed to disable: " + ex.Message, ToolTipIcon.Error);
                MessageBox.Show(
                    "Could not remove the virtual display driver.\n\n" + ex.Message + "\n\nSee the log for details:\n" + _logPath,
                    "Virtual Display Tray", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void UninstallApp()
        {
            var confirm = MessageBox.Show(
                "This will turn off the virtual display, remove the driver, and delete Virtual Display Tray completely (including its auto-start entry).\n\nContinue?",
                "Uninstall Virtual Display Tray", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            Log("Uninstalling app completely...");
            try
            {
                if (IddDeviceInstaller.CountDevices() > 0)
                {
                    IddDeviceInstaller.RemoveAllDevices();
                }
                RemoveDriverPackage();
            }
            catch (Exception ex)
            {
                Log("Uninstall device removal warning: " + ex.Message);
            }

            RunHidden("schtasks.exe", "/Delete /TN \"" + _taskName + "\" /F");

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            try
            {
                string installDirEscaped = _installDir.Replace("\"", "\\\"");
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c timeout /t 1 /nobreak >nul & rmdir /s /q \"" + installDirEscaped + "\"")
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                Process.Start(psi);
            }
            catch
            {
                // best-effort cleanup only
            }

            ExitThread();
        }

        // -----------------------------------------------------------------
        // Tray icon UI
        // -----------------------------------------------------------------
        private static Icon CreateDotIcon(Color color)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var brush = new SolidBrush(color)) g.FillEllipse(brush, 2, 2, 28, 28);
                    using (var pen = new Pen(Color.Black, 1)) g.DrawEllipse(pen, 2, 2, 28, 28);
                }
                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private void BuildTrayIcon()
        {
            _iconOn = CreateDotIcon(Color.LimeGreen);
            _iconOff = CreateDotIcon(Color.Firebrick);
            _iconBusy = CreateDotIcon(Color.Goldenrod);

            _notifyIcon = new NotifyIcon { Visible = true };

            var menu = new ContextMenuStrip();
            _toggleItem = new ToolStripMenuItem("");
            _toggleItem.Click += (s, e) => InvokeToggle();
            menu.Items.Add(_toggleItem);

            var openLogItem = new ToolStripMenuItem("Open log file");
            openLogItem.Click += (s, e) =>
            {
                if (!File.Exists(_logPath))
                {
                    Directory.CreateDirectory(_installDir);
                    File.Create(_logPath).Dispose();
                }
                Process.Start("notepad.exe", "\"" + _logPath + "\"");
            };
            menu.Items.Add(openLogItem);
            menu.Items.Add(new ToolStripSeparator());

            var uninstallItem = new ToolStripMenuItem("Uninstall completely");
            uninstallItem.Click += (s, e) => UninstallApp();
            menu.Items.Add(uninstallItem);
            menu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("Exit (keep installed)");
            exitItem.Click += (s, e) =>
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                ExitThread();
            };
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) InvokeToggle();
            };
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            if (_notifyIcon == null) return;
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = text;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(4000);
        }

        private void UpdateTrayState()
        {
            int count = 0;
            try { count = IddDeviceInstaller.CountDevices(); }
            catch (Exception ex) { Log("State check failed: " + ex.Message); }

            if (count > 1)
            {
                _state = "duplicate";
                _notifyIcon.Icon = _iconBusy;
                _notifyIcon.Text = Truncate("Virtual Display: " + count + " copies found (click to fix)");
                _toggleItem.Text = "Fix duplicate displays (" + count + " found)";
                Log("Duplicate devices detected: " + count);
            }
            else if (count == 1)
            {
                _state = "on";
                _notifyIcon.Icon = _iconOn;
                _notifyIcon.Text = "Virtual Display: ON (click to disable)";
                _toggleItem.Text = "Disable virtual display";
            }
            else
            {
                _state = "off";
                _notifyIcon.Icon = _iconOff;
                _notifyIcon.Text = "Virtual Display: OFF (click to enable)";
                _toggleItem.Text = "Enable virtual display";
            }
        }

        private static string Truncate(string s)
        {
            // NotifyIcon.Text is limited to 63 characters on older Windows versions.
            return s.Length <= 63 ? s : s.Substring(0, 63);
        }

        private void InvokeToggle()
        {
            _notifyIcon.Icon = _iconBusy;
            _notifyIcon.Text = "Virtual Display: working...";
            Application.DoEvents();

            if (_state == "on" || _state == "duplicate")
            {
                bool wasDuplicate = _state == "duplicate";
                DisableVirtualDisplay();
                if (wasDuplicate)
                {
                    ShowBalloon("Virtual Display Driver",
                        "Removed the duplicate displays. Click again to turn the virtual display back on.",
                        ToolTipIcon.Info);
                }
            }
            else
            {
                EnableVirtualDisplay();
            }

            UpdateTrayState();
        }
    }

    // -----------------------------------------------------------------------
    // Native device install/remove helper (SetupAPI P/Invoke). Mirrors what
    // the classic "devcon install / remove" commands do for a root-enumerated
    // (non plug-and-play) device, without needing devcon.exe.
    // -----------------------------------------------------------------------
    internal static class IddDeviceInstaller
    {
        private const int DIGCF_PRESENT = 0x02;
        private const int SPDRP_HARDWAREID = 0x01;
        private const int DIF_REGISTERDEVICE = 0x19;
        private const int DIF_REMOVE = 0x05;
        private const int DICD_GENERATE_ID = 0x01;
        private const int INSTALLFLAG_FORCE = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public int DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid ClassGuid, IntPtr hwndParent);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, int Flags);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiCreateDeviceInfo(IntPtr DeviceInfoSet, string DeviceName, ref Guid ClassGuid, string DeviceDescription, IntPtr hwndParent, int CreationFlags, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiSetDeviceRegistryProperty(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, int Property, byte[] PropertyBuffer, int PropertyBufferSize);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiCallClassInstaller(int InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, int MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, int Property, out int PropertyRegDataType, byte[] PropertyBuffer, int PropertyBufferSize, out int RequiredSize);

        [DllImport("newdev.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool UpdateDriverForPlugAndPlayDevices(IntPtr hwndParent, string HardwareId, string FullInfPath, int InstallFlags, out bool bRebootRequired);

        private static readonly Guid GUID_DEVCLASS_DISPLAY = new Guid("4D36E968-E325-11CE-BFC1-08002BE10318");
        private const string HWID = "Root\\IddSampleDriver";

        public static string InstallDevice(string infPath)
        {
            Guid classGuid = GUID_DEVCLASS_DISPLAY;
            IntPtr devInfoSet = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
            if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1))
                throw new Exception("SetupDiCreateDeviceInfoList failed (Win32 error " + Marshal.GetLastWin32Error() + ")");

            try
            {
                SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = Marshal.SizeOf(devInfoData);

                if (!SetupDiCreateDeviceInfo(devInfoSet, "Display", ref classGuid, null, IntPtr.Zero, DICD_GENERATE_ID, ref devInfoData))
                    throw new Exception("SetupDiCreateDeviceInfo failed (Win32 error " + Marshal.GetLastWin32Error() + ")");

                byte[] hwIdBytes = System.Text.Encoding.Unicode.GetBytes(HWID + "\0\0");
                if (!SetupDiSetDeviceRegistryProperty(devInfoSet, ref devInfoData, SPDRP_HARDWAREID, hwIdBytes, hwIdBytes.Length))
                    throw new Exception("SetupDiSetDeviceRegistryProperty failed (Win32 error " + Marshal.GetLastWin32Error() + ")");

                if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, devInfoSet, ref devInfoData))
                    throw new Exception("SetupDiCallClassInstaller(REGISTERDEVICE) failed (Win32 error " + Marshal.GetLastWin32Error() + ")");

                bool reboot;
                if (!UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, HWID, infPath, INSTALLFLAG_FORCE, out reboot))
                    throw new Exception("UpdateDriverForPlugAndPlayDevices failed (Win32 error " + Marshal.GetLastWin32Error() + ")");

                return reboot ? "installed-reboot-required" : "installed";
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }
        }

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
                    string multi = System.Text.Encoding.Unicode.GetString(buffer, 0, requiredSize);
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

        public static bool IsDeviceInstalled()
        {
            IntPtr devInfoSet;
            SP_DEVINFO_DATA data;
            bool found = FindDevice(out devInfoSet, out data);
            if (found) SetupDiDestroyDeviceInfoList(devInfoSet);
            return found;
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
                        string multi = System.Text.Encoding.Unicode.GetString(buffer, 0, requiredSize);
                        if (multi.ToUpperInvariant().Contains(HWID.ToUpperInvariant()))
                            count++;
                    }
                    index++;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }
            return count;
        }

        // Removes every device instance matching HWID, not just the first
        // one found - guards against duplicate device nodes ending up with
        // more than one virtual monitor showing up. Returns count removed.
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
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfoSet);
                }
            }
            return removed;
        }
    }
}
