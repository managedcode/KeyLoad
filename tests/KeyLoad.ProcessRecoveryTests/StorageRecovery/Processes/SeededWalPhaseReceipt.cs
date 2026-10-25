namespace KeyLoad.ProcessRecoveryTests.StorageRecovery;

internal sealed record SeededWalPhaseReceipt(string Phase, string ExpectedSignal, bool SignalObserved,
    int? ExitCode, bool OriginalExitAndBothReadersJoined, bool ProcessClosed);
