namespace KeyLoad.FunctionalTests.BackupRestore;

internal sealed record CliBackupRestoreProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool OriginalExitJoined,
    bool StandardOutputJoined,
    bool StandardErrorJoined,
    bool ProcessDisposed);
