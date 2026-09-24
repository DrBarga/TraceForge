# Contributing to TraceForge

TraceForge is a Windows diagnostics application with a C# presentation and application layer and a native C++ Agent. Keep changes inside the layer that owns the behavior.

## Development environment

- Windows 10 2004 or newer
- Visual Studio 2026
- .NET 10 SDK
- Desktop development with C++ workload
- WinUI application development workload

Open `TraceForge.slnx`, restore packages, and build `Debug | x64` before making changes.

## Engineering rules

- Keep Windows API collection and dump work in `TraceForge.Agent`.
- Keep orchestration, protocol models, diagnostics and report generation in `TraceForge.Application`.
- Keep SQLite implementation details in `TraceForge.Data`.
- Keep WinUI state and presentation in `TraceForge.App`.
- Treat unavailable telemetry as unavailable. Never replace a collection failure with a plausible zero.
- Bound IPC input sizes and waits.
- Preserve protocol compatibility or increment the protocol version.
- Add a migration for every database schema change.
- Measure storage and CPU cost before adding a high-frequency collector.
- Do not write secrets, dump contents or full report payloads to TraceForge logs.

## Verification

Run both commands from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-debug.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

Then run the tests:

```powershell
dotnet test .\tests\TraceForge.Application.Tests\TraceForge.Application.Tests.csproj --configuration Release --no-build
```

Changes to Agent lifetime, IPC, collectors, storage or dumps also require the matching manual checks in `docs/RELEASE.md`.
