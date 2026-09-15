# Migration from the current repository

1. Commit and push the current working tree.
2. Close Visual Studio.
3. Back up the current TraceForge folder.
4. Copy the contents of this release-candidate folder over the repository root.
5. Keep the repository `.git` directory from the existing checkout.
6. Reopen `TraceForge.slnx`.
7. Restore NuGet packages.
8. Select `Debug | x64`.
9. Build the whole solution.
10. Set `TraceForge.App` as Startup Project and run with F5.
11. Review Git Changes before committing the upgrade.

Recommended commit message:

`Assemble TraceForge v1.0 release candidate`
