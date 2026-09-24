# Architecture

TraceForge is split into four production projects.

`TraceForge.App` owns the WinUI presentation and application lifetime.

`TraceForge.Application` owns IPC contracts, monitoring orchestration, diagnostics, Black Box buffering and report generation.

`TraceForge.Data` implements local persistence through SQLite.

`TraceForge.Agent` is a native C++20 process that talks to Windows APIs and exposes process telemetry over a local named pipe.

Dependency direction:

`TraceForge.App -> TraceForge.Application`

`TraceForge.App -> TraceForge.Data -> TraceForge.Application`

`TraceForge.App -> TraceForge.Agent` only as a build/runtime dependency.

The App starts one Agent per instance and gives it a unique pipe name. `\\.\pipe\TraceForge.Agent.v1` remains the Agent's default for direct protocol testing.

Requests and responses are newline-delimited UTF-8 JSON. The protocol currently reports version `1`; the App rejects incompatible Agents. Each request has a client-side timeout, and the App restarts and reconnects the child Agent after a communication failure.

The Named Pipe rejects remote clients and its access control list grants access only to the current Windows user and the local system account.

## Telemetry contract

Every process metric carries both a value and an availability state. A zero CPU or memory value is treated as data only when the corresponding availability flag is true. Win32 errors are preserved separately for process access, executable path, CPU timing and working-set queries.

## Storage

SQLite uses WAL mode, foreign keys on every connection, schema version `4`, additive startup migrations and a seven-day session retention window. The UI refreshes every two seconds, while full process snapshots are persisted every 30 seconds to keep database growth bounded. The in-memory Black Box retains the denser 60-second timeline used in reports. Incidents are persisted separately and remain visible in the History dialog.

## Native inspection and process watch

The Agent uses Toolhelp32 snapshots for threads and modules, IP Helper tables for TCP/UDP endpoints and a synchronization handle for the watched process. The synchronization handle prevents a recycled PID from being mistaken for the original process. Inspection responses include process start time so the App can reject stale selections.

## MiniDump capture

The App can request an on-demand MiniDump for the selected process. The C++ Agent opens the target process, calls `MiniDumpWriteDump`, and writes the result under `%LOCALAPPDATA%\TraceForge\Dumps`. The App displays a privacy warning before capture because process memory can contain sensitive data.
