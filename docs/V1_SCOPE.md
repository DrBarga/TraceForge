# TraceForge 1.0.0 scope

TraceForge 1.0.0 is a portable Windows x64 diagnostics application. It is intended for developers and support engineers who need a fast view of a local process, its resource use, and its exit context without sending telemetry to a service.

## Included

- Live process inventory with CPU, working-set memory, thread count, parent PID, executable path and process start time.
- Search by name, path or PID; process details remain selected during live refresh.
- On-demand inspection of threads, loaded modules and TCP/UDP endpoints, with explicit Win32 errors where access is restricted.
- Launch-and-watch workflow and watch of an already running process. When a watched process exits, TraceForge keeps a 60-second in-memory timeline and writes JSON and HTML incident reports.
- Manual MiniDump capture with a privacy warning. Dumps are never collected automatically.
- Local SQLite sessions and incident history with a seven-day session retention window.
- Rule-based CPU and memory anomaly flags, standalone report export, agent recovery and per-instance named-pipe isolation.
- Self-contained portable x64 ZIP with a SHA-256 checksum.

## Limits

- A watch follows one process at a time. Launching an application that exits before the watch attaches may miss its exit.
- An observed exit code is not proof of a crash. TraceForge does not yet correlate Windows Error Reporting or Event Log records.
- Inspection and dump capture depend on the current user's Windows access rights. Protected or elevated processes may expose only partial data.
- There is no automatic crash-time dump, hang detector, symbol resolution, updater or public extension API.
- The portable package is not signed. There is no installer in this release.

The original roadmap describes a broader product. These deferred features are not presented as complete in 1.0.0.
