# v1.0 RC1 scope matrix

This release candidate is intended to get TraceForge to its first integrated Windows launch, not to silently mark every stretch item from the original long-term roadmap as finished.

## Implemented in RC1

- C# WinUI 3 desktop shell
- C++20 native Agent
- Named Pipe IPC
- UTF-8 JSON request/response protocol
- Process enumeration
- PID, name and full executable path
- CPU sampling
- Working set memory sampling
- Thread count
- Protected/inaccessible process handling
- Live process search
- Selected-process details
- Local SQLite session storage
- Rule-based CPU/memory anomalies
- 60-second Black Box snapshot buffer
- JSON diagnostic report export
- x64 Debug/Release configuration
- GitHub Actions build workflow
- Release and architecture documentation
- Application-layer unit tests

## Requires the final Windows validation pass before v1.0.0

- Build on the exact Visual Studio 2026 installation
- NuGet restore validation
- Packaged WinUI child-agent deployment validation
- Long-running sampling test
- SQLite growth/retention test
- MSIX signing and installation smoke test

## Original roadmap items intentionally not claimed as complete in RC1

- Full per-thread inspector
- Full module/DLL inspector UI
- Open-handle/file inspector
- Per-process TCP/UDP connection inspector
- Automatic crash-time MiniDump capture
- PDB/symbol resolution
- Windows Event Log/WER crash correlation
- Hang detection timeline UI
- LLM/AI diagnostic provider
- Automatic updater
- Stable public plugin API

These should be completed and validated before calling the project feature-complete against the original extended roadmap. RC1 is the integrated launch baseline.
