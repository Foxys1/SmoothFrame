# SmoothFrame ⚡

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-informational.svg)]()
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-purple.svg)]()
[![Release](https://img.shields.io/badge/Release-v2.3.0-8B5CF6.svg)](https://github.com/Foxys1/SmoothFrame/releases/tag/v2.3.0)

🌐 **English** | **[Русский](README_RU.md)**

> **SmoothFrame** is a modern, lightweight, transparent, and 100% reversible game optimization suite for Windows 10 and 11.

Engineered to eliminate micro-stutters, stabilize frame pacing (1% Low FPS), reduce input latency, and optimize Java game heaps (Minecraft NeoForge 1.21.1) without dangerous system tweaks or permanent registry pollution.

---

## 🚀 Key Features

### 🖥️ Obsidian & Laser Monolith Minimalist UI (v2.3)
* **Cyber Aesthetic:** Modern deep-obsidian palette (`#08080C`) paired with electric neon-violet accents (`#8B5CF6`).
* **Bento Grid 3x2 Layout & Quick Control Deck:** Modular telemetry and toggle cards for real-time timer status, active power plans, process priority, P-core isolation, background task silencing, Standby RAM cache purge, Power Throttling QoS, Windowed Flip Model, 1:1 Mouse Input, and Gaming Health (expanded 3x2 grid with monitor refresh rate drop alerts, DXGI VRAM budget inspection, and CPU throttling detection).
* **Subpixel ClearType Preservation:** Decoupled drop-shadow layer architecture isolating effect transforms from visual text tree, preventing WPF text blurs and maintaining razor-sharp ClearType readability.
* **Interactive Close Dialog:** Smart exit prompt allowing instant selection between minimizing to the System Tray (retaining active gameplay optimizations) or completely exiting (triggering full, safe WAL state restoration).
* **Fluid Physics-Based Animations:** Smooth tab transitions, spring-interpolated active navigation pills (`BackEase`), and clean responsive scaling.
* **Staged First-Launch Onboarding:** Orchestrated entrance animations, language indicator badges, and a calibrated 1-second pause before smooth multi-language greeting cycling.
* **Laser Precision Monolith Installer:** Setup wizard featuring interactive micro-scale button physics and physical metric verification chips (`NT_TIMER: 0.50 ms`, `UDP_HUD: 127.0.0.1`, `SAFETY: 100% REVERSIBLE`).
* **Robust Internationalization (i18n):** Complete key coverage across English, Russian, German, Spanish, French, and Chinese, with automated English fallback protection.

### 🎮 Dedicated Minecraft Optimization Suite (NeoForge 1.21.1)
* **Real-Time UDP Loopback Telemetry HUD:** High-frequency (4 Hz) loopback connection (`127.0.0.1:28511`) directly streaming in-game FPS, 1% Low FPS, frametime, and JVM memory heap usage into the desktop app.
* **Built-in SmoothFrame Mod (`SmoothFrameMod-1.21.1.jar`):**
  * **Frustum & Distance Entity Culling:** Dynamically culls invisible entities and off-screen mobs behind the camera, boosting FPS by up to +30–40% in dense bases and farms.
  * **Zero-Stall Chunk Streamer:** Throttles async chunk uploads to the GPU render thread to eliminate stuttering during Elytra flights.
  * **Dynamic FPS & Battery Saver:** Drops background frame rate to 15 FPS when minimized, restoring full refresh rate instantly upon alt-tab.
* **Synchronous RAM Allocation (`Xms=Xmx`):** Interactive memory slider pairing with `AlwaysPreTouch` to prevent dynamic Java heap resizing pauses.
* **Next-Gen JVM GC Profiles:** One-click presets for Generational ZGC (Java 21 / NeoForge 1.21+) with sub-millisecond GC pauses and optimized Aikar G1GC for Java 17.
* **Universal Launcher Auto-Detection:** Automatically discovers profiles and mods directories for CurseForge, Prism Launcher, Modrinth, Modded Launchers, and the standard Minecraft Launcher.
* **Hardware GPU Binding & RAM Purge:** Enforces high-performance discrete GPU binding (NVIDIA/AMD) for `javaw.exe` via Windows Settings registry and provides instantaneous Standby List memory clearing.

### ⚡ System-Level Kernel & OS Optimizations (v2.3)
* **Process Memory Priority Protection (RAM Protection):** Enforces `MEMORY_PRIORITY_NORMAL (5)` for the active game process via native `SetProcessInformation(ProcessMemoryPriority)` to prevent Windows Memory Manager from paging the game working set into the disk pagefile (swap), while setting `MEMORY_PRIORITY_VERY_LOW (1)` for background applications. Fully reversible with guaranteed rollback to default priority upon game exit.
* **Smart Window Focus & Smooth Alt+Tab:** Tracks foreground window transitions via unmanaged `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` in a dedicated STA message pump with zero DLL injection. Unleashes maximum boost when inside the game, automatically relaxes background limitations when the user switches to browser or messenger, and instantly re-applies full boost upon returning to the game.
* **Gaming Health Inspector (100% Read-Only):** Real-time non-invasive hardware inspection: displays current and maximum monitor refresh rate with automatic warning on 60 Hz drops and a 1-click shortcut to `ms-settings:display`; tracks dedicated GPU VRAM budget and current usage via DXGI (`IDXGIAdapter3`) to alert on overflow risk with zero COM leaks; and inspects per-core CPU thermal and power limits via `CallNtPowerInformation` to detect hardware throttling.
* **Process Power Throttling & Quality of Service (QoS):** Enforces `HighQoS` on the active game process via native `SetProcessInformation(ProcessPowerThrottling)` preventing Windows 11 CPU downclocking, while shifting non-critical background apps (Discord, Chrome, Telegram, Spotify) into `EcoQoS` (Efficiency Mode) — 100% in-memory with zero registry modifications.
* **Windowed Game Optimization (DXGI Flip Model):** Activates the Windows 11 presentation model auto-upgrade (`SwapEffectUpgradeCache = 1`), reducing 15–30 ms of Desktop Window Manager (DWM) composition latency in borderless windowed mode and unlocking G-Sync/FreeSync without screen tearing.
* **1:1 Linear Hardware Mouse Input:** Dynamically removes the Windows "Enhance Pointer Precision" acceleration curve (`SPI_SETMOUSE`) during gameplay, guaranteeing authentic 1:1 hardware sensor tracking without permanent registry corruption, and cleanly restores original curve and speed on exit.
* **System Diagnostics & Platform Readiness:** Real-time read-only inspection for VBS (Virtualization-Based Security / Core Isolation) with one-click shortcut to Windows Settings, Hardware-Accelerated GPU Scheduling (HAGS), and Windows Game Mode.
* **High-Precision System Timer:** Dynamically scales Windows scheduler timer resolution down to **0.50 ms** via the native `NtSetTimerResolution` API for lowest input latency and mouse response.
* **Intelligent Power Scheme Switching:** Activates Windows Ultimate Performance plan during gameplay (preventing CPU core parking) and cleanly restores your default plan on exit.
* **Process Priority & Affinity Optimization:** Elevates active game process priority while gently deprioritizing non-critical background services.
* **Zero OS Handle Leaks & Memory Hardening:** Safe unmanaged handle lifetime management (`Process.Dispose()`) and strict Win32 structure marshalling.
* **PID Recycling Protection:** Verifies process creation timestamp (`StartTime`) and executable paths to prevent applying tuning to recycled PIDs.
* **Immutable System Blacklist:** Absolute protection for critical Windows subsystems (`csrss.exe`, `dwm.exe`, `audiodg.exe`, `explorer.exe`, etc.).
* **100% Reversible with Crash Recovery (WAL Snapshot):** Baseline states are atomically written to `active_snapshot.json`. On game close, app exit, or unexpected shutdown, original system states are cleanly restored on next launch.

---

## 🛡️ Security & Architecture

SmoothFrame is built on transparency, safety, and non-invasive system interaction:

| Metric | Status | Details |
| :--- | :---: | :--- |
| **Malware / PUP** | 0% Clean | 100% open-source code. Zero trojans, miners, or adware. |
| **Network Privacy** | 100% Offline | Zero external network calls, zero tracking analytics, zero telemetry sent to servers. In-game HUD strictly communicates over local UDP loopback (`127.0.0.1`). |
| **Anti-Cheat Safe** | 100% Compatible | Zero memory injection, zero DLL hooking into foreign processes. Safe for EAC, BattlEye, Vanguard, and VAC. |
| **System Integrity** | Protected | Does not modify protected Windows system binaries or create persistent unremovable registry changes. |
| **Automatic Rollback** | Guaranteed | Always restores original power scheme, timer resolution, and process states upon exit. |

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

# Build solution in Release configuration
dotnet build SmoothFrame.sln -c Release
```

### Build Minecraft NeoForge Mod (Optional)
```powershell
# Compile Java mod sources into SmoothFrameMod-1.21.1.jar
powershell -ExecutionPolicy Bypass -File ./scripts/build_mod.ps1
```

### Build Release Installer
```powershell
powershell -ExecutionPolicy Bypass -File ./build.ps1 -SkipTests
```
The compiled installer will be generated at `installer/SmoothFrame_Setup.exe`.

---

## 📦 Download & Installation

1. Download the latest installer from the official [GitHub Releases](https://github.com/Foxys1/SmoothFrame/releases/latest) page (`SmoothFrame_Setup.exe`).
2. Run the installer and launch SmoothFrame.
3. Select your game or launch Minecraft with the SmoothFrame mod to activate real-time telemetry and optimization.

To uninstall, use standard Windows **Settings → Installed Apps** or launch `Uninstall.exe` in the application directory.

---

## 📄 License

This project is licensed under the [MIT License](LICENSE).
Feel free to inspect, modify, and contribute to the code.
