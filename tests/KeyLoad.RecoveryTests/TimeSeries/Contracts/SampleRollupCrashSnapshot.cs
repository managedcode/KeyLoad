namespace KeyLoad.RecoveryTests;

internal sealed record SampleRollupCrashSnapshot(SampleRecord[] Samples, SampleRollupResult Rollup,
    string RawImage, long Position, string AcknowledgedReceipt);
