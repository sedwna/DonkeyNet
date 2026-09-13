using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("DonkeyNet")]
[assembly: AssemblyDescription("A tiny Persian Windows internet quality monitor")]
[assembly: AssemblyCompany("sedwna")]
[assembly: AssemblyProduct("DonkeyNet")]
[assembly: AssemblyVersion("1.1.2.0")]
[assembly: AssemblyFileVersion("1.1.2.0")]

namespace DonkeyNet
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "DonkeyNet.SingleInstance", out createdNew))
            {
                if (!createdNew) return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplication());
            }
        }
    }

    internal sealed class TrayApplication : ApplicationContext
    {
        private const int NimModify = 0x00000001;
        private const int NifInfo = 0x00000010;
        private const int NiifUser = 0x00000004;
        private const int NiifLargeIcon = 0x00000020;
        // Thresholds are intentionally easy to edit and rebuild.
        private const int CheckEverySeconds = 10;
        private const int PingTimeoutMs = 1200;
        private const int Level1LatencyMs = 180;
        private const int Level2LatencyMs = 350;
        private const int Level3LatencyMs = 700;
        private static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(10);
        private static readonly string[] Targets = { "1.1.1.1", "8.8.8.8", "9.9.9.9" };
        private const string StartupValueName = "DonkeyNet";
        private const string VersionUrl = "https://github.com/sedwna/DonkeyNet/releases/latest/download/VERSION";
        private const string DownloadUrl = "https://github.com/sedwna/DonkeyNet/releases/latest/download/DonkeyNet.exe";
        private const string ChecksumUrl = "https://github.com/sedwna/DonkeyNet/releases/latest/download/DonkeyNet.exe.sha256";

        private readonly NotifyIcon trayIcon;
        private readonly Icon appIcon;
        private readonly System.Windows.Forms.Timer timer;
        private readonly ToolStripMenuItem statusItem;
        private readonly ToolStripMenuItem startupItem;
        private readonly ToolStripMenuItem updateItem;
        private int checking;
        private int updating;
        private int currentLevel;
        private int candidateLevel = -1;
        private int candidateCount;
        private DateTime lastWarning = DateTime.MinValue;

        internal TrayApplication()
        {
            statusItem = new ToolStripMenuItem("در حال بررسی اینترنت…") { Enabled = false };
            startupItem = new ToolStripMenuItem("اجرا با شروع ویندوز") { CheckOnClick = true };
            startupItem.Checked = IsStartupEnabled();
            startupItem.Click += delegate { SetStartup(startupItem.Checked); };

            var checkNowItem = new ToolStripMenuItem("بررسی همین حالا");
            checkNowItem.Click += async delegate { await CheckConnectionAsync(true); };

            updateItem = new ToolStripMenuItem("بررسی برای به‌روزرسانی");
            updateItem.Click += async delegate { await CheckForUpdatesAsync(); };

            var exitItem = new ToolStripMenuItem("خروج");
            exitItem.Click += delegate { Exit(); };

            var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(checkNowItem);
            menu.Items.Add(updateItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            appIcon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location)
                ?? (Icon)SystemIcons.Information.Clone();

            trayIcon = new NotifyIcon
            {
                Icon = appIcon,
                Text = "اینترنت‌سنج خر",
                ContextMenuStrip = menu,
                Visible = true
            };
            trayIcon.DoubleClick += async delegate { await CheckConnectionAsync(true); };

            // The app is meant to stay available, so enable per-user startup on first run.
            if (!startupItem.Checked)
            {
                SetStartup(true);
                startupItem.Checked = IsStartupEnabled();
            }

            timer = new System.Windows.Forms.Timer { Interval = 250 };
            timer.Tick += async delegate
            {
                // The first short tick starts only after the Windows message loop exists.
                if (timer.Interval != CheckEverySeconds * 1000)
                    timer.Interval = CheckEverySeconds * 1000;
                await CheckConnectionAsync(false);
            };
            timer.Start();
        }

        private async Task CheckConnectionAsync(bool userRequested)
        {
            if (Interlocked.Exchange(ref checking, 1) == 1) return;

            try
            {
                ConnectionResult result = await MeasureAsync();
                int level = Classify(result);
                UpdateStatusText(result, level);

                if (userRequested)
                {
                    ShowResult(level, result, true);
                    return;
                }

                // Two equal readings avoid warnings caused by a single brief hiccup.
                if (candidateLevel == level) candidateCount++;
                else
                {
                    candidateLevel = level;
                    candidateCount = 1;
                }

                if (candidateCount < 2) return;

                bool levelChanged = currentLevel != level;
                currentLevel = level;

                if (level > 0 && (levelChanged || DateTime.UtcNow - lastWarning >= ReminderInterval))
                {
                    ShowResult(level, result, false);
                    lastWarning = DateTime.UtcNow;
                }
            }
            catch
            {
                statusItem.Text = "خطا در سنجش اتصال";
            }
            finally
            {
                Interlocked.Exchange(ref checking, 0);
            }
        }

        private static async Task<ConnectionResult> MeasureAsync()
        {
            var latencies = new List<long>();
            int failures = 0;

            foreach (string target in Targets)
            {
                try
                {
                    using (var ping = new Ping())
                    {
                        PingReply reply = await ping.SendPingAsync(target, PingTimeoutMs);
                        if (reply.Status == IPStatus.Success) latencies.Add(reply.RoundtripTime);
                        else failures++;
                    }
                }
                catch { failures++; }
            }

            // Some networks block ICMP. A TCP fallback prevents a false "offline" warning.
            if (latencies.Count == 0)
            {
                long tcpLatency = await MeasureTcpAsync("1.1.1.1", 443, PingTimeoutMs);
                if (tcpLatency >= 0)
                {
                    latencies.Add(tcpLatency);
                    failures = 2;
                }
            }

            long average = 0;
            foreach (long latency in latencies) average += latency;
            if (latencies.Count > 0) average /= latencies.Count;

            return new ConnectionResult(average, failures, latencies.Count);
        }

        private static async Task<long> MeasureTcpAsync(string host, int port, int timeoutMs)
        {
            var stopwatch = Stopwatch.StartNew();
            using (var client = new TcpClient())
            {
                Task connect = client.ConnectAsync(host, port);
                Task completed = await Task.WhenAny(connect, Task.Delay(timeoutMs));
                if (completed != connect) return -1;
                try { await connect; }
                catch { return -1; }
                if (!client.Connected) return -1;
                stopwatch.Stop();
                return stopwatch.ElapsedMilliseconds;
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            if (Interlocked.Exchange(ref updating, 1) == 1) return;

            updateItem.Enabled = false;
            statusItem.Text = "در حال بررسی به‌روزرسانی…";
            string downloadedPath = null;

            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
                string cacheBuster = "?t=" + DateTime.UtcNow.Ticks;
                Version latestVersion;
                string remoteVersion;

                using (var client = CreateWebClient())
                    remoteVersion = await client.DownloadStringTaskAsync(new Uri(VersionUrl + cacheBuster));

                if (!Version.TryParse(remoteVersion.Trim().TrimStart('v', 'V'), out latestVersion))
                    throw new InvalidDataException("نسخهٔ منتشرشده معتبر نیست.");

                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (latestVersion <= currentVersion)
                {
                    statusItem.Text = "برنامه به‌روز است";
                    ShowCustomBalloon("\u200Fبرنامه به‌روز است.",
                        "\u200Fنسخهٔ فعلی: " + ToPersianDigits(currentVersion.ToString(3)));
                    return;
                }

                statusItem.Text = "در حال دریافت نسخهٔ " + ToPersianDigits(latestVersion.ToString(3)) + "…";
                byte[] executable;
                string expectedChecksum;
                using (var client = CreateWebClient())
                {
                    Task<byte[]> executableTask = client.DownloadDataTaskAsync(new Uri(DownloadUrl + cacheBuster));
                    using (var checksumClient = CreateWebClient())
                    {
                        Task<string> checksumTask = checksumClient.DownloadStringTaskAsync(new Uri(ChecksumUrl + cacheBuster));
                        await Task.WhenAll(executableTask, checksumTask);
                        executable = executableTask.Result;
                        expectedChecksum = checksumTask.Result.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    }
                }

                if (executable.Length < 10240 || executable[0] != (byte)'M' || executable[1] != (byte)'Z')
                    throw new InvalidDataException("فایل به‌روزرسانی معتبر نیست.");
                if (!string.Equals(ComputeSha256(executable), expectedChecksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("صحت فایل به‌روزرسانی تأیید نشد.");

                downloadedPath = Path.Combine(Path.GetTempPath(), "DonkeyNet-update-" + Guid.NewGuid().ToString("N") + ".exe");
                File.WriteAllBytes(downloadedPath, executable);
                string currentPath = Assembly.GetExecutingAssembly().Location;
                StartUpdater(downloadedPath, currentPath, Process.GetCurrentProcess().Id);

                ShowCustomBalloon("\u200Fبه‌روزرسانی دریافت شد.",
                    "\u200Fنسخهٔ " + ToPersianDigits(latestVersion.ToString(3)) + " نصب و برنامه دوباره اجرا می‌شود.");
                await Task.Delay(1400);
                downloadedPath = null; // The updater owns the temporary file now.
                Exit();
            }
            catch (Exception ex)
            {
                statusItem.Text = "خطا در به‌روزرسانی";
                ShowCustomBalloon("\u200Fبه‌روزرسانی انجام نشد.", "\u200F" + ex.Message);
            }
            finally
            {
                if (downloadedPath != null)
                {
                    try { File.Delete(downloadedPath); }
                    catch { }
                }
                updateItem.Enabled = true;
                Interlocked.Exchange(ref updating, 0);
            }
        }

        private static WebClient CreateWebClient()
        {
            var client = new TimeoutWebClient { Encoding = Encoding.UTF8 };
            client.Headers[HttpRequestHeader.UserAgent] = "DonkeyNet/" + Assembly.GetExecutingAssembly().GetName().Version;
            return client;
        }

        private static string ComputeSha256(byte[] data)
        {
            using (SHA256 sha256 = SHA256.Create())
                return BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", string.Empty);
        }

        private static void StartUpdater(string downloadedPath, string currentPath, int processId)
        {
            string script = string.Format(
                "$process = Get-Process -Id {0} -ErrorAction SilentlyContinue; " +
                "if ($process) {{ $process.WaitForExit() }}; " +
                "Start-Sleep -Milliseconds 300; " +
                "Copy-Item -LiteralPath {1} -Destination {2} -Force; " +
                "Remove-Item -LiteralPath {1} -Force; " +
                "Start-Process -FilePath {2}",
                processId, PowerShellQuote(downloadedPath), PowerShellQuote(currentPath));
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }

        private static string PowerShellQuote(string value)
        {
            return "'" + value.Replace("'", "''") + "'";
        }

        private static int Classify(ConnectionResult result)
        {
            if (result.Successes == 0 || result.AverageLatency >= Level3LatencyMs) return 3;
            if (result.Failures >= 2 || result.AverageLatency >= Level2LatencyMs) return 2;
            if (result.Failures == 1 || result.AverageLatency >= Level1LatencyMs) return 1;
            return 0;
        }

        private void UpdateStatusText(ConnectionResult result, int level)
        {
            if (level == 3 && result.Successes == 0)
                statusItem.Text = "اتصال برقرار نیست";
            else
                statusItem.Text = string.Format("وضعیت: {0} — {1} ms", LevelName(level), result.AverageLatency);

            trayIcon.Text = level == 0 ? "اینترنت‌سنج خر — اتصال خوب است" : "اینترنت‌سنج خر — هشدار سطح " + level;
        }

        private void ShowResult(int level, ConnectionResult result, bool userRequested)
        {
            string title;
            string explanation;

            if (level == 1)
            {
                title = "اینترنت خر است.";
                explanation = "اینترنت ضعیفه، بازم جای شکرش باقیه.";
            }
            else if (level == 2)
            {
                title = "اینترنت خیلی خر است.";
                explanation = "اینترنت خیلی ضعیفه، اینجا ایرانه مشکل داری جمع کن برو.";
            }
            else if (level == 3)
            {
                title = "اینترنت خود خر است.";
                explanation = "اینترنتی وجود نداره، برو بمیر.";
            }
            else
            {
                if (!userRequested) return;
                title = "اینترنت خر نیست.";
                explanation = "اینترنت خوبه، بگو حمد و سپاس خدارا.";
            }

            string ping = result.Successes == 0
                ? "سرعت خر: قطع"
                : string.Format("سرعت خر: {0} میلی‌ثانیه", ToPersianDigits(result.AverageLatency));
            string connectionName = GetConnectionName();
            string connection = "سوار کدوم خری؟ \u2068" + connectionName + "\u2069";

            // Keep every line purely right-to-left. Mixing "ms" and Persian text
            // makes Windows reorder punctuation and numbers in notification cards.
            string text = string.Format("\u200F{0}{1}\u200F{2}{1}\u200F{3}",
                connection, Environment.NewLine, ping, explanation);
            title = "\u200F" + title;
            if (!ShowCustomBalloon(title, text))
            {
                // A logo-less fallback is preferable to Windows' generic blue icon.
                trayIcon.BalloonTipTitle = title;
                trayIcon.BalloonTipText = text;
                trayIcon.BalloonTipIcon = ToolTipIcon.None;
                trayIcon.ShowBalloonTip(5000);
            }
        }

        private bool ShowCustomBalloon(string title, string text)
        {
            try
            {
                // WinForms does not expose custom balloon icons, but Windows supports
                // them through NOTIFYICONDATA.hBalloonIcon with NIIF_USER.
                Type notifyIconType = typeof(NotifyIcon);
                FieldInfo windowField = notifyIconType.GetField("window", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? notifyIconType.GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo idField = notifyIconType.GetField("id", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? notifyIconType.GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic);
                if (windowField == null || idField == null) return false;

                NativeWindow window = windowField.GetValue(trayIcon) as NativeWindow;
                if (window == null || window.Handle == IntPtr.Zero) return false;

                var data = new NotifyIconData
                {
                    cbSize = Marshal.SizeOf(typeof(NotifyIconData)),
                    hWnd = window.Handle,
                    uID = Convert.ToUInt32(idField.GetValue(trayIcon)),
                    uFlags = NifInfo,
                    szTip = string.Empty,
                    szInfo = Truncate(text, 255),
                    uTimeoutOrVersion = 5000,
                    szInfoTitle = Truncate(title, 63),
                    dwInfoFlags = NiifUser | NiifLargeIcon,
                    guidItem = Guid.Empty,
                    hBalloonIcon = appIcon.Handle
                };

                return ShellNotifyIcon(NimModify, ref data);
            }
            catch { return false; }
        }

        private static string Truncate(string value, int maximumLength)
        {
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        private static string ToPersianDigits(long value)
        {
            return ToPersianDigits(value.ToString());
        }

        private static string ToPersianDigits(string value)
        {
            return value
                .Replace('0', '۰').Replace('1', '۱').Replace('2', '۲').Replace('3', '۳').Replace('4', '۴')
                .Replace('5', '۵').Replace('6', '۶').Replace('7', '۷').Replace('8', '۸').Replace('9', '۹');
        }

        private static string GetConnectionName()
        {
            object managerObject = null;
            object networksObject = null;
            try
            {
                Type managerType = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
                managerObject = Activator.CreateInstance(managerType);
                dynamic manager = managerObject;
                networksObject = manager.GetNetworks(1); // NLM_ENUM_NETWORK_CONNECTED
                foreach (dynamic network in (dynamic)networksObject)
                {
                    try
                    {
                        if (network.IsConnectedToInternet)
                        {
                            string name = CleanConnectionName((string)network.GetName());
                            if (name != "نامشخص") return name;
                        }
                    }
                    finally
                    {
                        if (Marshal.IsComObject(network)) Marshal.FinalReleaseComObject(network);
                    }
                }
            }
            catch { }
            finally
            {
                if (networksObject != null && Marshal.IsComObject(networksObject))
                    Marshal.FinalReleaseComObject(networksObject);
                if (managerObject != null && Marshal.IsComObject(managerObject))
                    Marshal.FinalReleaseComObject(managerObject);
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = "wlan show interfaces",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process process = Process.Start(startInfo))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    Match match = Regex.Match(output, @"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
                    if (match.Success) return CleanConnectionName(match.Groups[1].Value);
                }
            }
            catch { }

            try
            {
                NetworkInterface fallback = null;
                foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (network.OperationalStatus != OperationalStatus.Up ||
                        network.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        network.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                        network.GetIPProperties().GatewayAddresses.Count == 0) continue;

                    if (network.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                        return CleanConnectionName(network.Name);
                    if (fallback == null) fallback = network;
                }
                if (fallback != null) return CleanConnectionName(fallback.Name);
            }
            catch { }

            return "نامشخص";
        }

        private static string CleanConnectionName(string value)
        {
            string clean = Regex.Replace(value ?? string.Empty, @"[\x00-\x1F\x7F]", string.Empty).Trim();
            if (clean.Length == 0) return "نامشخص";
            return clean.Length <= 40 ? clean : clean.Substring(0, 40) + "…";
        }

        private static string LevelName(int level)
        {
            switch (level)
            {
                case 1: return "ضعیف";
                case 2: return "خیلی ضعیف";
                case 3: return "قطع یا افتضاح";
                default: return "خوب";
            }
        }

        private static bool IsStartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                return key != null && key.GetValue(StartupValueName) != null;
            }
        }

        private static void SetStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (key == null) return;
                if (enabled)
                {
                    string path = Assembly.GetExecutingAssembly().Location;
                    key.SetValue(StartupValueName, "\"" + path + "\"");
                }
                else key.DeleteValue(StartupValueName, false);
            }
        }

        private void Exit()
        {
            timer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                trayIcon.Dispose();
                appIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class ConnectionResult
        {
            internal readonly long AverageLatency;
            internal readonly int Failures;
            internal readonly int Successes;

            internal ConnectionResult(long averageLatency, int failures, int successes)
            {
                AverageLatency = averageLatency;
                Failures = failures;
                Successes = successes;
            }
        }

        private sealed class TimeoutWebClient : WebClient
        {
            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest request = base.GetWebRequest(address);
                request.Timeout = 15000;
                var httpRequest = request as HttpWebRequest;
                if (httpRequest != null) httpRequest.ReadWriteTimeout = 15000;
                return request;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NotifyIconData
        {
            internal int cbSize;
            internal IntPtr hWnd;
            internal uint uID;
            internal int uFlags;
            internal int uCallbackMessage;
            internal IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string szTip;
            internal int dwState;
            internal int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string szInfo;
            internal int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string szInfoTitle;
            internal int dwInfoFlags;
            internal Guid guidItem;
            internal IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShellNotifyIcon(int message, ref NotifyIconData data);
    }
}
