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

IPC pipe: `\\.\pipe\TraceForge.Agent.v1`

Requests and responses are newline-delimited UTF-8 JSON.
