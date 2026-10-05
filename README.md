# NetSpeedTray

A tiny (~45 KB) Windows **system-tray internet speed monitor**. It draws your live
download/upload speed right on the tray icon — like the Wi-Fi icon — and opens an
elegant popup with your network name, a live graph, and an Mbps speed test.

Standalone `.exe`. **No installer, no runtime download** — it runs on Windows 10 & 11
out of the box (uses the .NET Framework already built into Windows).

<p align="center">
  <img src="screenshots/popup-dark.png" width="320" alt="Popup (dark)">
  &nbsp;&nbsp;
  <img src="screenshots/popup-light.png" width="320" alt="Popup (light)">
</p>

## Features

| | |
|---|---|
| 📊 **Live speed on the tray icon** | Download (green) over upload (blue), updated every second — visible at a glance like the Wi-Fi icon. Two-line or download-only mode. |
| 🖱️ **Elegant popup** | Left-click for big readouts, the **network name**, live graph, **ping/latency**, data used on this network, and **today / this-month totals**. |
| 📶 **Wi-Fi *and* Ethernet** | Follows whichever adapter carries your internet — **Wi-Fi glyph + SSID** on wireless, **Ethernet glyph + adapter name** when wired. |
| 📈 **Daily & monthly usage** | Tracks data per day/month and keeps the history on disk — plus per-connection usage that resets when you disconnect. |
| 🚨 **Data-cap alert** | Set a monthly limit; get a tray notification at 80% and 100%. |
| 📡 **Ping / latency** | Live ping to a host you choose (default `1.1.1.1`), colour-coded. |
| ⚡ **Mbps speed test** | Built-in download test with live Mbps, **peak** and **average**. Optional **scheduled** auto-tests logged to CSV. |
| 🖥️ **Floating desktop widget** | Optional always-on-top mini readout you can drag anywhere. |
| 🔔 **Connect / disconnect toasts** | Notifies when your network drops or reconnects. |
| 🌗 **Auto / Dark / Light theme** | Follows Windows automatically, or pick one. |
| ⚙️ **Settings window** | All options in one place; `netsh`-free config stored in `%APPDATA%`. |
| ⬆️ **Update check** | Checks GitHub Releases on launch and notifies when a newer build exists. |
| 🚀 **Run at startup** · 🪶 **Lightweight** | Optional autostart; ~64 KB exe, a few MB RAM, negligible CPU. |

<p align="center">
  <img src="screenshots/speedtest-dark.png" width="440" alt="Speed test (dark)">
</p>

## Download & run

1. Grab **`NetSpeedTray.exe`** (from this repo or the Releases page).
2. Double-click it. The icon appears in the system tray — click the `^` overflow
   arrow if it's hidden, and drag it onto the taskbar to keep it visible.
3. **Left-click** the icon → popup. **Right-click** → menu (units, theme, speed test,
   run at startup, exit).

> The exe is unsigned, so SmartScreen may warn on first launch
> (**More info → Run anyway**). This is normal for self-built apps.

## Build from source

No SDK needed — it compiles with the C# compiler already included in Windows:

```bat
build.bat
```

That runs the .NET Framework `csc.exe` to produce `NetSpeedTray.exe`. Edit
`Program.cs` and re-run to rebuild.

## How it works

- Reads cumulative byte counters from the active network interface
  (`NetworkInterface.GetIPv4Statistics`) once per second and computes the delta.
- Gets the Wi-Fi SSID via `netsh wlan show interfaces`; uses the adapter name for
  Ethernet.
- Renders the tray icon on the fly with GDI+ (two auto-fitted lines, colour-coded).
- Settings are saved to `%APPDATA%\NetSpeedTray\config.ini`.

## License

MIT — do whatever you like.

---

<p align="center"><sub>Made by <b>Devoryn Labs</b></sub></p>
