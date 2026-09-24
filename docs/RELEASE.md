# TraceForge 1.0.0 release procedure

Run `scripts/build-release.ps1` on Windows with Visual Studio 2026, the C++ desktop workload and .NET 10 SDK. The script builds the solution, runs the tests, publishes the self-contained x64 application and writes a ZIP and SHA-256 file to `artifacts/dist`.

Before distributing a build:

1. Launch `TraceForge.App.exe` from the extracted ZIP, not a development output folder.
2. Check live refresh, search, selection, inspection, history and report export.
3. Watch a disposable process through exit and check the incident report and history entry.
4. Capture a MiniDump of a disposable process after reading the privacy warning.
5. Stop the Agent during live refresh and confirm recovery; close the App and confirm that no child Agent remains.
6. Verify the ZIP checksum and confirm that the archive includes `TraceForge.Agent.exe`.
7. Check the Windows CI run for the exact commit being distributed.

The distributed ZIP is portable and unsigned. A signed installer, clean-machine installation test and automatic update channel are separate future work. Do not claim those checks as completed by a local build.
