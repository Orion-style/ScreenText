# Zenith Utility (ScreenText)

<p align="center">
  <img src="ZenithApp/Resources/app.png" alt="Zenith Logo" width="128" height="128">
</p>

<p align="center">
  <b>Modern, modular, and lightweight productivity utility for Windows 10 & 11</b><br>
  Elevate your daily workflow: instant offline OCR & screen capture, macOS-style Quick Look on Spacebar, Always on Top window pinning, low-latency key remapping, and contextual smart suggestions.
</p>

<p align="center">
  <b>English</b> •
  <a href="README.ru.md">Русский</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D7?logo=windows" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-8.0%20LTS-512BD4?logo=dotnet" alt=".NET 8">
  <img src="https://img.shields.io/badge/UI-WPF%20Fluent%20(Mica)-blue" alt="WPF UI">
  <img src="https://img.shields.io/badge/Release-1.0.0-success" alt="Release">
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License">
</p>

---

## 📌 Table of Contents

- [Overview](#-overview)
- [Key Features](#-key-features)
  - [1. Smart Screenshot & Offline OCR](#1-smart-screenshot--offline-ocr)
  - [2. Quick Look File Preview](#2-quick-look-file-preview)
  - [3. SnapZones / Always on Top](#3-snapzones--always-on-top)
  - [4. SmartLinks Contextual Helper](#4-smartlinks-contextual-helper)
  - [5. Key Remap & Hotkey Automation](#5-key-remap--hotkey-automation)
- [Default Hotkeys Reference](#-default-hotkeys-reference)
- [Download & Installation](#-download--installation)
  - [Windows Setup Installer](#windows-setup-installer)
  - [Portable Edition](#portable-edition)
  - [System Requirements](#system-requirements)
- [Architecture & Performance](#-architecture--performance)
- [Building from Source](#-building-from-source)
- [Configuration & Storage](#-configuration--storage)
- [Creating Custom Modules](#-creating-custom-modules)
- [License & Acknowledgments](#-license--acknowledgments)

---

## 💡 Overview

**Zenith Utility** is a sleek, modular desktop utility for Windows designed to bring macOS-like conveniences and power-user tools into a single, cohesive experience. Running quietly in the system tray, Zenith delivers near-zero idle CPU and RAM footprint while processing everything locally on your machine with no external network requests.

### Core Philosophy

- ⚡ **Zero Inactive Overhead**: Unused modules are completely unloaded from memory along with their system hooks.
- 🎨 **Windows 11 Native Aesthetics**: Fluent Design with Mica and Acrylic backdrops, and automatic dark/light theme switching.
- 🔒 **100% Privacy & Offline**: Built on local Windows OCR APIs — zero cloud transmissions, zero telemetry.
- 🛡️ **Robust Engineering**: Single-instance mutex with named pipe activation, atomic settings writes, and failsafe keyboard hooks.

---

## ✨ Key Features

### 1. Smart Screenshot & Offline OCR

Capture any screen region instantly and recognize text or scan QR codes in real time.

- **Default Shortcuts**:
  - `Ctrl + Shift + S` — Capture selected area directly to clipboard.
  - `Ctrl + Shift + T` — Capture area and perform instant optical character recognition (OCR).
- **Native Windows OCR**: High-accuracy offline recognition engine supporting English, Russian, and any language pack installed on your Windows system.
- **Layout Reconstruction**: Automatically preserves paragraphs, column structure, and indentation.
- **QR & Barcode Scanner**: Detects and decodes QR codes and barcodes inside captured regions.
- **Interactive Toasts**: Clean Windows notifications with one-click actions to copy text or open detected URLs.
- **Optional Auto-Save**: Automatically save captures to a folder of your choice in PNG format.

### 2. Quick Look File Preview

Enjoy fast, macOS-style file previews in Windows Explorer and Desktop with zero loading delay.

- **How to Use**: Select any file in Windows Explorer or Desktop and press <kbd>Space</kbd>. Press <kbd>Space</kbd> or <kbd>Esc</kbd> again to dismiss.
- **Supported Formats**:
  - **Images**: `.png`, `.jpg`, `.jpeg`, `.webp`, `.bmp`, `.gif`, `.ico`, `.tiff`.
  - **Text & Source Code**: `.txt`, `.cs`, `.js`, `.ts`, `.json`, `.xml`, `.html`, `.css`, `.py`, `.md`, `.yaml`, `.log`, `.sql`, etc. with syntax highlighting and line numbers.
  - **PDF Documents**: Fast native multi-page PDF rendering without heavy third-party readers.
  - **Media**: Audio and video (`.mp4`, `.mkv`, `.avi`, `.mp3`, `.wav`) with an integrated player.
- **Adaptive Centering**: Previews center smoothly on screen scaled proportionally to your active display.

### 3. SnapZones / Always on Top

Keep any active window floating on top of all others with a customizable visual accent border.

- **Default Shortcut**: `Win + Ctrl + T`.
- **Dynamic Accent Border**: A colored border securely tracks and wraps around the pinned window as you move or resize it.
- **Auditory Feedback**: Subtle sound cues when toggling pin status.
- **Exclusion Safeguard**: Automatically protects system windows (`explorer.exe`, `SearchHost`, Taskbar) from accidental pinning.

### 4. SmartLinks Contextual Helper

Floating helper analyzing recognized text or clipboard content for actionable information.

- **Entity Recognition**: Automatically detects URLs, IP addresses, emails, color codes (HEX, RGB, HSL), and QR codes.
- **Color Preview & Conversion**: Displays a live swatch chip for color codes and converts between formats.
- **QR Generator**: Generates clean QR codes for any selected text or URL to scan on mobile devices.

### 5. Key Remap & Hotkey Automation

Low-level keyboard hook (`WH_KEYBOARD_LL`) providing system-wide key remapping and hotkey workflows.

- **Flexible Rules**: Remap keys, map key combinations to macros, and handle non-standard layouts.
- **Emergency Escape (`Ctrl + Shift + Esc` held for 1.5s)**: Instantly halts active remapping in the event of accidental key locks or recursion.

---

## ⌨️ Default Hotkeys Reference

| Action | Shortcut | Customizable | Description |
| :--- | :---: | :---: | :--- |
| **Capture Area** | <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>S</kbd> | Yes | Takes screenshot to clipboard |
| **Capture + OCR** | <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>T</kbd> | Yes | Recognizes text & QR codes to clipboard |
| **Quick Look** | <kbd>Space</kbd> | Yes | Previews selected file in Windows Explorer |
| **Pin Window (Top)** | <kbd>Win</kbd> + <kbd>Ctrl</kbd> + <kbd>T</kbd> | Yes | Pins window with visual accent border |
| **Emergency Halt** | <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Esc</kbd> | Yes | Hold 1.5s to suspend key remapping |
| **Settings Window** | Double-click tray icon | — | Opens the configuration UI |

*All shortcuts can be modified in the Zenith Settings panel.*

---

## 📦 Download & Installation

Pre-built binaries are available in [GitHub Releases](https://github.com/Orion-style/ScreenText/releases) and in the local repository [`Release/`](./Release/) folder.

### Windows Setup Installer

The official Windows installer `ZenithSetup-1.0.0-x64.exe` is powered by Inno Setup:

- 🌐 **Multilingual Wizard**: Complete English and Russian installer UI.
- 👤 **Non-Admin Installation**: Installs cleanly per-user into `%LocalAppData%\Programs\Zenith Utility` without UAC prompts, or machine-wide into `Program Files` when elevated.
- 🖥️ **Shortcuts & Autostart**: Optional Desktop icon, Start Menu shortcut, and automatic Windows startup.
- 🔄 **Smart Process Closure**: Detects running instances and offers to shut them down cleanly before upgrade or uninstall.
- 🧹 **Clean Removal**: Integrates with Windows "Installed Apps" / "Programs & Features" for spotless uninstallation.

#### Silent Installation Flags (for System Administrators)

```powershell
# Unattended silent installation with Desktop icon and Windows startup enabled
.\ZenithSetup-1.0.0-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /TASKS="desktopicon,autostart"
```

### Portable Edition

A self-contained single-file executable `ZenithApp.exe` is also available for portable use. No installation or registry changes are required: simply run it from any folder or a flash drive.

### System Requirements

- **Operating System**: Windows 10 (version 2004 / build 19041 or higher) or Windows 11.
- **Architecture**: 64-bit (x64 / AMD64).
- **Runtime**: Self-contained (no external .NET installation required).

---

## 🏗️ Architecture & Performance

Zenith is organized with clean separation of concerns and modular lifecycle management:

```
ScreenText\
├── ZenithApp\                  # Main WPF Application
│   ├── Core\                   # System-level infrastructure
│   │   ├── Hotkeys\            # Low-latency Win32 message-only hotkey engine
│   │   ├── Lifecycle\          # SingleInstanceService (Named Mutex + IPC Pipe)
│   │   ├── Logging\            # Lightweight file logger with date rotation
│   │   ├── Modules\            # BaseFeatureModule, ModuleManager
│   │   ├── Settings\           # Atomic JSON configuration store
│   │   └── Startup\            # Windows HKCU Run registry service
│   ├── Modules\                # Standalone feature modules
│   │   ├── Screenshot\         # Screen capture, Windows OCR, QR engine
│   │   ├── QuickLook\          # Spacebar file preview & Shell integration
│   │   ├── SnapZones\          # Window pin engine with accent overlay
│   │   ├── SmartLinks\         # Contextual link and color parser
│   │   └── KeyRemap\           # Low-level keyboard hook remapper
│   ├── Resources\              # Vector icons, dictionaries & assets
│   └── UI\                     # Views & ViewModels (WPF UI, Fluent Mica)
│
├── ZenithApp.Tests\            # Unit and lifecycle tests (xUnit)
├── installer\                  # Inno Setup configuration scripts
├── scripts\                    # Automated build and release scripts
└── Release\                    # Packaged installers and checksums
```

### Zero-Cost Inactive Modules
Each feature module extends `BaseFeatureModule`. When disabled in Settings:
1. Global keyboard and mouse hooks are unregistered.
2. Overlay windows and cached visual resources are destroyed.
3. Garbage collection cleans up all unmanaged handles.

---

## 🛠️ Building from Source

### Prerequisites

1. [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. [Inno Setup 6](https://jrsoftware.org/isdl.php) (for compiling the installer)
3. Git

### Build Instructions

```powershell
# 1. Clone the repository
git clone https://github.com/Orion-style/ScreenText.git
cd ScreenText

# 2. Build solution and execute tests
dotnet build ZenithApp.sln -c Release
dotnet test ZenithApp.sln -c Release

# 3. Publish self-contained executable
dotnet publish ZenithApp\ZenithApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o build_output

# 4. Compile the Windows Installer
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

The resulting setup wizard will be created in `Release\ZenithSetup-1.0.0-x64.exe`.

---

## ⚙️ Configuration & Storage

Configuration files are stored in standard Windows application folders:

- **Settings File**: `%AppData%\ZenithApp\settings.json`
- **Application Logs**: `%AppData%\ZenithApp\Logs\app-YYYYMMDD.log`

Writes are performed atomically using safe temporary swap files to safeguard against power interruptions or unexpected process terminations.

---

## 🧩 Creating Custom Modules

Zenith makes adding new capabilities simple:

1. Create a class inheriting from `BaseFeatureModule`:
   ```csharp
   public sealed class MyFeatureModule : BaseFeatureModule
   {
       public override string Id => "my_feature";
       public override string Name => "My Feature Module";
       public override string Description => "Brief description of the feature.";

       public MyFeatureModule(IAppLogger logger) : base(logger) { }

       protected override Task OnEnableAsync()
       {
           Logger.LogInfo("Module activated!");
           return Task.CompletedTask;
       }

       protected override Task OnDisableAsync()
       {
           Logger.LogInfo("Module deactivated!");
           return Task.CompletedTask;
       }
   }
   ```
2. Register the module in `ConfigureServices` within `App.xaml.cs`:
   ```csharp
   services.AddSingleton<IFeatureModule, MyFeatureModule>();
   ```
3. The new module will instantly appear in the Settings window with full persistence support.

---

## 📄 License & Acknowledgments

This project is licensed under the [MIT License](LICENSE).

### Third-Party Libraries
- [WPF-UI](https://github.com/lepoco/wpfui) — Modern Fluent Design UI controls for WPF.
- [ZXing.Net](https://github.com/micjahn/ZXing.Net) — Multi-format 1D/2D barcode and QR image processing library.
- [Microsoft.Windows.SDK.NET](https://github.com/microsoft/CsWinRT) — Windows Runtime APIs for OCR and display integration.
