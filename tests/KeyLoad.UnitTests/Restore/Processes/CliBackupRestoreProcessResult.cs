namespace KeyLoad.UnitTests.BackupRestore;

internal sealed record CliBackupRestoreProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool OriginalExitJoined,
    bool StandardOutputJoined,
    bool StandardErrorJoined,
    bool ProcessDisposed);
