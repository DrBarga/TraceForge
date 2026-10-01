# TraceForge: data handling and security

Applies to the Windows x64 desktop release **1.0.0**. Reviewed on **1 October 2026** against the published release source. Maintainer: **Bohdan Zelenskyi**, [bogdan.zelya.s@gmail.com](mailto:bogdan.zelya.s@gmail.com).

## What is collected

- During monitoring: process names, executable paths, PID and parent PID, process start time, CPU usage, working-set memory, thread counts, timestamps, availability flags, and Windows error codes. The normal refresh interval is two seconds. Monitoring covers the workstation's process inventory, not only the selected application.
- When you choose Inspect: the selected process's thread IDs and base priorities; loaded module names, paths, and sizes; TCP/UDP local addresses and ports, TCP remote addresses and ports, and connection states. This reads Windows endpoint tables, not network packet contents.
- When you watch a process: its identity, observed exit time and exit code, and up to 60 seconds of preceding sampled process context. The app writes JSON and HTML incident reports automatically after an observed exit.
- When you export: the current process inventory, anomaly flags, timeline, last incident if present, and available inspection data. JSON contains the complete serialized snapshot and timeline; HTML presents a summary. Reports may include information about other processes.
- When you confirm Capture MiniDump: diagnostic process state is written to a .dmp file. Dumps are never captured automatically. Even a MiniDump can contain credentials, personal data, or other sensitive application state.
- For troubleshooting TraceForge itself: app and agent logs record startup and error details. Exceptions can include paths and system information.

Routine monitoring does not collect keystrokes, screenshots, document contents, browser history, network payloads, or process command-line arguments. This is not a guarantee about what may appear inside a user-requested dump.

## Where it is stored and for how long

All desktop diagnostic files are stored under **%LOCALAPPDATA%\TraceForge** for the Windows user running the app.

- **traceforge.db** and its SQLite WAL/SHM sidecars contain sessions, process samples saved approximately every 30 seconds, threshold anomaly flags, and incident metadata. At startup, the app deletes sessions whose end time, or start time when no end is recorded, is older than seven days; related rows are deleted with them. Cleanup is not a continuously running expiry service or secure erasure.
- **Reports** contains exported and automatic incident JSON/HTML files. **Dumps** contains manual dumps. **Logs** contains app and agent logs. These files have no automatic retention or rotation in 1.0.0 and remain until you delete them.
- The **60-second Black Box** is an in-memory rolling buffer. Its contents can persist in exported incident reports; the seven-day database cleanup does not remove those reports.

## What leaves the machine

TraceForge 1.0.0 has no diagnostic upload endpoint, analytics, telemetry, remote diagnostics, or automatic update check. The desktop app and agent communicate through a local Windows named pipe. No diagnostic data is sent to the maintainer by the application.

You control any sharing of reports, logs, or dumps. Email, cloud backups, synchronisation software, endpoint-management tools, and an application launched through TraceForge may transmit data independently. Downloading TraceForge from GitHub and visiting its website involve those providers' normal web requests; those requests are separate from desktop diagnostics.

## Protection and its limits

The app runs with your existing Windows permissions and does not automatically elevate or install a service. Protected processes may be unavailable. Each app instance starts its own native agent with a unique pipe name. The pipe rejects remote clients and its access control list allows the current Windows user and LOCAL SYSTEM.

Diagnostic files inherit the Windows profile directory's permissions. TraceForge does not apply its own encryption, password protection, or automatic redaction. Another process running as you, an administrator, or local malware may be able to read the data. Use your organisation's Windows access controls, approved disk encryption, and backup policy where appropriate.

The 1.0.0 executables are not code-signed. A SHA-256 checksum is supplied with the release to check archive integrity; it does not authenticate a publisher independently of the download source. No independent security audit or compliance certification is claimed.

## Sharing and deletion

Before sharing, review process and module paths, addresses, ports, and other workstation details. JSON can contain more data than the HTML summary. There is no built-in redaction workflow in 1.0.0. Share dumps only with an authorised recipient through an approved channel.

To remove local history, close all TraceForge instances and delete traceforge.db together with any traceforge.db-wal and traceforge.db-shm files. Delete the Reports, Dumps, and Logs files you no longer need, or delete the TraceForge data folder to remove all local data. The next launch recreates it. Normal file deletion does not guarantee forensic erasure and does not remove copies in backups or files you have shared.

Questions or suspected vulnerabilities: contact **bogdan.zelya.s@gmail.com**. Do not attach a sensitive dump or diagnostic report to a public GitHub issue.
