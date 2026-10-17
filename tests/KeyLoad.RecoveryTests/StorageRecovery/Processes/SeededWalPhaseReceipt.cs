namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal sealed record SeededWalPhaseReceipt(string Phase, string ExpectedSignal, bool SignalObserved,
    int? ExitCode, bool OriginalExitAndBothReadersJoined, bool NativeProcessClosed);
