# TraceForge

TraceForge is a Windows desktop diagnostics tool built with C# / WinUI 3 and a native C++20 agent.

## v1.0 RC1 scope

- Live Windows process inventory
- PID, executable name, full executable path
- CPU usage, working set memory and thread count
- Native C++20 Win32 agent
- Named Pipe IPC between the WinUI application and the agent
- Live search and process details
- Local SQLite session history
- Rule-based anomaly detection
- 60-second Black Box snapshot buffer
- JSON diagnostic report export
- x64 Debug and Release builds
- GitHub Actions build workflow

## Requirements

- Windows 10 2004 or newer
- Visual Studio 2026
- .NET 10 SDK
- Desktop development with C++ workload
- .NET desktop development workload
- WinUI application development workload

## Run

1. Open `TraceForge.slnx` in Visual Studio.
2. Select `Debug | x64`.
3. Restore NuGet packages.
4. Build the solution.
5. Set `TraceForge.App` as Startup Project.
6. Press `F5`.

The native agent is a build dependency of the WinUI application and is copied into the application output.

## Local data

TraceForge writes data to:

`%LOCALAPPDATA%\TraceForge`

The folder contains `traceforge.db` and exported reports.

## Release status

This archive is a v1.0 release candidate assembled from the current TraceForge architecture. The Windows-specific projects still need one final build and smoke test on the target Windows machine before tagging `v1.0.0`.
