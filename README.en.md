<div align="center">

<img src="src/CleanOptimize.App/Assets/logo.png" alt="CleanOptimize logo" width="80" />

# CleanOptimize for Windows

A Windows cleanup and system management tool with scan previews, risk explanations, quarantine recovery, and disk space analysis.

[![Release](https://img.shields.io/github/v/release/HZ-PRE/cleanwin?label=Release)](https://github.com/HZ-PRE/cleanwin/releases/latest)
[![Build](https://github.com/HZ-PRE/cleanwin/actions/workflows/verify.yml/badge.svg)](https://github.com/HZ-PRE/cleanwin/actions/workflows/verify.yml)
[![License](https://img.shields.io/badge/License-BSL--1.0-blue)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011%20x64-0078D4)](#download-and-use)
[![.NET](https://img.shields.io/badge/.NET-8-512BD4)](#run-from-source)

[Download the latest release](https://github.com/HZ-PRE/cleanwin/releases/latest) · [Features](#features) · [Build and test](#build-and-test) · [Report an issue](https://github.com/HZ-PRE/cleanwin/issues)

[简体中文](README.md) | English | [繁體中文](README.zh-TW.md) | [日本語](README.ja.md)

</div>

## Download and use

Supports **Windows 10 / Windows 11, x64**. Both the installer and portable package include the .NET runtime; no separate .NET installation is needed to use the app.

Choose a stable version on the [Releases](https://github.com/HZ-PRE/cleanwin/releases) page and download its assets:

| File | Purpose |
|---|---|
| `CleanOptimizeSetup-<version>.exe` | Full installer with a setup wizard and uninstaller |
| `CleanOptimize-<version>-win-x64.zip` | Portable package: extract all files, then run `CleanOptimize.exe` |
| `SHA256SUMS.txt` | SHA256 checksums for the installer and portable package |

Start with a scan, review the items, risks, and effects, then confirm cleanup. Add files or folders you want to keep to the whitelist. If cleanup fails, open “View cleanup issues” for the path, reason, and suggested action.

## Features

| Module | Main features |
|---|---|
| Home | Health score, scan summary, CPU and memory monitoring, disk capacity, drive selection, quick cleanup, and recent activity |
| Smart scan | Scan multiple cleanup categories, group results by risk, preview details, select items, and review cleanup results |
| Clean space | System temporary files, application and browser caches, privacy traces, Recycle Bin, and custom cleanup folders |
| Space analysis | Folder usage, large file discovery, size and type filters, open file locations, and recycle actions; scrollable details for long paths |
| Startup items | View and manage registry entries, startup folders, and logon scheduled tasks; show boot time when available |
| App management | List installed apps and icons, launch their uninstallers, and detect identifiable leftovers |
| Toolbox | Quarantine management, duplicate file detection, empty folder cleanup, file shredding, cleanup history, system maintenance status, and Windows tools |
| Settings and tray | Light, dark, and system themes; eight languages; saved window placement; automatic cleanup; result notifications; and updates |

**Quick cleanup** is available from Home, the tray, and the command palette. It scans only low-risk system, app, and browser caches selected by default, then asks for confirmation before cleaning. **Automatic cleanup** is off by default. When enabled, it runs on the configured schedule while the app is running; result notifications have a separate switch.

Shortcuts: `Ctrl+K` opens the command palette, `Ctrl+1` through `Ctrl+8` switch pages, and `Esc` cancels scans that support cancellation. Home stops sampling system metrics when hidden, and large file lists use UI virtualization.

## Cleanup safeguards and local data

- **Scope and whitelist checks**: Cleanup rules are constrained by allowed directories and protected system and user folders. Duplicate recycling, empty folder cleanup, and shredding validate explicitly selected targets separately; they do not broaden automatic cleanup access.
- **Risk explanations**: Low-risk items may be selected by default. Items requiring confirmation, such as privacy traces, are not selected automatically. Review their descriptions and effects before proceeding.
- **Quarantine recovery**: With quarantine enabled, cleanup items that support recovery are moved there and kept for 30 days by default. Quarantined bytes and freed disk space are counted separately: moving files to quarantine does not free disk space.
- **Permanent operations**: Shredding files, emptying the Recycle Bin, and permanently deleting quarantined items cannot be undone through this app. Recovery for other cleanup operations depends on the rule and quarantine settings.
- **Path boundaries**: Cleanup rejects UNC paths, network drives, and device paths, and prevents directory links from crossing cleanup boundaries.
- **Failure details**: Explanations distinguish files in use, insufficient permissions, long paths, and other failures, with suggested actions. Uncleaned items take priority in retained details; up to 1,000 are shown alongside the total uncleaned count.

Settings, history, and quarantine data are stored in `%LOCALAPPDATA%\CleanOptimize`. Uninstalling the app preserves this data so quarantined files can be reviewed and restored after reinstalling.

Anonymous statistics and crash reporting options remain present, but uploads are disabled in the current version. Usage records, file names, paths, and file contents are not uploaded. The updater requests public release information from GitHub and downloads update files after confirmation; local scanning and cleanup work offline.

## Checking for updates

The app checks for public stable releases at startup. When a newer version is available, a red **NEW** badge appears beside the top logo, and an update badge appears beside “Check for updates.” Click a badge to open the update dialog. Choosing “Confirm update” downloads the installer, verifies its SHA256 checksum, and launches it.

- Background checks are cached for five minutes. A manual check sends a fresh request; concurrent requests are combined.
- Downloads can be canceled. If downloading, verification, or installer launch fails, the current app stays open.
- Finish active scans, cleanup, or app uninstallation before updating.
- Drafts, prereleases, and Actions build artifacts are excluded from stable updates. Equal or lower versions do not trigger an update notification.

**The build succeeded, but no update appears?** Confirm that the version is publicly published on [Releases](https://github.com/HZ-PRE/cleanwin/releases), includes fully uploaded `CleanOptimizeSetup-<version>.exe` and `SHA256SUMS.txt` assets, and is newer than the installed version. Drafts left by the old workflow require **Publish release** before clients can discover them.

The update source is the public stable Releases of `HZ-PRE/cleanwin`. SHA256 verifies file integrity. The default release workflow does not enable automatic code signing, and SignPath is not integrated.

## Build and test

### Run from source

Development requires Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). Run these commands in PowerShell:

```powershell
git clone https://github.com/HZ-PRE/cleanwin.git
cd cleanwin
dotnet build ./src/CleanOptimize.App/CleanOptimize.App.csproj -c Release
./src/CleanOptimize.App/bin/Release/net8.0-windows/CleanOptimize.exe
```

The project uses WPF and Windows Forms. Application code has no third-party NuGet package dependencies.

### Automated checks

```powershell
# Build, network entry-point checks, language resource checks, and core regression tests
./scripts/verify.ps1 -Configuration Debug
./scripts/verify.ps1 -Configuration Release

# Full app self-test: core, WPF UI, and window lifecycle checks
Start-Process ./src/CleanOptimize.App/bin/Release/net8.0-windows/CleanOptimize.exe `
    -ArgumentList '--selftest' -Wait
```

Tests use isolated temporary data directories and cover deletion safeguards, whitelists, quarantine recovery, locked files, duplicate recycling, cancellation and concurrency, update integrity, and multilingual/theme layouts. Real-machine rule scans do not delete system junk; file-operation tests use fixtures. Automated checks do not replace interactive testing on every device.

### Local packaging

Creating an installer also requires [Inno Setup](https://jrsoftware.org/isinfo.php). The publishing script reads the project version, creates self-contained installer and portable packages plus checksums, and runs the published app’s self-tests and ZIP integrity checks.

```powershell
./scripts/publish.ps1 -RequireInstaller

# Specify actual paths when tools are not on PATH or in their default installation locations
./scripts/publish.ps1 -DotnetPath C:/tools/dotnet/dotnet.exe `
    -IsccPath C:/tools/Inno/ISCC.exe -RequireInstaller
```

Outputs are written to `dist/`. The script also creates a framework-dependent directory for development and debugging. Distributed installers and ZIP packages both include the runtime.

To remove build caches, run Debug and Release verification, and package from scratch:

```powershell
./scripts/full-test.ps1 -DotnetPath dotnet -IsccPath C:/tools/Inno/ISCC.exe
```

To validate the installer and portable package:

```powershell
./scripts/test-packages.ps1
```

This script extracts the portable package, installs into a new test directory, verifies installed files, runs self-tests, and tests uninstallation while the app is running. It stops if the same product is already installed to avoid overwriting it. Validation logs are saved in `dist/`.

### Versioning and GitHub releases

Set the default local version in `<Version>` in [CleanOptimize.App.csproj](src/CleanOptimize.App/CleanOptimize.App.csproj), using `major.minor.patch`, for example `0.0.3`. You can also override a single package build with `publish.ps1 -Version 0.0.3`.

The [Release workflow](.github/workflows/release.yml) supports two ways to run:

| Trigger | Result |
|---|---|
| Push a version tag, such as `v0.0.3` | Build and validate using the tag version, upload the installer, portable package, and checksums, then publish the Release after all uploads succeed |
| Actions → Release → Run workflow, with a branch selected | Build and upload the `CleanOptimize-windows` artifact without publishing a stable release |

Commit and push the code you intend to release, then create an unused version tag on that commit. For example, using `v0.0.3`:

```powershell
git tag v0.0.3
git push origin v0.0.3
```

Tag builds override the project version with the tag version. Update the project file as well to keep local and cloud versions consistent. Tags with suffixes, such as `v0.0.3-preview.1`, produce prereleases that do not trigger stable update notifications. Regular branch pushes and PRs run builds and core regressions through the [Verify workflow](.github/workflows/verify.yml).

## Project structure

```text
src/
  CleanOptimize.App/   WPF UI, themes, tray, and update services
  CleanOptimize.Core/  Cleanup rules, safeguards, analysis, and local storage
  SelfTestRunner/      Core automated test entry point
scripts/              Build, test, packaging, and language resource scripts
.github/workflows/    GitHub Actions verification and release workflows
```

## Feedback and support

Submit bugs and feature suggestions through [Issues](https://github.com/HZ-PRE/cleanwin/issues). Include the app version, Windows version, reproduction steps, and error message. Before sharing logs or screenshots, check for and redact sensitive information such as personal file paths.

Search WeChat for **馋熊冰**. WeChat contact and optional donation links are also available on the app’s About page.

<table>
  <tr><th>Find us on WeChat</th><th>WeChat donations</th></tr>
  <tr>
    <td align="center"><img src="src/CleanOptimize.App/Assets/wechat-search.jpg" alt="Search WeChat for 馋熊冰" width="280" /></td>
    <td align="center"><img src="src/CleanOptimize.App/Assets/wx_pay.png" alt="WeChat donation QR code" width="200" /></td>
  </tr>
</table>

## License

This project uses the [Boost Software License 1.0 (BSL-1.0)](LICENSE), which permits commercial use, modification, and distribution. When distributing source code or source derivatives, retain the copyright notices and the full license text. See [LICENSE](LICENSE) for the complete terms and disclaimer.
