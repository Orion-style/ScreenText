# Zenith Utility v1.0.0 — Release Notes

**Release Date:** November 2026  
**Target Platform:** Windows 10 (19041+) / Windows 11 (x64)

---

## 🌟 Highlights

Initial release of **Zenith Utility** (ScreenText) — the modern, lightweight productivity powerhouse for Windows.

### 📦 Included Files
- `ZenithSetup-1.0.0-x64.exe` — Self-contained Windows installer (Inno Setup) with Russian and English support.
- `SHA256SUMS.txt` — SHA256 integrity checksums for distribution files.

### ⚡ Included Features
1. **Screen Capture & OCR (`Ctrl + Shift + S` / `Ctrl + Shift + T`)**:
   - High-accuracy offline Windows Optical Character Recognition.
   - Text layout reconstruction (preserves paragraphs and table alignments).
   - Built-in QR Code and barcode detector.
   - Actionable toast notifications (Copy text, Open URLs).
2. **Quick Look Preview (`Space`)**:
   - macOS-style instant file previews in Windows Explorer and Desktop.
   - Support for Images (PNG, JPG, WebP, GIF, ICO, TIFF), Code & Plaintext with syntax highlights, Native PDF rendering, and Media files.
3. **Always on Top Window Pinning (`Win + Ctrl + T`)**:
   - Keep any window floating on top of all others.
   - Dynamic accent border that smoothly follows window resizing and movement.
   - Auditory feedback and system process exclusion safeguards.
4. **SmartLinks Contextual Parser**:
   - Detects URLs, IP addresses, emails, and color values (HEX, RGB, HSL) with live preview chips.
   - Instant QR code generation for any captured snippet.
5. **Key Remapper**:
   - Low-latency global keyboard hooks (`WH_KEYBOARD_LL`).
   - Custom mapping rules with an Emergency Escape safeguard (`Ctrl + Shift + Esc` held for 1.5s).

### 🛠️ Installer Features
- Standalone self-contained deployment (no .NET 8 desktop runtime installation required).
- Per-user (`%LocalAppData%\Programs\Zenith Utility`) or all-users (`C:\Program Files\Zenith Utility`) installation mode.
- Desktop and Start Menu shortcut creation.
- Optional automatic startup with Windows.
- Clean uninstallation via Windows Settings / Control Panel.
