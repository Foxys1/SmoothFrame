# SmoothFrame ⚡

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-informational.svg)]()
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-purple.svg)]()
[![Tests](https://img.shields.io/badge/Tests-164%20Passing-brightgreen.svg)]()

🌐 **English** | **[Русский](README_RU.md)**

> **SmoothFrame** is a lightweight, transparent, safe, and 100% reversible game optimization utility for Windows 10 and 11.

Engineered to eliminate micro-stutters, stabilize frame pacing, and minimize input latency without dangerous registry hacks or system file modifications.

---

## 🚀 Key Features

* **High-Precision System Timer:** Dynamically scales Windows multimedia timer resolution down to **0.5 ms** via the native `NtSetTimerResolution` API for lowest possible input latency.
* **Intelligent Power Scheme Switching:** Automatically switches to the Windows High Performance power plan during active gameplay sessions and cleanly restores your previous plan on exit.
* **Process Priority Optimization:** Elevates foreground game process priority while safely reducing priority of non-critical background clutter.
* **PID Recycling Protection:** Verifies process start time (`StartTime`) and executable path before applying modifications, strictly preventing accidental priority changes to recycled process IDs.
* **Immutable System Blacklist:** Hardcoded protection for essential Windows services and processes (`csrss.exe`, `dwm.exe`, `audiodg.exe`, `explorer.exe`, etc.).
* **100% Reversible with Crash Recovery (WAL Snapshot):** Baseline system states are atomically saved to `active_snapshot.json`. On game close, application exit, or even an unexpected power cut, original system settings are automatically restored on startup.
* **100% Offline & Private:** Zero network sockets, zero telemetry, zero analytics, and zero remote data transmission.
* **Modern Dark WPF UI:** Fluid vector-based interface with dynamic localization supporting 14 languages (English, Russian, German, French, Spanish, Japanese, Chinese, etc.).

---

## 🛡️ Security & Architecture

SmoothFrame has been subjected to comprehensive independent security auditing and multi-threaded stress verification:

| Metric | Status | Details |
| :--- | :---: | :--- |
| **Malware / PUP** | 0% | Open-source code. Zero trojans, keyloggers, stealers, or cryptominers. |
| **Network Activity** | None | Fully offline. Contains no network sockets, HTTP clients, or telemetry endpoints. |
| **System Integrity** | Guaranteed | No OS file modifications, no dangerous persistent registry tweaks. |
| **Rollback Guarantee** | Automatic | Restores original system timer, power plan, and process priorities upon exit. |
| **Automated Tests** | 164 / 164 | Full unit, integration, and empirical crash-resilience test coverage. |

### Why Are Administrator Privileges Required?
Administrator privileges are strictly required for two legitimate Windows API operations:
1. `NtSetTimerResolution` (multimedia timer resolution configuration).
2. `PowerSetActiveScheme` (active Windows power scheme switching).

---

## 🛠️ Building from Source

### Prerequisites
* Windows 10 / 11 (x64)
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later
* PowerShell 5.1+

### Clone & Build
```bash
# Clone the repository
git clone https://github.com/Foxys1/SmoothFrame.git
cd SmoothFrame

# Build solution
dotnet build SmoothFrame.sln -c Release
```

### Run Test Suites
```bash
# Core safety and process blacklist verification (46 tests)
dotnet run --project tests/SmoothFrame.Runner -c Release

# Empirical stress testing & crash resilience challenge suite (118 tests)
dotnet run --project tests/SmoothFrame.Challenge -c Release
```

### Build Standalone Installer
To produce the clean, compressed single-file installer, run:
```powershell
powershell -ExecutionPolicy Bypass -File ./build.ps1
```
The compiled installer will be generated at `installer/SmoothFrame_Setup.exe`.

---

## 📦 Installation

1. Download the latest `SmoothFrame_Setup.exe` from the [Releases](https://github.com/Foxys1/SmoothFrame/releases) page.
2. Launch the setup wizard and follow the on-screen instructions.
3. SmoothFrame is ready to optimize your games immediately.

To uninstall, use standard Windows **Settings → Installed Apps** or launch `Uninstall.exe` in the program folder.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
You are free to use, inspect, modify, and distribute the code with attribution.
