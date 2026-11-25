<p align="center">
  <img src="assets/logo.png" alt="Kintsugi logo: a cracked shield mended with a cyan weld" width="160">
</p>

<h1 align="center">Kintsugi</h1>

<p align="center">
  Memory Integrity &amp; Kernel Stack Protection repair tool for Windows 11.<br>
  Restores control of greyed-out or inaccessible Memory Integrity and Kernel-mode Hardware-enforced Stack Protection settings.
</p>

<p align="center">
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2011-0078D4">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10-512BD4">
  <img alt="Version" src="https://img.shields.io/badge/version-1.0.0-6CC04A">
</p>

> [!WARNING]
> This application changes system-wide Windows security configuration. An incorrect or unsupported configuration can cause instability, a blue screen, or a boot loop. Create a restore point or system backup first, run the driver scan, and do not force-enable a feature on unsupported hardware unless you understand the risk.

## Overview

Kintsugi (formerly *Windows Security Patcher*) is named after the Japanese art of repairing broken pottery with gold. It provides direct controls for security settings that can become unavailable through the standard Windows Security interface. It can:

- Enable or disable **Memory Integrity (HVCI)**.
- Enable or disable **Kernel-mode Hardware-enforced Stack Protection (KSHSP)**.
- Show each feature's live status, i.e. whether it is configured, actually running, or waiting for a restart.
- Check hardware support: virtualization for Memory Integrity, hardware shadow stacks for KSHSP.
- Scan installed kernel drivers and the driver store for drivers commonly reported as incompatible.
- Create a System Restore point before applying a change, and restart Windows when you are ready.
- Report Device Guard policy values that can grey out the Windows Security toggles.
- Keep an in-app activity log you can copy for bug reports.

This is an unofficial third-party utility and is not affiliated with or endorsed by Microsoft.

## Requirements

- Windows 11 22H2 or newer (build `22621` or later).
- Windows Security app version `1000.25330.0.9000` or newer.
- Nothing else to install: `Kintsugi.exe` is a single self-contained file that includes the .NET 10 runtime.
- An administrator account and elevated execution.
- Compatible CPU, firmware settings, and drivers for the feature being enabled.
- A restart after changing either security feature.

The KSHSP hardware check asks Windows whether the processor supports hardware shadow stacks (Intel CET, found on 11th-gen Core and newer, or AMD Zen 3 and newer). It is a safeguard, not a guarantee of compatibility.

## Usage

1. Right-click `Kintsugi.exe` and select **Run as administrator** (Windows asks for elevation automatically).
2. Read and accept the authorization notice.
3. Check each feature's status pill (On, Off, Restart required, Off after restart) and the **System** panel for hardware support.
4. Select **Scan for driver conflicts** and review any flagged drivers.
5. Flip the switch for the feature you want to change.
6. Confirm the change. Kintsugi offers to create a System Restore point first; this is recommended.
7. Restart Windows when prompted, or later, to apply the new configuration.

### Recommended enablement order

KSHSP depends on Memory Integrity. When enabling both features:

1. Enable Memory Integrity.
2. Restart Windows.
3. Reopen Kintsugi as administrator.
4. Enable Kernel Stack Protection.
5. Restart Windows again.

## What the app changes

Kintsugi sets the same `Enabled` DWORD values the Windows Security toggles use:

```text
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\KernelShadowStacks
```

Enabling a feature writes `1`; disabling it writes `0`. Changes do not take effect until Windows restarts. Disabling Memory Integrity also disables KSHSP, which depends on it (you are asked first).

Windows Security Patcher, the predecessor of Kintsugi, also wrote `HvcicpKernelModeHardwareEnforcedStackProtection` under `HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard`. That is not a documented Windows policy, and values under the Policies key can make Windows Security show a setting as managed by your organization, so Kintsugi does not write it, and removes it the next time you change KSHSP.

## Driver conflict scan

The scan checks installed kernel drivers (and whether they are running) and third-party packages in the Windows driver store, matching file names against drivers commonly reported as incompatible with Memory Integrity, including:

- Western Digital (`wdcsam`), VirtualBox (`vbox`), Samsung USB (`ssudbus`, `ssudmdm`), Logitech Gaming Software (`lgcore`)
- ENE RGB control (`ene`, `enetechio`), GIGABYTE utilities (`gio`, `gdrv`), `ngmsvs`
- WinRing0, MSI Afterburner / RivaTuner (`RTCore`), ASUS AsIO, Intel network diagnostics (`iqvw64e`), Dell BIOS utility (`dbutil_2_3`), miHoYo anti-cheat (`mhyprot`), AMI flash (`amifldrv`)

The scan is heuristic: it may report false positives (newer versions of some of these drivers are compatible), and it cannot find every incompatible driver.

## Build from source

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows.
- Optional: Visual Studio 2022 (17.14 or newer) or Visual Studio 2026 with the **.NET desktop development** workload.

### Build and publish

```powershell
git clone https://github.com/AC-Storm-YT/kintsugi.git
cd kintsugi
dotnet publish src/Kintsugi/Kintsugi.csproj -c Release -o publish
```

This produces one self-contained file, `publish\Kintsugi.exe` (about 60 MB), which runs on any 64-bit Windows 11 PC without installing .NET. For day-to-day development, open `Kintsugi.sln` in Visual Studio or run `dotnet build`.

Debug builds can render the main window to an image without elevation, which is handy for checking layout changes:

```powershell
$env:__COMPAT_LAYER = "RunAsInvoker"
.\src\Kintsugi\bin\Debug\net10.0-windows\win-x64\Kintsugi.exe --render window.png
```

### Continuous integration

Every push to `main` and every pull request is built by GitHub Actions (`.github/workflows/build.yml`), and the built `Kintsugi.exe` is available as a workflow artifact. Pushing a version tag (for example `v1.0.0`) publishes a GitHub release with the exe attached.

## Dependencies

Kintsugi uses WPF from the .NET 10 Windows Desktop runtime and no third-party libraries. The only package reference is Microsoft's [System.Management](https://www.nuget.org/packages/System.Management), used to read live Device Guard status through WMI. Publishing bundles it, together with the .NET runtime, into `Kintsugi.exe`.

## Project layout

```text
Kintsugi.sln
assets/
├── logo.png                     # README image
├── logo.svg                     # Logo source (vector)
└── logo-small.svg               # Simplified logo for 16–32 px icons
src/Kintsugi/
├── App.xaml(.cs)                # Startup: single instance, elevation and version checks, agreement
├── Views/
│   ├── MainWindow.xaml(.cs)     # Main window: feature switches, system panel, tools, activity log
│   └── Dialog.xaml(.cs)         # Themed message and confirmation dialogs
├── Styles/
│   └── Theme.xaml               # Colors, cards, status pills, toggle switch, buttons
├── Services/
│   ├── SystemStatus.cs          # Configured vs. running state (registry + Device Guard WMI)
│   ├── FeatureController.cs     # Registry writes
│   ├── HardwareSupport.cs       # Shadow stack support, CPU name
│   ├── DriverScanner.cs         # Driver conflict scan
│   ├── RestorePoint.cs          # System Restore point creation
│   ├── WindowsEnvironment.cs    # Elevation, build and Windows Security app checks
│   ├── AppSettings.cs           # Per-user settings (HKCU\Software\Kintsugi)
│   └── SecurityFeature.cs       # Feature definitions
├── Resources/
│   ├── app.ico                  # Application icon
│   └── logo.png                 # Header logo
├── app.manifest                 # Requires administrator, per-monitor DPI awareness
└── Kintsugi.csproj              # .NET 10 WPF, self-contained single-file publish
```

## Disclaimer

Use this software at your own risk. Security features exist to protect the operating system, and disabling or overriding them can reduce protection. The authors and contributors are not responsible for data loss, system instability, boot failure, reduced security, or other damage resulting from use of this project.
