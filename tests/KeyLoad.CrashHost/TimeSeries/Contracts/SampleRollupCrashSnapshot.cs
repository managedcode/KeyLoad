namespace KeyLoad.CrashHost;

internal sealed record SampleRollupCrashSnapshot(SampleRecord[] Samples, SampleRollupResult Rollup,
    string RawImage, long Position, string AcknowledgedReceipt);
