# TraceForge

TraceForge is a Windows desktop diagnostics tool built with C# / WinUI 3 and a native C++20 agent.

## What it does

- Live Windows process inventory
- PID, executable name, full executable path
- Parent PID, CPU usage, working set memory and thread count
- Explicit availability and Win32 error state for every collected metric
- Native C++20 Win32 agent
- Named Pipe IPC between the WinUI application and the agent
- Live search and process details
- Local SQLite session and incident history
- Rule-based anomaly detection
- 60-second Black Box snapshot buffer
- JSON and standalone HTML diagnostic report export
- On-demand MiniDump capture for a selected process
- Threads, loaded modules and TCP/UDP endpoint inspection
- Launch and watch a target application, or watch an existing process through exit
- Automatic JSON and HTML incident reports with the preceding Black Box timeline
- Automatic Agent restart after IPC failures
- Per-request IPC timeouts and protocol-version validation
- Current-user-only Named Pipe access
- Versioned SQLite schema, seven-day retention and 30-second persistence cadence
- Internal App and Agent logs under `%LOCALAPPDATA%\TraceForge\Logs`
- x64 Debug and Release builds
- GitHub Actions build workflow

## Use the portable package

Extract `TraceForge-1.0.0-win-x64.zip` and run `TraceForge.App.exe`. Keep the folder together; the application starts its bundled Agent automatically. No administrator rights or installer are required for ordinary process diagnostics. Windows may limit access to protected or elevated processes.

Search for a process by name or PID, select it, and use **Inspect** for threads, modules and network endpoints. Use **Watch selected process** or **Launch and watch an application** to record an exit. **Export report** writes a standalone JSON and HTML pair to `%LOCALAPPDATA%\TraceForge\Reports`.

The accompanying `.sha256` file contains the SHA-256 checksum of the ZIP. Verify it with `Get-FileHash` before distributing a copied archive.

## Build requirements

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

## Build a portable x64 package

From PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

The script restores and builds the C++/C# solution, runs the tests, publishes the portable folder, and creates a versioned ZIP with a SHA-256 checksum under `artifacts\dist`.

The unpacked application is written to:

`src\TraceForge.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish`

Copy the whole folder when distributing TraceForge. The .NET and Windows App SDK runtimes are included, and the native Agent uses the static C++ runtime. The portable package is not code-signed, so Windows may show a publisher warning.

## Local data

TraceForge writes data to:

`%LOCALAPPDATA%\TraceForge`

The folder contains:

- `traceforge.db` for session history
- `Reports` for JSON and HTML reports
- `Dumps` for manually captured MiniDump files
- `Logs` for TraceForge's own diagnostics

Memory dumps can contain credentials and personal data. Keep them private and share them only with trusted recipients.

## Scope and limitations

Version `1.0.0` is a portable x64 release. TraceForge watches one process at a time and records its exit code; it does not claim that every nonzero exit is a crash. Protected processes can return incomplete diagnostics. It does not automatically capture dumps, resolve symbols, detect hangs or correlate Windows Error Reporting events. See [the release scope](docs/V1_SCOPE.md) for details.
