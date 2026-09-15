# Release checklist

1. Build `Release | x64`.
2. Run all tests.
3. Start TraceForge.App and verify the agent connects.
4. Verify process refresh, filtering and selection.
5. Leave live refresh enabled for at least 10 minutes.
6. Export a diagnostic report and inspect the JSON.
7. Confirm `%LOCALAPPDATA%\TraceForge\traceforge.db` is created.
8. Confirm an inaccessible/protected process does not terminate the agent.
9. Confirm closing the UI terminates the child agent.
10. Package and sign the MSIX with the release certificate.
11. Tag the tested commit as `v1.0.0`.
