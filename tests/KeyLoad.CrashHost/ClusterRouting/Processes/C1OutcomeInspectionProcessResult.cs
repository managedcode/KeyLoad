namespace KeyLoad.CrashHost.Features.ClusterRouting.Processes;

internal sealed record C1OutcomeInspectionProcessResult(
    int ProcessId,
    int ExitCode,
    byte[] StandardOutput,
    byte[] StandardError,
    bool StandardOutputExceeded,
    bool StandardErrorExceeded,
    bool ProcessReaped,
    bool InputWriterSettled,
    bool StandardOutputReaderSettled,
    bool StandardErrorReaderSettled,
    bool ProcessHandleClosed,
    bool OuterOwnerReleased);
