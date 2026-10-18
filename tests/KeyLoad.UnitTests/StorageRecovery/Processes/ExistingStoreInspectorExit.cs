using KeyLoad.RecoveryTests;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record ExistingStoreInspectorExit(
    int ProcessId,
    int ExitCode,
    bool Canceled,
    string AssemblySha256,
    string Stdout,
    string Stderr,
    ExistingStoreInspectionReceipt? Receipt,
    bool ProcessReaped,
    bool StdoutReaderSettled,
    bool StderrReaderSettled,
    bool OuterOwnerReleased,
    bool CleanupWarningExceeded);
