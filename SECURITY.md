# Security policy

## Reporting a vulnerability

Do not open a public issue for a vulnerability that could expose process memory, local files, diagnostic reports or Named Pipe control. Send a private report to the repository owner with the affected version, reproduction steps and impact.

## Sensitive artifacts

TraceForge reports can contain executable paths and process names. MiniDump files can contain credentials, tokens, personal data and application secrets. Dumps are created only after an explicit user action and are stored locally under `%LOCALAPPDATA%\TraceForge\Dumps`.

TraceForge does not upload reports or dumps. Anyone adding a remote integration must make transmission opt-in, show the exact data being sent and document retention at the receiving service.

## Local trust boundary

The native Agent accepts local Named Pipe connections from the current Windows user and the local system account. Requests are size-limited and processed sequentially; the App checks the protocol version during the initial ping. The App applies request timeouts and restarts its child Agent after a communication failure.
