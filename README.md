# TraceForge

**Local Windows diagnostics for developers and support engineers.**

See what is running, inspect a process, and keep the context before a watched application exits. TraceForge combines a C# / WinUI 3 desktop app with a native C++20 agent. Diagnostic data stays on your machine; there is no account, telemetry service, or background upload.

[Download 1.0.0](https://github.com/DrBarga/TraceForge/releases/tag/v1.0.0) · [Data handling](docs/DATA_HANDLING.md) · [Security brief (PDF)](output/pdf/TraceForge-data-handling.pdf) · [Architecture](docs/ARCHITECTURE.md)

## When to use it

- An application consumes too much CPU or memory: find the process and inspect its metrics and loaded modules.
- An application exits intermittently: watch it and save the preceding 60 seconds of sampled process context with its exit code.
- A support request needs evidence: export a JSON report for analysis and an HTML report that opens in a browser.
- You need a closer look at a process: inspect its threads and TCP/UDP endpoints, or explicitly capture a MiniDump.

An exit code is evidence, not a diagnosis. TraceForge does not treat every nonzero exit as a crash.

## Get started

1. Download `TraceForge-1.0.0-win-x64.zip` and its `.sha256` file from the [release page](https://github.com/DrBarga/TraceForge/releases/tag/v1.0.0).
2. Check the archive with `Get-FileHash .\TraceForge-1.0.0-win-x64.zip -Algorithm SHA256` and compare the result with the checksum file.
3. Extract the ZIP and run `TraceForge.App.exe`. Keep the extracted files together.
4. Search by process name, path, or PID. Select a process and choose **Inspect**, **Watch selected process**, or **Capture MiniDump**.

The portable package includes the .NET and Windows App SDK runtimes. It targets **Windows 10 version 2004 or newer, x64**. Ordinary diagnostics do not require administrator rights. Windows may restrict access to protected or elevated processes.

The executables are **not code-signed**. This release has no installer; Windows may display a publisher warning. The published checksum verifies archive integrity, but is not a substitute for a publisher signature.

## What is included in 1.0.0

| Capability | What you get |
| --- | --- |
| Process inventory | PID, parent PID, executable name and path, start time, CPU, working-set memory, thread count |
| Access visibility | Availability flags and Win32 errors, so an unavailable metric is not shown as a genuine zero |
| Process inspector | Thread IDs and priorities, loaded modules and paths, TCP/UDP endpoints |
| Process watch | One existing process or a launched application, observed exit code, preceding 60-second timeline |
| Reports | Local JSON and standalone HTML exports; reports are written automatically after a watched exit |
| MiniDump | Manual capture with a sensitive-data warning and confirmation |
| History | Local SQLite sessions, sampled metrics, anomaly flags, and incident metadata |
| Connection recovery | A separate agent per app instance, request timeouts, protocol checks, and automatic agent restart |

CPU and memory warnings are threshold-based. They do not establish a root cause.

## Data and privacy

TraceForge stores its data under `%LOCALAPPDATA%\TraceForge`:

| Location | Contents | Retention |
| --- | --- | --- |
| `traceforge.db` and SQLite sidecars | Sessions, full process samples, anomaly flags, incident metadata | Expired sessions are removed at app startup after seven days |
| `Reports\` | JSON and HTML reports, including automatic watched-exit reports | Until you delete them |
| `Dumps\` | Manually requested MiniDumps | Until you delete them |
| `Logs\` | App and agent troubleshooting logs | Until you delete them |

The UI normally refreshes every two seconds; database snapshots are saved every 30 seconds. The Black Box timeline is held in memory for 60 seconds and is also included in exported reports. Reports can describe **other processes on the workstation**, not just the selected process.

Files use the Windows user profile's inherited access permissions. TraceForge does not encrypt or redact them. Paths can reveal user or project names; endpoint information can reveal infrastructure; dumps may contain credentials or application data. Review reports and follow your organisation's policy before sharing them.

Read the [data-handling brief](docs/DATA_HANDLING.md) for collection details, storage, access controls, deletion, and the limits of these protections.

## Architecture

```text
TraceForge.App (C# / WinUI 3)
  ├─ TraceForge.Application: monitoring, IPC contracts, diagnostics, reports
  ├─ TraceForge.Data: SQLite persistence
  └─ TraceForge.Agent (C++20): Win32 collection, inspection, watch, MiniDump
       └─ local named pipe: newline-delimited UTF-8 JSON
```

The agent uses Windows APIs, including Toolhelp32, PSAPI, IP Helper, and DbgHelp. Its named pipe rejects remote clients and grants access to the current Windows user and `LOCAL SYSTEM`. A unique pipe name separates app instances. These controls do not isolate the app from another program already running as the same user or an administrator.

See [architecture](docs/ARCHITECTURE.md) and [release scope](docs/V1_SCOPE.md).

## Build and test

Use Windows with Visual Studio 2026, the .NET 10 SDK, and the C++, .NET desktop, and WinUI development workloads.

Open `TraceForge.slnx`, select `Debug | x64`, build, and run `TraceForge.App` as the startup project. The native agent is built and copied automatically.

To build, test, and package a self-contained release:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

The script performs a full rebuild, runs the tests, and creates the ZIP and checksum in `artifacts\dist`. [Windows CI](https://github.com/DrBarga/TraceForge/actions/workflows/windows-ci.yml) validates Debug and Release. The 1.0.0 release passed 15 automated tests and extracted-package checks covering launch, report export, process exits, and agent reconnection.

The product website lives in [`website/`](website/README.md). It is a static site without analytics or third-party scripts.

## Current limits

TraceForge watches one process at a time. Very short-lived applications can exit before the watch attaches. Protected processes may expose partial data. Version 1.0.0 does not include automatic crash dumps, hang detection, symbol resolution, Windows Error Reporting correlation, an updater, or a Windows service.

## Author and feedback

Built and maintained by **Bohdan Zelya**.

- Contact: [bogdan.zelya.s@gmail.com](mailto:bogdan.zelya.s@gmail.com)
- Bugs and feature requests: [GitHub Issues](https://github.com/DrBarga/TraceForge/issues)
- Sensitive security reports: use email and avoid posting dumps or private reports in public issues.

For a bug report, include the TraceForge version, Windows version, the steps to reproduce, and the observed result. Remove sensitive details before attaching logs or reports.
