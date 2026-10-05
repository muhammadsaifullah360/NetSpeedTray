// NetSpeedTray - lightweight Windows system-tray internet speed monitor.
// Targets .NET Framework 4.x (C# 5). No external dependencies.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetSpeedTray
{
    static class App
    {
        public const string Version = "2.0";
        public const string Owner = "muhammadsaifullah360";
        public const string Repo = "NetSpeedTray";
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 1 && args[0] == "--shots")
            {
                Screenshots.Run(args.Length >= 2 ? args[1] : ".");
                return;
            }

            bool createdNew;
            using (Mutex m = new Mutex(true, "NetSpeedTray_SingleInstance_8f3a", out createdNew))
            {
                if (!createdNew) return;
                try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls; }
                catch { }
                Application.Run(new TrayContext());
            }
        }
    }

    // ---------------------------------------------------------------- Settings
    enum UnitMode { AutoBytes, AutoBits, KBps, MBps, Kbps, Mbps }
    enum ThemeMode { Auto, Dark, Light }
    enum IconMode { TwoLine, DownloadOnly }

    class Settings
    {
        public UnitMode Unit = UnitMode.AutoBytes;
        public ThemeMode Theme = ThemeMode.Dark;
        public IconMode Icon = IconMode.TwoLine;
        public bool StartWithWindows = false;
        public double MonthlyCapGB = 0;        // 0 = off
        public bool PingEnabled = true;
        public string PingHost = "1.1.1.1";
        public int AutoTestHours = 0;          // 0 = off
        public bool CheckUpdates = true;
        public bool WidgetVisible = false;
        public int WidgetX = -1, WidgetY = -1;

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetSpeedTray"); } }
        static string FilePath { get { return Path.Combine(Dir, "config.ini"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (File.Exists(FilePath))
                    foreach (string raw in File.ReadAllLines(FilePath))
                    {
                        string line = raw.Trim();
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string v = line.Substring(eq + 1).Trim();
                        switch (k)
                        {
                            case "unit": try { s.Unit = (UnitMode)Enum.Parse(typeof(UnitMode), v, true); } catch { } break;
                            case "theme": try { s.Theme = (ThemeMode)Enum.Parse(typeof(ThemeMode), v, true); } catch { } break;
                            case "icon": try { s.Icon = (IconMode)Enum.Parse(typeof(IconMode), v, true); } catch { } break;
                            case "startwithwindows": s.StartWithWindows = (v == "1" || v.ToLowerInvariant() == "true"); break;
                            case "monthlycapgb": double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out s.MonthlyCapGB); break;
                            case "pingenabled": s.PingEnabled = (v == "1" || v.ToLowerInvariant() == "true"); break;
                            case "pinghost": if (v.Length > 0) s.PingHost = v; break;
                            case "autotesthours": int.TryParse(v, out s.AutoTestHours); break;
                            case "checkupdates": s.CheckUpdates = (v == "1" || v.ToLowerInvariant() == "true"); break;
                            case "widgetvisible": s.WidgetVisible = (v == "1" || v.ToLowerInvariant() == "true"); break;
                            case "widgetx": int.TryParse(v, out s.WidgetX); break;
                            case "widgety": int.TryParse(v, out s.WidgetY); break;
                        }
                    }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("unit=" + Unit);
                sb.AppendLine("theme=" + Theme);
                sb.AppendLine("icon=" + Icon);
                sb.AppendLine("startwithwindows=" + (StartWithWindows ? "1" : "0"));
                sb.AppendLine("monthlycapgb=" + MonthlyCapGB.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("pingenabled=" + (PingEnabled ? "1" : "0"));
                sb.AppendLine("pinghost=" + PingHost);
                sb.AppendLine("autotesthours=" + AutoTestHours);
                sb.AppendLine("checkupdates=" + (CheckUpdates ? "1" : "0"));
                sb.AppendLine("widgetvisible=" + (WidgetVisible ? "1" : "0"));
                sb.AppendLine("widgetx=" + WidgetX);
                sb.AppendLine("widgety=" + WidgetY);
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- Palette
    class Palette
    {
        public Color Bg, Card, Text, Dim, Border, Down, Up;
        public static Palette For(ThemeMode t)
        {
            Palette p = new Palette();
            if (t == ThemeMode.Light)
            {
                p.Bg = Color.FromArgb(244, 245, 248); p.Card = Color.FromArgb(255, 255, 255);
                p.Text = Color.FromArgb(26, 28, 34); p.Dim = Color.FromArgb(107, 114, 128);
                p.Border = Color.FromArgb(32, 0, 0, 0);
                p.Down = Color.FromArgb(22, 163, 74); p.Up = Color.FromArgb(37, 99, 235);
            }
            else
            {
                p.Bg = Color.FromArgb(24, 25, 32); p.Card = Color.FromArgb(33, 35, 46);
                p.Text = Color.FromArgb(240, 242, 248); p.Dim = Color.FromArgb(150, 158, 178);
                p.Border = Color.FromArgb(60, 255, 255, 255);
                p.Down = Color.FromArgb(56, 214, 107); p.Up = Color.FromArgb(61, 165, 255);
            }
            return p;
        }
    }

    // --------------------------------------------------------------- Usage log
    class UsageStore
    {
        readonly Dictionary<string, long[]> _days = new Dictionary<string, long[]>(); // date -> {down,up}
        static string FilePath { get { return Path.Combine(Settings.Dir, "usage.csv"); } }

        public UsageStore() { Load(); }

        static string TodayKey { get { return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }
        static string MonthPrefix { get { return DateTime.Now.ToString("yyyy-MM", CultureInfo.InvariantCulture); } }

        void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string[] p = raw.Split(',');
                    if (p.Length != 3) continue;
                    long d, u;
                    if (long.TryParse(p[1], out d) && long.TryParse(p[2], out u))
                        _days[p[0].Trim()] = new long[] { d, u };
                }
            }
            catch { }
        }

        public void Add(long down, long up)
        {
            if (down <= 0 && up <= 0) return;
            string k = TodayKey;
            long[] v;
            if (!_days.TryGetValue(k, out v)) { v = new long[2]; _days[k] = v; }
            v[0] += down; v[1] += up;
        }

        public long TodayDown { get { long[] v; return _days.TryGetValue(TodayKey, out v) ? v[0] : 0; } }
        public long TodayUp { get { long[] v; return _days.TryGetValue(TodayKey, out v) ? v[1] : 0; } }

        public long MonthDown { get { long t = 0; string m = MonthPrefix; foreach (var kv in _days) if (kv.Key.StartsWith(m)) t += kv.Value[0]; return t; } }
        public long MonthUp { get { long t = 0; string m = MonthPrefix; foreach (var kv in _days) if (kv.Key.StartsWith(m)) t += kv.Value[1]; return t; } }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(Settings.Dir)) Directory.CreateDirectory(Settings.Dir);
                // Prune entries older than ~400 days.
                DateTime cutoff = DateTime.Now.AddDays(-400);
                StringBuilder sb = new StringBuilder();
                foreach (var kv in _days)
                {
                    DateTime dt;
                    if (DateTime.TryParse(kv.Key, out dt) && dt < cutoff) continue;
                    sb.Append(kv.Key).Append(',').Append(kv.Value[0]).Append(',').Append(kv.Value[1]).Append('\n');
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }
    }

    // ------------------------------------------------------------- Measurement
    class Sample
    {
        public double DownBps, UpBps;
        public long DeltaDown, DeltaUp;   // bytes transferred this tick (primary)
        public string NetworkName = "Disconnected";
        public string AdapterName = "";
        public bool IsWireless;
        public bool Connected;
        public long ConnDown, ConnUp;     // since connecting to the current network
    }

    class Monitor
    {
        class Counter { public long Rx; public long Tx; }
        readonly Dictionary<string, Counter> _prev = new Dictionary<string, Counter>();
        readonly Stopwatch _sw = new Stopwatch();
        long _connDown, _connUp;
        string _primaryId;
        string _networkName = "Disconnected";
        string _adapterName = "";
        bool _wireless;
        int _nameTick;

        public Sample Tick()
        {
            double dt = 1.0;
            if (_sw.IsRunning) { dt = _sw.Elapsed.TotalSeconds; if (dt < 0.05) dt = 0.05; }
            _sw.Restart();

            NetworkInterface[] all;
            try { all = NetworkInterface.GetAllNetworkInterfaces(); }
            catch { return new Sample(); }

            if (_nameTick <= 0) { ChoosePrimary(all); _nameTick = 5; }
            _nameTick--;

            double down = 0, up = 0; long dd = 0, du = 0; bool connected = false;

            foreach (NetworkInterface ni in all)
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (ni.OperationalStatus != OperationalStatus.Up) continue;

                long rx, tx;
                try { var st = ni.GetIPv4Statistics(); rx = st.BytesReceived; tx = st.BytesSent; }
                catch { continue; }

                string id = ni.Id;
                Counter c;
                if (_prev.TryGetValue(id, out c))
                {
                    long dRx = rx - c.Rx, dTx = tx - c.Tx;
                    if (dRx < 0) dRx = 0;
                    if (dTx < 0) dTx = 0;
                    if (id == _primaryId)
                    {
                        down = dRx / dt; up = dTx / dt;
                        dd = dRx; du = dTx;
                        _connDown += dRx; _connUp += dTx;
                        connected = true;
                    }
                    c.Rx = rx; c.Tx = tx;
                }
                else _prev[id] = new Counter { Rx = rx, Tx = tx };
            }

            Sample s = new Sample();
            s.DownBps = down; s.UpBps = up;
            s.DeltaDown = dd; s.DeltaUp = du;
            s.Connected = connected && _primaryId != null;
            s.NetworkName = s.Connected ? _networkName : "Disconnected";
            s.AdapterName = _adapterName; s.IsWireless = _wireless;
            s.ConnDown = _connDown; s.ConnUp = _connUp;
            return s;
        }

        void ChoosePrimary(NetworkInterface[] all)
        {
            NetworkInterface best = null;
            foreach (NetworkInterface ni in all)
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                try
                {
                    IPInterfaceProperties p = ni.GetIPProperties();
                    bool hasGateway = false;
                    foreach (GatewayIPAddressInformation g in p.GatewayAddresses)
                        if (g.Address != null && !g.Address.Equals(IPAddress.Any)) { hasGateway = true; break; }
                    if (!hasGateway) continue;
                    if (best == null) best = ni;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) { best = ni; break; }
                }
                catch { }
            }

            string newId = best == null ? null : best.Id;
            if (newId != _primaryId) { _connDown = 0; _connUp = 0; }

            if (best == null)
            {
                _primaryId = null; _networkName = "Disconnected"; _adapterName = ""; _wireless = false; return;
            }

            _primaryId = best.Id;
            _adapterName = best.Name;
            _wireless = best.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
            if (_wireless)
            {
                string ssid = GetWifiSsid();
                _networkName = string.IsNullOrEmpty(ssid) ? best.Name : ssid;
            }
            else _networkName = best.Name;
        }

        static string GetWifiSsid()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netsh", "wlan show interfaces");
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.StandardOutputEncoding = Encoding.UTF8;
                using (Process pr = Process.Start(psi))
                {
                    string outp = pr.StandardOutput.ReadToEnd();
                    pr.WaitForExit(1500);
                    foreach (string raw in outp.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                            !line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
                        {
                            int c = line.IndexOf(':');
                            if (c > 0) return line.Substring(c + 1).Trim();
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }

    // ------------------------------------------------------------- Formatting
    static class Fmt
    {
        public static double Mbps(double bytesPerSec) { return bytesPerSec * 8.0 / 1000000.0; }

        public static string Full(double bytesPerSec, UnitMode mode)
        {
            double v; string unit; Scale(bytesPerSec, mode, out v, out unit);
            string num = v >= 100 ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString("0.0", CultureInfo.InvariantCulture);
            return num + " " + unit;
        }

        public static string Compact(double bytesPerSec, UnitMode mode)
        {
            bool bits = mode == UnitMode.AutoBits || mode == UnitMode.Kbps || mode == UnitMode.Mbps;
            double v = bits ? bytesPerSec * 8.0 : bytesPerSec;
            string[] u = { "", "K", "M", "G" };
            int i = 0;
            while (v >= 1000 && i < u.Length - 1) { v /= 1024.0; i++; }
            string num = i == 0 ? v.ToString("0", CultureInfo.InvariantCulture)
                       : (v < 10 ? v.ToString("0.0", CultureInfo.InvariantCulture) : v.ToString("0", CultureInfo.InvariantCulture));
            return num + u[i];
        }

        static void Scale(double bytesPerSec, UnitMode mode, out double value, out string unit)
        {
            switch (mode)
            {
                case UnitMode.KBps: value = bytesPerSec / 1024.0; unit = "KB/s"; return;
                case UnitMode.MBps: value = bytesPerSec / (1024.0 * 1024.0); unit = "MB/s"; return;
                case UnitMode.Kbps: value = bytesPerSec * 8.0 / 1000.0; unit = "Kbps"; return;
                case UnitMode.Mbps: value = bytesPerSec * 8.0 / 1000000.0; unit = "Mbps"; return;
                case UnitMode.AutoBits:
                    { double b = bytesPerSec * 8.0; string[] us = { "bps", "Kbps", "Mbps", "Gbps" }; int i = 0; while (b >= 1000 && i < us.Length - 1) { b /= 1000.0; i++; } value = b; unit = us[i]; return; }
                default:
                    { double b = bytesPerSec; string[] us = { "B/s", "KB/s", "MB/s", "GB/s" }; int i = 0; while (b >= 1024 && i < us.Length - 1) { b /= 1024.0; i++; } value = b; unit = us[i]; return; }
            }
        }

        public static string Bytes(long bytes)
        {
            double b = bytes; string[] us = { "B", "KB", "MB", "GB", "TB" };
            int i = 0; while (b >= 1024 && i < us.Length - 1) { b /= 1024.0; i++; }
            return (b >= 100 ? b.ToString("0", CultureInfo.InvariantCulture) : b.ToString("0.0", CultureInfo.InvariantCulture)) + " " + us[i];
        }

        public static double ToGB(long bytes) { return bytes / 1073741824.0; }
    }

    // --------------------------------------------------------- Background tools
    static class SpeedTest
    {
        const string Url = "https://speed.cloudflare.com/__down?bytes=250000000";
        public static bool Run(double maxSeconds, out double avgMbps, out double peakMbps)
        {
            avgMbps = 0; peakMbps = 0;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Url);
                req.Timeout = 15000; req.ReadWriteTimeout = 15000; req.UserAgent = "NetSpeedTray/" + App.Version;
                long total = 0, prevB = 0; double prevT = 0;
                Stopwatch sw = Stopwatch.StartNew();
                using (WebResponse resp = req.GetResponse())
                using (Stream st = resp.GetResponseStream())
                {
                    byte[] buf = new byte[65536]; int n;
                    while ((n = st.Read(buf, 0, buf.Length)) > 0)
                    {
                        total += n;
                        double t = sw.Elapsed.TotalSeconds;
                        if (t - prevT >= 0.25)
                        {
                            double inst = (total - prevB) * 8.0 / 1000000.0 / (t - prevT);
                            if (inst > peakMbps) peakMbps = inst;
                            prevB = total; prevT = t;
                        }
                        if (t > maxSeconds) break;
                    }
                }
                double secs = sw.Elapsed.TotalSeconds; if (secs <= 0) secs = 1;
                avgMbps = total * 8.0 / 1000000.0 / secs;
                return total > 0;
            }
            catch { return false; }
        }

        public static void Log(double avg, double peak)
        {
            try
            {
                if (!Directory.Exists(Settings.Dir)) Directory.CreateDirectory(Settings.Dir);
                string path = Path.Combine(Settings.Dir, "speedtests.csv");
                bool fresh = !File.Exists(path);
                using (StreamWriter w = new StreamWriter(path, true))
                {
                    if (fresh) w.WriteLine("datetime,avg_mbps,peak_mbps");
                    w.WriteLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "," +
                        avg.ToString("0.0", CultureInfo.InvariantCulture) + "," +
                        peak.ToString("0.0", CultureInfo.InvariantCulture));
                }
            }
            catch { }
        }
    }

    static class Updater
    {
        public static void CheckAsync(Action<string> onNewer)
        {
            Thread th = new Thread(() =>
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(
                        "https://api.github.com/repos/" + App.Owner + "/" + App.Repo + "/releases/latest");
                    req.UserAgent = "NetSpeedTray"; req.Timeout = 10000; req.Accept = "application/vnd.github+json";
                    using (WebResponse resp = req.GetResponse())
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                    {
                        string s = sr.ReadToEnd();
                        string tag = Extract(s, "\"tag_name\":\"", "\"");
                        if (!string.IsNullOrEmpty(tag) && IsNewer(tag, App.Version)) onNewer(tag);
                    }
                }
                catch { }
            });
            th.IsBackground = true; th.Start();
        }

        static string Extract(string s, string a, string b)
        {
            int i = s.IndexOf(a); if (i < 0) return null; i += a.Length;
            int j = s.IndexOf(b, i); if (j < 0) return null; return s.Substring(i, j - i);
        }
        static bool IsNewer(string tag, string cur)
        {
            int[] t = Parse(tag), c = Parse(cur);
            for (int i = 0; i < 3; i++) { if (t[i] > c[i]) return true; if (t[i] < c[i]) return false; }
            return false;
        }
        static int[] Parse(string v)
        {
            v = v.TrimStart('v', 'V'); string[] p = v.Split('.'); int[] r = new int[3];
            for (int i = 0; i < 3 && i < p.Length; i++)
            {
                string digits = ""; foreach (char ch in p[i]) { if (char.IsDigit(ch)) digits += ch; else break; }
                int x; int.TryParse(digits, out x); r[i] = x;
            }
            return r;
        }
    }

    // ------------------------------------------------------------- Tray context
    class TrayContext : ApplicationContext
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool DestroyIcon(IntPtr handle);

        static readonly Color IconDown = Color.FromArgb(56, 214, 107);
        static readonly Color IconUp = Color.FromArgb(61, 165, 255);

        readonly NotifyIcon _tray;
        readonly System.Windows.Forms.Timer _timer;
        readonly Monitor _mon = new Monitor();
        readonly UsageStore _usage = new UsageStore();
        readonly Settings _cfg;
        readonly Control _sync = new Control();
        PopupForm _popup;
        WidgetForm _widget;
        SpeedTestForm _speed;
        SettingsForm _settings;
        ToolStripMenuItem _widgetItem;
        IntPtr _lastIcon = IntPtr.Zero;

        Palette _pal;
        ThemeMode _effTheme;
        Ping _pinger;
        volatile int _pingMs = -2;      // -2 = n/a, -1 = timeout, >=0 = ms
        bool _pingBusy;
        int _tickCount;

        // alerts / state
        bool _first = true, _wasConnected; string _lastNet = "";
        string _capMonth = ""; bool _cap80, _cap100;
        DateTime _lastAutoTest = DateTime.Now;
        bool _autoTestRunning;

        public TrayContext()
        {
            _cfg = Settings.Load();
            var h = _sync.Handle; // force handle for cross-thread marshaling
            _effTheme = ResolveTheme();
            _pal = Palette.For(_effTheme);
            SyncStartup();

            _popup = new PopupForm(_pal);

            try { _pinger = new Ping(); _pinger.PingCompleted += PingDone; } catch { }

            _tray = new NotifyIcon();
            _tray.Visible = true;
            _tray.Text = "NetSpeedTray";
            _tray.ContextMenuStrip = BuildMenu();
            _tray.MouseClick += TrayClick;

            if (_cfg.WidgetVisible) ShowWidget();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += (s, e) => Update();
            _timer.Start();
            Update();

            if (_cfg.CheckUpdates)
                Updater.CheckAsync(tag => { try { _sync.BeginInvoke((Action)(() => Notify("Update available", "NetSpeedTray " + tag + " is available on GitHub."))); } catch { } });
        }

        // ------------------------------------------------------------- per tick
        void Update()
        {
            Sample s = _mon.Tick();
            _usage.Add(s.DeltaDown, s.DeltaUp);
            _tickCount++;

            // Auto theme follow.
            ThemeMode eff = ResolveTheme();
            if (eff != _effTheme) { _effTheme = eff; _pal = Palette.For(eff); ApplyPalette(); }

            // Ping (every 2s).
            if (_cfg.PingEnabled && s.Connected)
            {
                if (!_pingBusy && _pinger != null && (_tickCount % 2 == 0))
                {
                    _pingBusy = true;
                    try { _pinger.SendAsync(_cfg.PingHost, 1000, new object()); }
                    catch { _pingBusy = false; _pingMs = -2; }
                }
            }
            else _pingMs = -2;

            // Push data to windows.
            _popup.Push(s, _cfg);
            _popup.PingMs = _pingMs;
            _popup.TodayBytes = _usage.TodayDown + _usage.TodayUp;
            _popup.MonthBytes = _usage.MonthDown + _usage.MonthUp;
            _popup.CapGB = _cfg.MonthlyCapGB;
            if (_popup.Visible) _popup.Invalidate();

            if (_widget != null && _widget.Visible) _widget.SetData(s, _cfg.Unit, _pingMs);
            if (_speed != null && _speed.Visible) _speed.PushLive(s.DownBps, s.UpBps);

            // Tooltip.
            string tip = s.Connected
                ? "↓ " + Fmt.Full(s.DownBps, _cfg.Unit) + "  ↑ " + Fmt.Full(s.UpBps, _cfg.Unit)
                  + (_pingMs >= 0 ? "  " + _pingMs + "ms" : "") + "\n" + s.NetworkName
                : "Disconnected";
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            _tray.Text = tip;

            SetIcon(RenderIcon(s));

            // Connect / disconnect notifications.
            if (_first) { _first = false; _wasConnected = s.Connected; _lastNet = s.NetworkName; }
            else
            {
                if (s.Connected && (!_wasConnected || s.NetworkName != _lastNet))
                    Notify("Connected", "Connected to " + s.NetworkName);
                else if (!s.Connected && _wasConnected)
                    Notify("Disconnected", "Network connection lost.");
                _wasConnected = s.Connected; _lastNet = s.NetworkName;
            }

            CheckCap();
            CheckAutoTest();

            if (_tickCount % 20 == 0) _usage.Save();
        }

        void CheckCap()
        {
            string month = DateTime.Now.ToString("yyyy-MM");
            if (month != _capMonth) { _capMonth = month; _cap80 = false; _cap100 = false; }
            if (_cfg.MonthlyCapGB <= 0) return;
            double usedGB = Fmt.ToGB(_usage.MonthDown + _usage.MonthUp);
            if (!_cap100 && usedGB >= _cfg.MonthlyCapGB)
            { _cap100 = true; _cap80 = true; Notify("Data cap reached", "You've used " + usedGB.ToString("0.0") + " GB of your " + _cfg.MonthlyCapGB.ToString("0.#") + " GB cap."); }
            else if (!_cap80 && usedGB >= _cfg.MonthlyCapGB * 0.8)
            { _cap80 = true; Notify("Data cap 80%", "You've used " + usedGB.ToString("0.0") + " GB (80% of " + _cfg.MonthlyCapGB.ToString("0.#") + " GB)."); }
        }

        void CheckAutoTest()
        {
            if (_cfg.AutoTestHours <= 0 || _autoTestRunning) return;
            if ((DateTime.Now - _lastAutoTest).TotalHours < _cfg.AutoTestHours) return;
            _lastAutoTest = DateTime.Now;
            _autoTestRunning = true;
            Thread th = new Thread(() =>
            {
                double avg, peak;
                bool ok = SpeedTest.Run(8.0, out avg, out peak);
                if (ok) SpeedTest.Log(avg, peak);
                _autoTestRunning = false;
                if (ok) try { _sync.BeginInvoke((Action)(() => Notify("Scheduled speed test", "Download: " + avg.ToString("0.0") + " Mbps (peak " + peak.ToString("0.0") + ")"))); } catch { }
            });
            th.IsBackground = true; th.Start();
        }

        void PingDone(object sender, PingCompletedEventArgs e)
        {
            _pingBusy = false;
            try { _pingMs = (e.Reply != null && e.Reply.Status == IPStatus.Success) ? (int)e.Reply.RoundtripTime : -1; }
            catch { _pingMs = -1; }
        }

        void Notify(string title, string text)
        {
            try { _tray.ShowBalloonTip(5000, title, text, ToolTipIcon.Info); } catch { }
        }

        ThemeMode ResolveTheme()
        {
            if (_cfg.Theme != ThemeMode.Auto) return _cfg.Theme;
            bool light = false;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    if (k != null) { object v = k.GetValue("AppsUseLightTheme"); if (v is int) light = ((int)v) != 0; }
            }
            catch { }
            return light ? ThemeMode.Light : ThemeMode.Dark;
        }

        void ApplyPalette()
        {
            if (_popup != null) _popup.SetPalette(_pal);
            if (_widget != null) _widget.SetPalette(_pal);
            if (_speed != null) _speed.SetPalette(_pal);
            if (_settings != null) _settings.SetPalette(_pal);
        }

        void TrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) _popup.Toggle();
        }

        void SetIcon(Icon ic)
        {
            _tray.Icon = ic;
            if (_lastIcon != IntPtr.Zero) DestroyIcon(_lastIcon);
            _lastIcon = ic.Handle;
        }

        Icon RenderIcon(Sample s)
        {
            int sz = SystemInformation.SmallIconSize.Width; if (sz < 16) sz = 16;
            Bitmap bmp = new Bitmap(sz, sz);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (_cfg.Icon == IconMode.DownloadOnly)
                    DrawFitted(g, Fmt.Compact(s.DownBps, _cfg.Unit), 0, 0, sz, sz, IconDown);
                else
                {
                    int half = sz / 2;
                    DrawFitted(g, Fmt.Compact(s.DownBps, _cfg.Unit), 0, 0, sz, half, IconDown);
                    DrawFitted(g, Fmt.Compact(s.UpBps, _cfg.Unit), 0, half, sz, sz - half, IconUp);
                }
            }
            Icon ic = Icon.FromHandle(bmp.GetHicon());
            bmp.Dispose();
            return ic;
        }

        void DrawFitted(Graphics g, string text, int x, int y, int w, int h, Color col)
        {
            float px = h * 0.98f; Font f = null; SizeF size = SizeF.Empty;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (f != null) f.Dispose();
                f = new Font("Tahoma", px, FontStyle.Bold, GraphicsUnit.Pixel);
                size = g.MeasureString(text, f, new PointF(0, 0), StringFormat.GenericTypographic);
                if (size.Width <= w && size.Height <= h + 2) break;
                px -= h * 0.11f; if (px < 5) break;
            }
            float tx = x + (w - size.Width) / 2f, ty = y + (h - size.Height) / 2f;
            using (Brush halo = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                g.DrawString(text, f, halo, tx + 0.6f, ty + 0.6f, StringFormat.GenericTypographic);
                g.DrawString(text, f, halo, tx - 0.6f, ty - 0.6f, StringFormat.GenericTypographic);
            }
            using (Brush b = new SolidBrush(col)) g.DrawString(text, f, b, tx, ty, StringFormat.GenericTypographic);
            f.Dispose();
        }

        // ---------------------------------------------------------- Context menu
        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Open monitor", null, (s, e) => _popup.Toggle()));
            menu.Items.Add(new ToolStripMenuItem("Speed test (Mbps)…", null, (s, e) => OpenSpeedTest()));
            _widgetItem = new ToolStripMenuItem("Desktop widget", null, (s, e) => ToggleWidget());
            _widgetItem.Checked = _cfg.WidgetVisible;
            menu.Items.Add(_widgetItem);

            ToolStripMenuItem units = new ToolStripMenuItem("Units");
            AddUnit(units, "Auto (bytes)", UnitMode.AutoBytes);
            AddUnit(units, "Auto (bits)", UnitMode.AutoBits);
            units.DropDownItems.Add(new ToolStripSeparator());
            AddUnit(units, "KB/s", UnitMode.KBps);
            AddUnit(units, "MB/s", UnitMode.MBps);
            AddUnit(units, "Kbps", UnitMode.Kbps);
            AddUnit(units, "Mbps", UnitMode.Mbps);
            menu.Items.Add(units);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Settings…", null, (s, e) => OpenSettings()));
            menu.Items.Add(new ToolStripMenuItem("About", null, (s, e) =>
                MessageBox.Show("NetSpeedTray  v" + App.Version +
                    "\nLightweight live network speed monitor.\n\nGreen = download, Blue = upload.\nLeft-click the tray icon for details.\n\nMade by Devoryn Labs",
                    "About NetSpeedTray", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitApp()));
            return menu;
        }

        void AddUnit(ToolStripMenuItem parent, string label, UnitMode mode)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(label);
            it.Checked = _cfg.Unit == mode;
            it.Click += (s, e) =>
            {
                _cfg.Unit = mode; _cfg.Save();
                foreach (ToolStripItem sib in parent.DropDownItems) { var mi = sib as ToolStripMenuItem; if (mi != null) mi.Checked = false; }
                it.Checked = true; Update();
            };
            parent.DropDownItems.Add(it);
        }

        void OpenSpeedTest()
        {
            if (_speed == null || _speed.IsDisposed) _speed = new SpeedTestForm(_pal);
            _speed.ShowAtCursor();
        }

        void OpenSettings()
        {
            if (_settings == null || _settings.IsDisposed) _settings = new SettingsForm(_cfg, _pal, ApplySettings);
            _settings.Show(); _settings.BringToFront(); _settings.Activate();
        }

        void ApplySettings()
        {
            SyncStartup();
            _effTheme = ResolveTheme(); _pal = Palette.For(_effTheme); ApplyPalette();
            if (_cfg.WidgetVisible) ShowWidget(); else HideWidget();
            if (_widgetItem != null) _widgetItem.Checked = _cfg.WidgetVisible;
            // refresh unit checks in menu
            Update();
        }

        void ToggleWidget()
        {
            _cfg.WidgetVisible = !_cfg.WidgetVisible;
            if (_cfg.WidgetVisible) ShowWidget(); else HideWidget();
            _widgetItem.Checked = _cfg.WidgetVisible;
            _cfg.Save();
        }

        void ShowWidget()
        {
            if (_widget == null || _widget.IsDisposed)
                _widget = new WidgetForm(_pal, _cfg, () => _popup.Toggle(), OpenSettings, ExitApp);
            _widget.Show();
        }
        void HideWidget() { if (_widget != null) _widget.Hide(); }

        void SyncStartup()
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (rk == null) return;
                    if (_cfg.StartWithWindows) rk.SetValue("NetSpeedTray", "\"" + Application.ExecutablePath + "\"");
                    else if (rk.GetValue("NetSpeedTray") != null) rk.DeleteValue("NetSpeedTray", false);
                }
            }
            catch { }
        }

        void ExitApp()
        {
            _timer.Stop();
            _usage.Save();
            _tray.Visible = false; _tray.Dispose();
            if (_lastIcon != IntPtr.Zero) DestroyIcon(_lastIcon);
            if (_popup != null) _popup.Dispose();
            if (_widget != null) _widget.Dispose();
            if (_speed != null) _speed.Dispose();
            if (_settings != null) _settings.Dispose();
            ExitThread();
        }
    }

    // ---------------------------------------------------------- Shared drawing
    static class Draw
    {
        public static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void ConnIcon(Graphics g, float x, float y, float sz, Color col, bool wifi)
        {
            using (Pen pen = new Pen(col, Math.Max(1.5f, sz * 0.12f)))
            using (Brush br = new SolidBrush(col))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                if (wifi)
                {
                    float cx = x + sz / 2f, cy = y + sz * 0.80f, dr = sz * 0.11f;
                    g.FillEllipse(br, cx - dr, cy - dr, dr * 2, dr * 2);
                    for (int i = 1; i <= 3; i++) { float rad = sz * 0.19f * i; g.DrawArc(pen, cx - rad, cy - rad, rad * 2, rad * 2, 225f, 90f); }
                }
                else
                {
                    float w = sz * 0.60f, h = sz * 0.42f, bx = x + (sz - w) / 2f, by = y + sz * 0.28f;
                    using (GraphicsPath p = Rounded(new Rectangle((int)bx, (int)by, (int)Math.Round(w), (int)Math.Round(h)), Math.Max(1, (int)(sz * 0.07f))))
                        g.FillPath(br, p);
                    float tw = w * 0.34f, th = sz * 0.12f;
                    g.FillRectangle(br, bx + (w - tw) / 2f, by + h - 1, tw, th);
                    float mx = x + sz / 2f;
                    g.DrawLine(pen, mx, by, mx, y + sz * 0.10f);
                }
            }
        }

        public static Color PingColor(int ms, Palette p)
        {
            if (ms < 0) return p.Dim;
            if (ms < 60) return p.Down;
            if (ms < 120) return Color.FromArgb(240, 180, 60);
            return Color.FromArgb(230, 90, 90);
        }
    }

    // ------------------------------------------------------------- Popup window
    class PopupForm : Form
    {
        Palette _p;
        const int N = 60;
        readonly double[] _down = new double[N];
        readonly double[] _up = new double[N];
        Sample _s = new Sample();
        UnitMode _unit = UnitMode.AutoBytes;
        public int PingMs = -2;
        public long TodayBytes, MonthBytes;
        public double CapGB;

        public PopupForm(Palette pal)
        {
            _p = pal;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Width = 322; Height = 236;
            DoubleBuffered = true;
            Deactivate += (s, e) => Hide();
            ApplyRegion();
        }

        public void SetPalette(Palette pal) { _p = pal; if (Visible) Invalidate(); }
        void ApplyRegion() { using (GraphicsPath p = Draw.Rounded(new Rectangle(0, 0, Width, Height), 14)) Region = new Region(p); }

        public void Push(Sample s, Settings cfg)
        {
            _s = s; _unit = cfg.Unit;
            for (int i = 0; i < N - 1; i++) { _down[i] = _down[i + 1]; _up[i] = _up[i + 1]; }
            _down[N - 1] = s.DownBps; _up[N - 1] = s.UpBps;
        }

        public void Toggle()
        {
            if (Visible) { Hide(); return; }
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Left = wa.Right - Width - 12; Top = wa.Bottom - Height - 12;
            Show(); Activate(); Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(_p.Bg);
            int pad = 16;

            Color gc = _s.Connected ? _p.Down : Color.FromArgb(205, 90, 90);
            Draw.ConnIcon(g, pad, pad, 18f, gc, _s.Connected ? _s.IsWireless : true);
            using (Font hf = new Font("Segoe UI", 11.5f, FontStyle.Bold))
            using (Brush wb = new SolidBrush(_p.Text))
                g.DrawString(_s.Connected ? _s.NetworkName : "Disconnected", hf, wb, pad + 26, pad - 2);

            // Ping, top-right.
            string ping = PingMs == -2 ? "" : (PingMs < 0 ? "timeout" : PingMs + " ms");
            if (ping.Length > 0)
                using (Font pf = new Font("Segoe UI", 9f, FontStyle.Bold))
                using (Brush pb = new SolidBrush(Draw.PingColor(PingMs, _p)))
                {
                    SizeF ps = g.MeasureString(ping, pf);
                    g.DrawString(ping, pf, pb, Width - pad - ps.Width, pad - 1);
                }

            using (Font sf = new Font("Segoe UI", 8.25f))
            using (Brush sb = new SolidBrush(_p.Dim))
            {
                string type = _s.IsWireless ? "Wi-Fi" : "Ethernet";
                string sub = _s.Connected
                    ? (string.Equals(_s.AdapterName, type, StringComparison.OrdinalIgnoreCase) ? type : type + " · " + _s.AdapterName)
                    : "No active connection";
                g.DrawString(sub, sf, sb, pad + 26, pad + 17);
            }

            int ry = pad + 44;
            DrawReadout(g, pad, ry, "↓", Fmt.Full(_s.DownBps, _unit), _p.Down);
            DrawReadout(g, Width / 2 + 2, ry, "↑", Fmt.Full(_s.UpBps, _unit), _p.Up);

            Rectangle gr = new Rectangle(pad, ry + 52, Width - pad * 2, 50);
            DrawGraph(g, gr);

            using (Font tf = new Font("Segoe UI", 8.25f))
            using (Brush tb = new SolidBrush(_p.Dim))
            {
                g.DrawString("Used  ↓ " + Fmt.Bytes(_s.ConnDown) + "   ↑ " + Fmt.Bytes(_s.ConnUp), tf, tb, pad, gr.Bottom + 6);
                string month = "Month " + Fmt.Bytes(MonthBytes);
                if (CapGB > 0) month += " / " + CapGB.ToString("0.#", CultureInfo.InvariantCulture) + " GB";
                g.DrawString("Today " + Fmt.Bytes(TodayBytes) + "   ·   " + month, tf, tb, pad, gr.Bottom + 22);
            }

            using (Pen bp = new Pen(_p.Border))
            using (GraphicsPath p = Draw.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 14))
                g.DrawPath(bp, p);
        }

        void DrawReadout(Graphics g, int x, int y, string arrow, string val, Color col)
        {
            using (Font af = new Font("Segoe UI", 13f, FontStyle.Bold))
            using (Brush ab = new SolidBrush(col)) g.DrawString(arrow, af, ab, x, y);
            using (Font vf = new Font("Segoe UI Semibold", 15f, FontStyle.Bold))
            using (Brush vb = new SolidBrush(_p.Text)) g.DrawString(val, vf, vb, x + 22, y - 1);
        }

        void DrawGraph(Graphics g, Rectangle r)
        {
            using (Brush bg = new SolidBrush(_p.Card))
            using (GraphicsPath p = Draw.Rounded(r, 8)) g.FillPath(bg, p);
            double max = 1;
            for (int i = 0; i < N; i++) { if (_down[i] > max) max = _down[i]; if (_up[i] > max) max = _up[i]; }
            Series(g, r, _down, max, _p.Down);
            Series(g, r, _up, max, _p.Up);
        }

        void Series(Graphics g, Rectangle r, double[] data, double max, Color col)
        {
            PointF[] pts = new PointF[N];
            float dx = (float)r.Width / (N - 1);
            for (int i = 0; i < N; i++)
            {
                float x = r.Left + dx * i;
                float y = r.Bottom - 3 - (float)(data[i] / max) * (r.Height - 6);
                pts[i] = new PointF(x, y);
            }
            PointF[] fill = new PointF[N + 2];
            Array.Copy(pts, fill, N);
            fill[N] = new PointF(r.Right, r.Bottom); fill[N + 1] = new PointF(r.Left, r.Bottom);
            using (Brush fb = new SolidBrush(Color.FromArgb(40, col))) g.FillPolygon(fb, fill);
            using (Pen pen = new Pen(col, 1.6f)) { pen.LineJoin = LineJoin.Round; g.DrawLines(pen, pts); }
        }
    }

    // ------------------------------------------------------------ Desktop widget
    class WidgetForm : Form
    {
        Palette _p; Settings _cfg;
        readonly Action _openPopup, _openSettings, _exit;
        double _d, _u; int _ping = -2; UnitMode _unit = UnitMode.AutoBytes;
        bool _drag; Point _dragOff;

        public WidgetForm(Palette pal, Settings cfg, Action openPopup, Action openSettings, Action exit)
        {
            _p = pal; _cfg = cfg; _openPopup = openPopup; _openSettings = openSettings; _exit = exit;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Width = 176; Height = 60; DoubleBuffered = true;

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (cfg.WidgetX >= 0 && cfg.WidgetY >= 0) Location = new Point(cfg.WidgetX, cfg.WidgetY);
            else Location = new Point(wa.Right - Width - 24, wa.Top + 24);

            using (GraphicsPath p = Draw.Rounded(new Rectangle(0, 0, Width, Height), 12)) Region = new Region(p);

            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _drag = true; _dragOff = e.Location; } };
            MouseMove += (s, e) => { if (_drag) Location = new Point(Location.X + e.X - _dragOff.X, Location.Y + e.Y - _dragOff.Y); };
            MouseUp += (s, e) => { if (_drag) { _drag = false; _cfg.WidgetX = Location.X; _cfg.WidgetY = Location.Y; _cfg.Save(); } };
            DoubleClick += (s, e) => { if (_openPopup != null) _openPopup(); };

            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add(new ToolStripMenuItem("Open monitor", null, (s, e) => { if (_openPopup != null) _openPopup(); }));
            cm.Items.Add(new ToolStripMenuItem("Settings…", null, (s, e) => { if (_openSettings != null) _openSettings(); }));
            cm.Items.Add(new ToolStripSeparator());
            cm.Items.Add(new ToolStripMenuItem("Hide widget", null, (s, e) => { _cfg.WidgetVisible = false; _cfg.Save(); Hide(); }));
            cm.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => { if (_exit != null) _exit(); }));
            ContextMenuStrip = cm;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        public void SetPalette(Palette pal) { _p = pal; Invalidate(); }

        public void SetData(Sample s, UnitMode unit, int ping)
        {
            _d = s.DownBps; _u = s.UpBps; _unit = unit; _ping = ping;
            if (Visible) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(_p.Bg);

            using (Font af = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (Font vf = new Font("Segoe UI Semibold", 11f, FontStyle.Bold))
            using (Brush tb = new SolidBrush(_p.Text))
            {
                using (Brush db = new SolidBrush(_p.Down)) g.DrawString("↓", af, db, 12, 7);
                g.DrawString(Fmt.Full(_d, _unit), vf, tb, 32, 7);
                using (Brush ub = new SolidBrush(_p.Up)) g.DrawString("↑", af, ub, 12, 30);
                g.DrawString(Fmt.Full(_u, _unit), vf, tb, 32, 30);
            }
            string ping = _ping == -2 ? "" : (_ping < 0 ? "t/o" : _ping + "ms");
            if (ping.Length > 0)
                using (Font pf = new Font("Segoe UI", 8.25f, FontStyle.Bold))
                using (Brush pb = new SolidBrush(Draw.PingColor(_ping, _p)))
                {
                    SizeF ps = g.MeasureString(ping, pf);
                    g.DrawString(ping, pf, pb, Width - ps.Width - 10, 8);
                }

            using (Pen bp = new Pen(_p.Border))
            using (GraphicsPath p = Draw.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 12))
                g.DrawPath(bp, p);
        }
    }

    // ---------------------------------------------------------- Speed-test window
    class SpeedTestForm : Form
    {
        Palette _p;
        readonly Button _btn;
        readonly System.Windows.Forms.Timer _ui;
        Thread _worker;
        volatile bool _running;
        long _bytes, _prevBytes; double _prevT;
        readonly Stopwatch _sw = new Stopwatch();
        const int N = 120;
        readonly double[] _hist = new double[N];
        double _curMbps, _peakMbps, _avgMbps;
        string _status = "Idle · showing live usage";
        const string TestUrl = "https://speed.cloudflare.com/__down?bytes=300000000";

        public SpeedTestForm(Palette pal)
        {
            _p = pal;
            Text = "NetSpeedTray – Speed Test";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(440, 320); DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _btn = new Button();
            _btn.Text = "Run download test"; _btn.FlatStyle = FlatStyle.Flat;
            _btn.Size = new Size(180, 34);
            _btn.Location = new Point(ClientSize.Width - 180 - 18, ClientSize.Height - 34 - 16);
            _btn.Cursor = Cursors.Hand;
            _btn.Click += (s, e) => { if (_running) Stop(); else Start(); };
            Controls.Add(_btn); StyleButton();

            _ui = new System.Windows.Forms.Timer(); _ui.Interval = 200; _ui.Tick += (s, e) => UiTick();
            FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Stop(); Hide(); } };
        }

        public void SetPalette(Palette pal) { _p = pal; StyleButton(); Invalidate(); }
        void StyleButton()
        {
            _btn.BackColor = _running ? Color.FromArgb(200, 70, 70) : _p.Up;
            _btn.ForeColor = Color.White; _btn.FlatAppearance.BorderSize = 0;
            _btn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        }

        public void SeedDemo()
        {
            Random r = new Random(3); double v = 0;
            for (int i = 0; i < N; i++) { double target = 150 + 55 * Math.Sin(i * 0.12); v = v * 0.55 + target * 0.45 + r.Next(-10, 10); if (v < 0) v = 0; _hist[i] = v; }
            _curMbps = _hist[N - 1]; _peakMbps = 0;
            for (int i = 0; i < N; i++) if (_hist[i] > _peakMbps) _peakMbps = _hist[i];
            _avgMbps = 148.6; _status = "Test complete"; _btn.Text = "Run download test";
        }

        public void ShowAtCursor()
        {
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Left = Math.Max(wa.Left, wa.Right - Width - 40);
            Top = Math.Max(wa.Top, wa.Bottom - Height - 60);
            Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            BringToFront(); Activate(); if (!_running) _ui.Start();
        }

        public void PushLive(double downBps, double upBps)
        {
            if (_running) return;
            _curMbps = Fmt.Mbps(downBps); Buffer(_curMbps); if (!_ui.Enabled) Invalidate();
        }
        void Buffer(double v) { for (int i = 0; i < N - 1; i++) _hist[i] = _hist[i + 1]; _hist[N - 1] = v; }

        void Start()
        {
            if (_running) return;
            _running = true; Interlocked.Exchange(ref _bytes, 0);
            _prevBytes = 0; _prevT = 0; _peakMbps = 0; _avgMbps = 0; _curMbps = 0;
            for (int i = 0; i < N; i++) _hist[i] = 0;
            _status = "Connecting…"; _sw.Restart(); _btn.Text = "Cancel"; StyleButton(); _ui.Start();
            _worker = new Thread(Download) { IsBackground = true }; _worker.Start();
        }
        void Stop() { _running = false; _sw.Stop(); _btn.Text = "Run download test"; StyleButton(); }

        void Download()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(TestUrl);
                req.Timeout = 15000; req.ReadWriteTimeout = 15000; req.AllowAutoRedirect = true;
                req.UserAgent = "NetSpeedTray/" + App.Version; req.KeepAlive = true;
                using (WebResponse resp = req.GetResponse())
                using (Stream st = resp.GetResponseStream())
                {
                    byte[] buf = new byte[65536]; int n;
                    while (_running && (n = st.Read(buf, 0, buf.Length)) > 0)
                    {
                        Interlocked.Add(ref _bytes, n);
                        if (_sw.Elapsed.TotalSeconds > 12.0) break;
                    }
                }
                SetStatus(_running ? "Test complete" : "Cancelled");
                if (_avgMbps > 0) SpeedTest.Log(_avgMbps, _peakMbps);
            }
            catch (Exception ex) { SetStatus("Error: " + ex.Message.Split('\n')[0]); }
            _running = false;
        }
        void SetStatus(string s) { try { if (IsHandleCreated) BeginInvoke((Action)(() => { _status = s; })); } catch { } }

        void UiTick()
        {
            if (_running)
            {
                double t = _sw.Elapsed.TotalSeconds; long b = Interlocked.Read(ref _bytes);
                double dt = t - _prevT; if (dt < 0.001) dt = 0.001;
                double inst = (b - _prevBytes) * 8.0 / 1000000.0 / dt;
                _prevBytes = b; _prevT = t; _curMbps = inst;
                if (inst > _peakMbps) _peakMbps = inst;
                if (t > 0.5) _avgMbps = b * 8.0 / 1000000.0 / t;
                Buffer(inst);
                if (t >= 0.5 && _status == "Connecting…") _status = "Testing…";
            }
            else if (_btn.Text == "Cancel") { _btn.Text = "Run download test"; StyleButton(); }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(_p.Bg);
            int pad = 18;
            using (Font hf = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (Brush tb = new SolidBrush(_p.Text)) g.DrawString("Download speed", hf, tb, pad, pad - 2);
            using (Font sf = new Font("Segoe UI", 8.5f))
            using (Brush sb = new SolidBrush(_p.Dim)) g.DrawString(_status, sf, sb, pad, pad + 20);

            using (Font bf = new Font("Segoe UI", 40f, FontStyle.Bold))
            using (Brush bb = new SolidBrush(_p.Down)) g.DrawString(_curMbps.ToString("0.0", CultureInfo.InvariantCulture), bf, bb, pad - 4, pad + 36);
            using (Font uf = new Font("Segoe UI", 13f, FontStyle.Bold))
            using (Brush ub = new SolidBrush(_p.Dim)) g.DrawString("Mbps", uf, ub, pad + MeasureBig(g, _curMbps), pad + 70);

            int tileY = pad + 44;
            Stat(g, Width - pad - 150, tileY, "Peak", _peakMbps);
            Stat(g, Width - pad - 150, tileY + 36, "Average", _avgMbps);

            Rectangle gr = new Rectangle(pad, pad + 118, Width - pad * 2, 118);
            DrawGraph(g, gr);
        }
        float MeasureBig(Graphics g, double v) { using (Font bf = new Font("Segoe UI", 40f, FontStyle.Bold)) return g.MeasureString(v.ToString("0.0", CultureInfo.InvariantCulture), bf).Width - 8; }
        void Stat(Graphics g, int x, int y, string label, double mbps)
        {
            using (Font lf = new Font("Segoe UI", 8.5f))
            using (Brush lb = new SolidBrush(_p.Dim)) g.DrawString(label, lf, lb, x, y);
            using (Font vf = new Font("Segoe UI Semibold", 13f, FontStyle.Bold))
            using (Brush vb = new SolidBrush(_p.Text)) g.DrawString(mbps.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps", vf, vb, x + 64, y - 3);
        }
        void DrawGraph(Graphics g, Rectangle r)
        {
            using (Brush bg = new SolidBrush(_p.Card))
            using (GraphicsPath p = Draw.Rounded(r, 8)) g.FillPath(bg, p);
            double max = 1; for (int i = 0; i < N; i++) if (_hist[i] > max) max = _hist[i]; max *= 1.15;
            using (Font af = new Font("Segoe UI", 7.5f))
            using (Brush ab = new SolidBrush(_p.Dim))
            { g.DrawString(max.ToString("0", CultureInfo.InvariantCulture), af, ab, r.Left + 4, r.Top + 2); g.DrawString("0", af, ab, r.Left + 4, r.Bottom - 14); }
            PointF[] pts = new PointF[N]; float dx = (float)r.Width / (N - 1);
            for (int i = 0; i < N; i++) { float x = r.Left + dx * i; float y = r.Bottom - 4 - (float)(_hist[i] / max) * (r.Height - 8); pts[i] = new PointF(x, y); }
            PointF[] fill = new PointF[N + 2]; Array.Copy(pts, fill, N);
            fill[N] = new PointF(r.Right, r.Bottom); fill[N + 1] = new PointF(r.Left, r.Bottom);
            using (Brush fb = new SolidBrush(Color.FromArgb(46, _p.Down))) g.FillPolygon(fb, fill);
            using (Pen pen = new Pen(_p.Down, 1.8f)) { pen.LineJoin = LineJoin.Round; g.DrawLines(pen, pts); }
        }
    }

    // ------------------------------------------------------------- Settings form
    class SettingsForm : Form
    {
        readonly Settings _cfg; Palette _p; readonly Action _onApply;
        ComboBox _unit, _theme, _icon, _autotest;
        TextBox _cap, _pingHost;
        CheckBox _ping, _startup, _updates, _widget;

        public SettingsForm(Settings cfg, Palette pal, Action onApply)
        {
            _cfg = cfg; _p = pal; _onApply = onApply;
            Text = "NetSpeedTray – Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(360, 430);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Build();
            SetPalette(pal);
        }

        public void SetPalette(Palette pal)
        {
            _p = pal;
            BackColor = pal.Bg; ForeColor = pal.Text;
            foreach (Control c in Controls) { c.ForeColor = pal.Text; if (c is TextBox || c is ComboBox) c.BackColor = pal.Card; }
        }

        int _y = 16;
        Label AddLabel(string t) { Label l = new Label(); l.Text = t; l.AutoSize = true; l.Location = new Point(16, _y + 4); Controls.Add(l); return l; }
        void Row(string label, Control c) { AddLabel(label); c.Location = new Point(170, _y); c.Width = 172; Controls.Add(c); _y += 34; }

        void Build()
        {
            _unit = Combo(new[] { "Auto (bytes)", "Auto (bits)", "KB/s", "MB/s", "Kbps", "Mbps" });
            _unit.SelectedIndex = (int)_cfg.Unit; Row("Units", _unit);

            _theme = Combo(new[] { "Auto (follow Windows)", "Dark", "Light" });
            _theme.SelectedIndex = (int)_cfg.Theme; Row("Theme", _theme);

            _icon = Combo(new[] { "Two lines (down + up)", "Download only" });
            _icon.SelectedIndex = (int)_cfg.Icon; Row("Tray icon", _icon);

            _cap = new TextBox(); _cap.Text = _cfg.MonthlyCapGB > 0 ? _cfg.MonthlyCapGB.ToString(CultureInfo.InvariantCulture) : "";
            Row("Monthly data cap (GB, 0 = off)", _cap);

            _ping = new CheckBox(); _ping.Text = "Show ping / latency"; _ping.Checked = _cfg.PingEnabled; _ping.AutoSize = true;
            _ping.Location = new Point(16, _y); Controls.Add(_ping); _y += 28;
            _pingHost = new TextBox(); _pingHost.Text = _cfg.PingHost; Row("Ping host", _pingHost);

            _autotest = Combo(new[] { "Off", "Every 6 hours", "Every 12 hours", "Every 24 hours" });
            _autotest.SelectedIndex = _cfg.AutoTestHours == 6 ? 1 : _cfg.AutoTestHours == 12 ? 2 : _cfg.AutoTestHours == 24 ? 3 : 0;
            Row("Auto speed test", _autotest);

            _widget = Check("Show floating desktop widget", _cfg.WidgetVisible);
            _startup = Check("Run at Windows startup", _cfg.StartWithWindows);
            _updates = Check("Check for updates on launch", _cfg.CheckUpdates);

            Button open = new Button(); open.Text = "Open data folder"; open.AutoSize = true;
            open.Location = new Point(16, _y + 6);
            open.Click += (s, e) => { try { Process.Start("explorer.exe", Settings.Dir); } catch { } };
            Controls.Add(open);

            Button save = new Button(); save.Text = "Save"; save.Width = 90; save.Location = new Point(ClientSize.Width - 200, ClientSize.Height - 40);
            save.Click += (s, e) => { SaveAll(); Close(); };
            Button cancel = new Button(); cancel.Text = "Cancel"; cancel.Width = 90; cancel.Location = new Point(ClientSize.Width - 104, ClientSize.Height - 40);
            cancel.Click += (s, e) => Close();
            Controls.Add(save); Controls.Add(cancel);
            AcceptButton = save; CancelButton = cancel;
        }

        ComboBox Combo(string[] items) { ComboBox c = new ComboBox(); c.DropDownStyle = ComboBoxStyle.DropDownList; c.Items.AddRange(items); c.FlatStyle = FlatStyle.Flat; return c; }
        CheckBox Check(string text, bool val) { CheckBox c = new CheckBox(); c.Text = text; c.Checked = val; c.AutoSize = true; c.Location = new Point(16, _y); Controls.Add(c); _y += 28; return c; }

        void SaveAll()
        {
            _cfg.Unit = (UnitMode)_unit.SelectedIndex;
            _cfg.Theme = (ThemeMode)_theme.SelectedIndex;
            _cfg.Icon = (IconMode)_icon.SelectedIndex;
            double cap; _cfg.MonthlyCapGB = double.TryParse(_cap.Text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out cap) && cap > 0 ? cap : 0;
            _cfg.PingEnabled = _ping.Checked;
            if (_pingHost.Text.Trim().Length > 0) _cfg.PingHost = _pingHost.Text.Trim();
            _cfg.AutoTestHours = _autotest.SelectedIndex == 1 ? 6 : _autotest.SelectedIndex == 2 ? 12 : _autotest.SelectedIndex == 3 ? 24 : 0;
            _cfg.WidgetVisible = _widget.Checked;
            _cfg.StartWithWindows = _startup.Checked;
            _cfg.CheckUpdates = _updates.Checked;
            _cfg.Save();
            if (_onApply != null) _onApply();
        }
    }

    // ---------------------------------------------------------- Screenshot mode
    static class Screenshots
    {
        public static void Run(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { }
            Settings cfg = new Settings(); cfg.Unit = UnitMode.AutoBytes;
            Capture(BuildPopup(ThemeMode.Dark, cfg), Path.Combine(dir, "popup-dark.png"));
            Capture(BuildPopup(ThemeMode.Light, cfg), Path.Combine(dir, "popup-light.png"));
            Capture(BuildSpeed(ThemeMode.Dark), Path.Combine(dir, "speedtest-dark.png"));
            Capture(BuildSpeed(ThemeMode.Light), Path.Combine(dir, "speedtest-light.png"));
        }

        static PopupForm BuildPopup(ThemeMode t, Settings cfg)
        {
            PopupForm pf = new PopupForm(Palette.For(t));
            Random r = new Random(11);
            for (int i = 0; i < 90; i++)
            {
                Sample s = new Sample();
                s.Connected = true; s.NetworkName = "Home_WiFi_5G"; s.AdapterName = "Wi-Fi"; s.IsWireless = true;
                double d = 2600000 + 1700000 * Math.Sin(i * 0.19) + r.Next(-250000, 250000);
                double u = 520000 + 360000 * Math.Sin(i * 0.16 + 1.0) + r.Next(-60000, 60000);
                s.DownBps = d < 0 ? 0 : d; s.UpBps = u < 0 ? 0 : u;
                s.ConnDown = 3456000000L; s.ConnUp = 612000000L;
                pf.Push(s, cfg);
            }
            pf.PingMs = 14; pf.TodayBytes = 4200000000L; pf.MonthBytes = 92000000000L; pf.CapGB = 150;
            return pf;
        }

        static SpeedTestForm BuildSpeed(ThemeMode t) { SpeedTestForm sf = new SpeedTestForm(Palette.For(t)); sf.SeedDemo(); return sf; }

        static void Capture(Form f, string path)
        {
            try
            {
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-4000, -4000);
                IntPtr h = f.Handle; f.Show(); Application.DoEvents();
                Rectangle rc = f.ClientRectangle;
                using (Bitmap bmp = new Bitmap(rc.Width, rc.Height))
                {
                    f.DrawToBitmap(bmp, rc); bmp.Save(path, ImageFormat.Png);
                }
                f.Hide();
            }
            catch { }
            finally { f.Dispose(); }
        }
    }
}
