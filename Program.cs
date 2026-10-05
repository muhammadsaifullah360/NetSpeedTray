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
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Hidden mode: render the UI to PNG files for documentation, then exit.
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
    enum ThemeMode { Dark, Light }

    class Settings
    {
        public UnitMode Unit = UnitMode.AutoBytes;
        public ThemeMode Theme = ThemeMode.Dark;
        public bool StartWithWindows = false;

        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetSpeedTray"); } }
        static string FilePath { get { return Path.Combine(Dir, "config.ini"); } }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string raw in File.ReadAllLines(FilePath))
                    {
                        string line = raw.Trim();
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                        string v = line.Substring(eq + 1).Trim();
                        if (k == "unit") { try { s.Unit = (UnitMode)Enum.Parse(typeof(UnitMode), v, true); } catch { } }
                        else if (k == "theme") { try { s.Theme = (ThemeMode)Enum.Parse(typeof(ThemeMode), v, true); } catch { } }
                        else if (k == "startwithwindows") s.StartWithWindows = (v == "1" || v.ToLowerInvariant() == "true");
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
                sb.AppendLine("unit=" + Unit.ToString());
                sb.AppendLine("theme=" + Theme.ToString());
                sb.AppendLine("startwithwindows=" + (StartWithWindows ? "1" : "0"));
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
                p.Bg = Color.FromArgb(244, 245, 248);
                p.Card = Color.FromArgb(255, 255, 255);
                p.Text = Color.FromArgb(26, 28, 34);
                p.Dim = Color.FromArgb(107, 114, 128);
                p.Border = Color.FromArgb(32, 0, 0, 0);
                p.Down = Color.FromArgb(22, 163, 74);
                p.Up = Color.FromArgb(37, 99, 235);
            }
            else
            {
                p.Bg = Color.FromArgb(24, 25, 32);
                p.Card = Color.FromArgb(33, 35, 46);
                p.Text = Color.FromArgb(240, 242, 248);
                p.Dim = Color.FromArgb(150, 158, 178);
                p.Border = Color.FromArgb(60, 255, 255, 255);
                p.Down = Color.FromArgb(56, 214, 107);
                p.Up = Color.FromArgb(61, 165, 255);
            }
            return p;
        }
    }

    // ------------------------------------------------------------- Measurement
    class Sample
    {
        public double DownBps;
        public double UpBps;
        public string NetworkName = "Disconnected";
        public string AdapterName = "";
        public bool IsWireless;
        public bool Connected;
        public long SessionDown;
        public long SessionUp;
    }

    class Monitor
    {
        class Counter { public long Rx; public long Tx; }
        readonly Dictionary<string, Counter> _prev = new Dictionary<string, Counter>();
        readonly Stopwatch _sw = new Stopwatch();
        long _sessionDown, _sessionUp;
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

            double down = 0, up = 0;
            bool connected = false;

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
                        down = dRx / dt;
                        up = dTx / dt;
                        _sessionDown += dRx;
                        _sessionUp += dTx;
                        connected = true;
                    }
                    c.Rx = rx; c.Tx = tx;
                }
                else _prev[id] = new Counter { Rx = rx, Tx = tx };
            }

            Sample s = new Sample();
            s.DownBps = down;
            s.UpBps = up;
            s.Connected = connected && _primaryId != null;
            s.NetworkName = s.Connected ? _networkName : "Disconnected";
            s.AdapterName = _adapterName;
            s.IsWireless = _wireless;
            s.SessionDown = _sessionDown;
            s.SessionUp = _sessionUp;
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
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
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
            double v; string unit;
            Scale(bytesPerSec, mode, out v, out unit);
            string num = v >= 100 ? v.ToString("0", CultureInfo.InvariantCulture)
                       : v.ToString("0.0", CultureInfo.InvariantCulture);
            return num + " " + unit;
        }

        public static string Compact(double bytesPerSec, UnitMode mode)
        {
            bool bits = mode == UnitMode.AutoBits || mode == UnitMode.Kbps || mode == UnitMode.Mbps;
            double v = bits ? bytesPerSec * 8.0 : bytesPerSec;
            string[] u = { "", "K", "M", "G" };
            int i = 0;
            while (v >= 1000 && i < u.Length - 1) { v /= 1024.0; i++; }
            string num;
            if (i == 0) num = v.ToString("0", CultureInfo.InvariantCulture);
            else if (v < 10) num = v.ToString("0.0", CultureInfo.InvariantCulture);
            else num = v.ToString("0", CultureInfo.InvariantCulture);
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
                    {
                        double b = bytesPerSec * 8.0;
                        string[] us = { "bps", "Kbps", "Mbps", "Gbps" };
                        int i = 0; while (b >= 1000 && i < us.Length - 1) { b /= 1000.0; i++; }
                        value = b; unit = us[i]; return;
                    }
                default:
                    {
                        double b = bytesPerSec;
                        string[] us = { "B/s", "KB/s", "MB/s", "GB/s" };
                        int i = 0; while (b >= 1024 && i < us.Length - 1) { b /= 1024.0; i++; }
                        value = b; unit = us[i]; return;
                    }
            }
        }

        public static string Bytes(long bytes)
        {
            double b = bytes; string[] us = { "B", "KB", "MB", "GB", "TB" };
            int i = 0; while (b >= 1024 && i < us.Length - 1) { b /= 1024.0; i++; }
            return (b >= 100 ? b.ToString("0", CultureInfo.InvariantCulture) : b.ToString("0.0", CultureInfo.InvariantCulture)) + " " + us[i];
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
        readonly Settings _cfg;
        readonly PopupForm _popup;
        SpeedTestForm _speed;
        IntPtr _lastIcon = IntPtr.Zero;

        public TrayContext()
        {
            _cfg = Settings.Load();
            SyncStartup();

            _popup = new PopupForm(Palette.For(_cfg.Theme));

            _tray = new NotifyIcon();
            _tray.Visible = true;
            _tray.Text = "NetSpeedTray";
            _tray.ContextMenuStrip = BuildMenu();
            _tray.MouseClick += TrayClick;

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 1000;
            _timer.Tick += (s, e) => Update();
            _timer.Start();
            Update();
        }

        void TrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) _popup.Toggle();
        }

        void Update()
        {
            Sample s = _mon.Tick();
            _popup.Push(s, _cfg);
            if (_speed != null && _speed.Visible) _speed.PushLive(s.DownBps, s.UpBps);

            string tip = s.Connected
                ? "↓ " + Fmt.Full(s.DownBps, _cfg.Unit) + "   ↑ " + Fmt.Full(s.UpBps, _cfg.Unit) + "\n" + s.NetworkName
                : "Disconnected";
            if (tip.Length > 63) tip = tip.Substring(0, 63);
            _tray.Text = tip;

            SetIcon(RenderIcon(s));
            if (_popup.Visible) _popup.Invalidate();
        }

        void SetIcon(Icon ic)
        {
            _tray.Icon = ic;
            if (_lastIcon != IntPtr.Zero) DestroyIcon(_lastIcon);
            _lastIcon = ic.Handle;
        }

        Icon RenderIcon(Sample s)
        {
            int sz = SystemInformation.SmallIconSize.Width;
            if (sz < 16) sz = 16;
            Bitmap bmp = new Bitmap(sz, sz);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int half = sz / 2;
                DrawFitted(g, Fmt.Compact(s.DownBps, _cfg.Unit), 0, 0, sz, half, IconDown);
                DrawFitted(g, Fmt.Compact(s.UpBps, _cfg.Unit), 0, half, sz, sz - half, IconUp);
            }
            Icon ic = Icon.FromHandle(bmp.GetHicon());
            bmp.Dispose();
            return ic;
        }

        void DrawFitted(Graphics g, string text, int x, int y, int w, int h, Color col)
        {
            float px = h * 0.98f;
            Font f = null;
            SizeF size = SizeF.Empty;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (f != null) f.Dispose();
                f = new Font("Tahoma", px, FontStyle.Bold, GraphicsUnit.Pixel);
                size = g.MeasureString(text, f, new PointF(0, 0), StringFormat.GenericTypographic);
                if (size.Width <= w && size.Height <= h + 2) break;
                px -= h * 0.11f;
                if (px < 5) break;
            }
            float tx = x + (w - size.Width) / 2f;
            float ty = y + (h - size.Height) / 2f;
            using (Brush halo = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                g.DrawString(text, f, halo, tx + 0.6f, ty + 0.6f, StringFormat.GenericTypographic);
                g.DrawString(text, f, halo, tx - 0.6f, ty - 0.6f, StringFormat.GenericTypographic);
            }
            using (Brush b = new SolidBrush(col))
                g.DrawString(text, f, b, tx, ty, StringFormat.GenericTypographic);
            f.Dispose();
        }

        // ---------------------------------------------------------- Context menu
        ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Open monitor", null, (s, e) => _popup.Toggle()));
            menu.Items.Add(new ToolStripMenuItem("Speed test (Mbps)…", null, (s, e) => OpenSpeedTest()));
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem units = new ToolStripMenuItem("Units");
            AddUnit(units, "Auto (bytes)", UnitMode.AutoBytes);
            AddUnit(units, "Auto (bits)", UnitMode.AutoBits);
            units.DropDownItems.Add(new ToolStripSeparator());
            AddUnit(units, "KB/s", UnitMode.KBps);
            AddUnit(units, "MB/s", UnitMode.MBps);
            AddUnit(units, "Kbps", UnitMode.Kbps);
            AddUnit(units, "Mbps", UnitMode.Mbps);
            menu.Items.Add(units);

            ToolStripMenuItem theme = new ToolStripMenuItem("Light theme");
            theme.Checked = _cfg.Theme == ThemeMode.Light;
            theme.Click += (s, e) =>
            {
                _cfg.Theme = _cfg.Theme == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light;
                theme.Checked = _cfg.Theme == ThemeMode.Light;
                Palette pal = Palette.For(_cfg.Theme);
                _popup.SetPalette(pal);
                if (_speed != null) _speed.SetPalette(pal);
                _cfg.Save();
            };
            menu.Items.Add(theme);

            ToolStripMenuItem startup = new ToolStripMenuItem("Run at Windows startup");
            startup.Checked = _cfg.StartWithWindows;
            startup.Click += (s, e) =>
            {
                _cfg.StartWithWindows = !_cfg.StartWithWindows;
                startup.Checked = _cfg.StartWithWindows;
                SyncStartup();
                _cfg.Save();
            };
            menu.Items.Add(startup);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("About", null, (s, e) =>
                MessageBox.Show("NetSpeedTray\nLightweight live network speed monitor.\n\nGreen = download, Blue = upload.\nLeft-click the tray icon for details.",
                    "About NetSpeedTray", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => ExitApp()));
            return menu;
        }

        void OpenSpeedTest()
        {
            if (_speed == null || _speed.IsDisposed)
                _speed = new SpeedTestForm(Palette.For(_cfg.Theme));
            _speed.ShowAtCursor();
        }

        void AddUnit(ToolStripMenuItem parent, string label, UnitMode mode)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(label);
            it.Checked = _cfg.Unit == mode;
            it.Click += (s, e) =>
            {
                _cfg.Unit = mode;
                _cfg.Save();
                foreach (ToolStripItem sib in parent.DropDownItems)
                {
                    ToolStripMenuItem mi = sib as ToolStripMenuItem;
                    if (mi != null) mi.Checked = false;
                }
                it.Checked = true;
                Update();
            };
            parent.DropDownItems.Add(it);
        }

        void SyncStartup()
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (rk == null) return;
                    if (_cfg.StartWithWindows)
                        rk.SetValue("NetSpeedTray", "\"" + Application.ExecutablePath + "\"");
                    else if (rk.GetValue("NetSpeedTray") != null)
                        rk.DeleteValue("NetSpeedTray", false);
                }
            }
            catch { }
        }

        void ExitApp()
        {
            _timer.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            if (_lastIcon != IntPtr.Zero) DestroyIcon(_lastIcon);
            _popup.Dispose();
            if (_speed != null) _speed.Dispose();
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

        public PopupForm(Palette pal)
        {
            _p = pal;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Width = 320; Height = 210;
            DoubleBuffered = true;
            Deactivate += (s, e) => Hide();
            ApplyRegion();
        }

        public void SetPalette(Palette pal) { _p = pal; if (Visible) Invalidate(); }

        void ApplyRegion()
        {
            using (GraphicsPath p = Draw.Rounded(new Rectangle(0, 0, Width, Height), 14))
                Region = new Region(p);
        }

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
            Left = wa.Right - Width - 12;
            Top = wa.Bottom - Height - 12;
            Show();
            Activate();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(_p.Bg);
            int pad = 16;

            Color gc = _s.Connected ? _p.Down : Color.FromArgb(205, 90, 90);
            DrawConnIcon(g, pad, pad, 18f, gc, _s.Connected ? _s.IsWireless : true);
            using (Font hf = new Font("Segoe UI", 11.5f, FontStyle.Bold))
            using (Brush wb = new SolidBrush(_p.Text))
                g.DrawString(_s.Connected ? _s.NetworkName : "Disconnected", hf, wb, pad + 26, pad - 2);
            using (Font sf = new Font("Segoe UI", 8.25f))
            using (Brush sb = new SolidBrush(_p.Dim))
            {
                string type = _s.IsWireless ? "Wi-Fi" : "Ethernet";
                string sub = _s.Connected
                    ? (string.Equals(_s.AdapterName, type, StringComparison.OrdinalIgnoreCase)
                        ? type
                        : type + " · " + _s.AdapterName)
                    : "No active connection";
                g.DrawString(sub, sf, sb, pad + 26, pad + 17);
            }

            int ry = pad + 42;
            DrawReadout(g, pad, ry, "↓", Fmt.Full(_s.DownBps, _unit), _p.Down);
            DrawReadout(g, Width / 2 + 2, ry, "↑", Fmt.Full(_s.UpBps, _unit), _p.Up);

            Rectangle gr = new Rectangle(pad, ry + 56, Width - pad * 2, 54);
            DrawGraph(g, gr);

            using (Font tf = new Font("Segoe UI", 8.25f))
            using (Brush tb = new SolidBrush(_p.Dim))
                g.DrawString("Session  ↓ " + Fmt.Bytes(_s.SessionDown) + "   ↑ " + Fmt.Bytes(_s.SessionUp),
                    tf, tb, pad, gr.Bottom + 6);

            using (Pen bp = new Pen(_p.Border))
            using (GraphicsPath p = Draw.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 14))
                g.DrawPath(bp, p);
        }

        // Draws a Wi-Fi arcs glyph or an Ethernet-port glyph.
        void DrawConnIcon(Graphics g, float x, float y, float sz, Color col, bool wifi)
        {
            using (Pen pen = new Pen(col, Math.Max(1.5f, sz * 0.12f)))
            using (Brush br = new SolidBrush(col))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                if (wifi)
                {
                    float cx = x + sz / 2f;
                    float cy = y + sz * 0.80f;
                    float dr = sz * 0.11f;
                    g.FillEllipse(br, cx - dr, cy - dr, dr * 2, dr * 2);
                    for (int i = 1; i <= 3; i++)
                    {
                        float rad = sz * 0.19f * i;
                        g.DrawArc(pen, cx - rad, cy - rad, rad * 2, rad * 2, 225f, 90f);
                    }
                }
                else
                {
                    float w = sz * 0.60f, h = sz * 0.42f;
                    float bx = x + (sz - w) / 2f, by = y + sz * 0.28f;
                    using (GraphicsPath p = Draw.Rounded(
                        new Rectangle((int)bx, (int)by, (int)Math.Round(w), (int)Math.Round(h)),
                        Math.Max(1, (int)(sz * 0.07f))))
                        g.FillPath(br, p);
                    float tw = w * 0.34f, th = sz * 0.12f;
                    g.FillRectangle(br, bx + (w - tw) / 2f, by + h - 1, tw, th);
                    float mx = x + sz / 2f;
                    g.DrawLine(pen, mx, by, mx, y + sz * 0.10f);
                }
            }
        }

        void DrawReadout(Graphics g, int x, int y, string arrow, string val, Color col)
        {
            using (Font af = new Font("Segoe UI", 13f, FontStyle.Bold))
            using (Brush ab = new SolidBrush(col))
                g.DrawString(arrow, af, ab, x, y);
            using (Font vf = new Font("Segoe UI Semibold", 15f, FontStyle.Bold))
            using (Brush vb = new SolidBrush(_p.Text))
                g.DrawString(val, vf, vb, x + 22, y - 1);
        }

        void DrawGraph(Graphics g, Rectangle r)
        {
            using (Brush bg = new SolidBrush(_p.Card))
            using (GraphicsPath p = Draw.Rounded(r, 8))
                g.FillPath(bg, p);

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
            fill[N] = new PointF(r.Right, r.Bottom);
            fill[N + 1] = new PointF(r.Left, r.Bottom);
            using (Brush fb = new SolidBrush(Color.FromArgb(40, col)))
                g.FillPolygon(fb, fill);
            using (Pen pen = new Pen(col, 1.6f)) { pen.LineJoin = LineJoin.Round; g.DrawLines(pen, pts); }
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
        long _bytes;            // total bytes read during active test
        long _prevBytes;
        double _prevT;
        readonly Stopwatch _sw = new Stopwatch();

        const int N = 120;
        readonly double[] _hist = new double[N]; // Mbps samples
        double _curMbps, _peakMbps, _avgMbps;
        string _status = "Idle · showing live usage";

        // A large, reliable download endpoint.
        const string TestUrl = "https://speed.cloudflare.com/__down?bytes=300000000";

        public SpeedTestForm(Palette pal)
        {
            _p = pal;
            Text = "NetSpeedTray – Speed Test";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(440, 320);
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _btn = new Button();
            _btn.Text = "Run download test";
            _btn.FlatStyle = FlatStyle.Flat;
            _btn.Size = new Size(180, 34);
            _btn.Location = new Point(ClientSize.Width - 180 - 18, ClientSize.Height - 34 - 16);
            _btn.Cursor = Cursors.Hand;
            _btn.Click += (s, e) => { if (_running) Stop(); else Start(); };
            Controls.Add(_btn);
            StyleButton();

            _ui = new System.Windows.Forms.Timer();
            _ui.Interval = 200;
            _ui.Tick += (s, e) => UiTick();

            FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Stop(); Hide(); } };
        }

        public void SetPalette(Palette pal) { _p = pal; StyleButton(); Invalidate(); }

        // Fill with illustrative data for documentation screenshots.
        public void SeedDemo()
        {
            Random r = new Random(3);
            double v = 0;
            for (int i = 0; i < N; i++)
            {
                double target = 150 + 55 * Math.Sin(i * 0.12);
                v = v * 0.55 + target * 0.45 + r.Next(-10, 10);
                if (v < 0) v = 0;
                _hist[i] = v;
            }
            _curMbps = _hist[N - 1];
            _peakMbps = 0;
            for (int i = 0; i < N; i++) if (_hist[i] > _peakMbps) _peakMbps = _hist[i];
            _avgMbps = 148.6;
            _status = "Test complete";
            _btn.Text = "Run download test";
        }

        void StyleButton()
        {
            _btn.BackColor = _running ? Color.FromArgb(200, 70, 70) : _p.Up;
            _btn.ForeColor = Color.White;
            _btn.FlatAppearance.BorderSize = 0;
            _btn.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        }

        public void ShowAtCursor()
        {
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Left = Math.Max(wa.Left, wa.Right - Width - 40);
            Top = Math.Max(wa.Top, wa.Bottom - Height - 60);
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            if (!_running) _ui.Start();   // keep live graph animating while open
        }

        // Live usage feed from the tray monitor (used when no test is running).
        public void PushLive(double downBps, double upBps)
        {
            if (_running) return;
            _curMbps = Fmt.Mbps(downBps);
            Buffer(_curMbps);
            if (!_ui.Enabled) Invalidate();
        }

        void Buffer(double v)
        {
            for (int i = 0; i < N - 1; i++) _hist[i] = _hist[i + 1];
            _hist[N - 1] = v;
        }

        void Start()
        {
            if (_running) return;
            _running = true;
            Interlocked.Exchange(ref _bytes, 0);
            _prevBytes = 0; _prevT = 0; _peakMbps = 0; _avgMbps = 0; _curMbps = 0;
            for (int i = 0; i < N; i++) _hist[i] = 0;
            _status = "Connecting…";
            _sw.Restart();
            _btn.Text = "Cancel";
            StyleButton();
            _ui.Start();
            _worker = new Thread(Download);
            _worker.IsBackground = true;
            _worker.Start();
        }

        void Stop()
        {
            _running = false;
            _sw.Stop();
            _btn.Text = "Run download test";
            StyleButton();
        }

        void Download()
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(TestUrl);
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                req.AllowAutoRedirect = true;
                req.UserAgent = "NetSpeedTray/1.0";
                req.KeepAlive = true;
                using (WebResponse resp = req.GetResponse())
                using (Stream st = resp.GetResponseStream())
                {
                    byte[] buf = new byte[65536];
                    int n;
                    while (_running && (n = st.Read(buf, 0, buf.Length)) > 0)
                    {
                        Interlocked.Add(ref _bytes, n);
                        if (_sw.Elapsed.TotalSeconds > 12.0) break; // cap test at ~12s
                    }
                }
                SetStatus(_running ? "Test complete" : "Cancelled");
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message.Split('\n')[0]);
            }
            _running = false;
        }

        void SetStatus(string s)
        {
            try { if (IsHandleCreated) BeginInvoke((Action)(() => { _status = s; })); }
            catch { }
        }

        void UiTick()
        {
            if (_running)
            {
                double t = _sw.Elapsed.TotalSeconds;
                long b = Interlocked.Read(ref _bytes);
                double dt = t - _prevT; if (dt < 0.001) dt = 0.001;
                double inst = (b - _prevBytes) * 8.0 / 1000000.0 / dt; // Mbps
                _prevBytes = b; _prevT = t;
                _curMbps = inst;
                if (inst > _peakMbps) _peakMbps = inst;
                if (t > 0.5) _avgMbps = b * 8.0 / 1000000.0 / t;
                Buffer(inst);
                if (t >= 0.5 && _status == "Connecting…") _status = "Testing…";
                if (!_running) { } // may have finished mid-tick
            }
            else
            {
                // Finished: ensure button reset.
                if (_btn.Text == "Cancel") { _btn.Text = "Run download test"; StyleButton(); }
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(_p.Bg);
            int pad = 18;

            using (Font hf = new Font("Segoe UI", 12f, FontStyle.Bold))
            using (Brush tb = new SolidBrush(_p.Text))
                g.DrawString("Download speed", hf, tb, pad, pad - 2);
            using (Font sf = new Font("Segoe UI", 8.5f))
            using (Brush sb = new SolidBrush(_p.Dim))
                g.DrawString(_status, sf, sb, pad, pad + 20);

            // Big current Mbps.
            using (Font bf = new Font("Segoe UI", 40f, FontStyle.Bold))
            using (Brush bb = new SolidBrush(_p.Down))
                g.DrawString(_curMbps.ToString("0.0", CultureInfo.InvariantCulture), bf, bb, pad - 4, pad + 36);
            using (Font uf = new Font("Segoe UI", 13f, FontStyle.Bold))
            using (Brush ub = new SolidBrush(_p.Dim))
                g.DrawString("Mbps", uf, ub, pad + MeasureBig(g, _curMbps), pad + 70);

            // Peak / Avg stat tiles.
            int tileY = pad + 44;
            Stat(g, Width - pad - 150, tileY, "Peak", _peakMbps);
            Stat(g, Width - pad - 150, tileY + 36, "Average", _avgMbps);

            // Graph.
            Rectangle gr = new Rectangle(pad, pad + 118, Width - pad * 2, 118);
            DrawGraph(g, gr);
        }

        float MeasureBig(Graphics g, double v)
        {
            using (Font bf = new Font("Segoe UI", 40f, FontStyle.Bold))
                return g.MeasureString(v.ToString("0.0", CultureInfo.InvariantCulture), bf).Width - 8;
        }

        void Stat(Graphics g, int x, int y, string label, double mbps)
        {
            using (Font lf = new Font("Segoe UI", 8.5f))
            using (Brush lb = new SolidBrush(_p.Dim))
                g.DrawString(label, lf, lb, x, y);
            using (Font vf = new Font("Segoe UI Semibold", 13f, FontStyle.Bold))
            using (Brush vb = new SolidBrush(_p.Text))
                g.DrawString(mbps.ToString("0.0", CultureInfo.InvariantCulture) + " Mbps", vf, vb, x + 64, y - 3);
        }

        void DrawGraph(Graphics g, Rectangle r)
        {
            using (Brush bg = new SolidBrush(_p.Card))
            using (GraphicsPath p = Draw.Rounded(r, 8))
                g.FillPath(bg, p);

            double max = 1;
            for (int i = 0; i < N; i++) if (_hist[i] > max) max = _hist[i];
            max *= 1.15;

            // Axis labels (0 and max Mbps).
            using (Font af = new Font("Segoe UI", 7.5f))
            using (Brush ab = new SolidBrush(_p.Dim))
            {
                g.DrawString(max.ToString("0", CultureInfo.InvariantCulture), af, ab, r.Left + 4, r.Top + 2);
                g.DrawString("0", af, ab, r.Left + 4, r.Bottom - 14);
            }

            PointF[] pts = new PointF[N];
            float dx = (float)r.Width / (N - 1);
            for (int i = 0; i < N; i++)
            {
                float x = r.Left + dx * i;
                float y = r.Bottom - 4 - (float)(_hist[i] / max) * (r.Height - 8);
                pts[i] = new PointF(x, y);
            }
            PointF[] fill = new PointF[N + 2];
            Array.Copy(pts, fill, N);
            fill[N] = new PointF(r.Right, r.Bottom);
            fill[N + 1] = new PointF(r.Left, r.Bottom);
            using (Brush fb = new SolidBrush(Color.FromArgb(46, _p.Down)))
                g.FillPolygon(fb, fill);
            using (Pen pen = new Pen(_p.Down, 1.8f)) { pen.LineJoin = LineJoin.Round; g.DrawLines(pen, pts); }
        }
    }

    // ---------------------------------------------------------- Screenshot mode
    static class Screenshots
    {
        public static void Run(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { }

            Settings cfg = new Settings();
            cfg.Unit = UnitMode.AutoBytes;

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
                s.Connected = true;
                s.NetworkName = "Home_WiFi_5G";
                s.AdapterName = "Wi-Fi";
                s.IsWireless = true;
                double d = 2600000 + 1700000 * Math.Sin(i * 0.19) + r.Next(-250000, 250000);
                double u = 520000 + 360000 * Math.Sin(i * 0.16 + 1.0) + r.Next(-60000, 60000);
                s.DownBps = d < 0 ? 0 : d;
                s.UpBps = u < 0 ? 0 : u;
                s.SessionDown = 3456000000L;
                s.SessionUp = 612000000L;
                pf.Push(s, cfg);
            }
            return pf;
        }

        static SpeedTestForm BuildSpeed(ThemeMode t)
        {
            SpeedTestForm sf = new SpeedTestForm(Palette.For(t));
            sf.SeedDemo();
            return sf;
        }

        static void Capture(Form f, string path)
        {
            try
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-4000, -4000);
                IntPtr h = f.Handle;        // force handle creation
                f.Show();
                Application.DoEvents();
                Rectangle rc = f.ClientRectangle;
                using (Bitmap bmp = new Bitmap(rc.Width, rc.Height))
                {
                    f.DrawToBitmap(bmp, rc);
                    bmp.Save(path, ImageFormat.Png);
                }
                f.Hide();
            }
            catch { }
            finally { f.Dispose(); }
        }
    }
}
