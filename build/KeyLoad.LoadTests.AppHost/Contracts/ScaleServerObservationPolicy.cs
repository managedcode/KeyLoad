namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

/// <summary>Actual primitive observation policy retained with execution measurement evidence.</summary>
internal sealed record ScaleServerObservationPolicy(double CadenceMilliseconds, double MaximumObservationMilliseconds,
    double CleanupThresholdMilliseconds, double ProcessSettlementMilliseconds, int MaxProcesses, int MaxMounts,
    int MaxFileBytes, int MinimumCommandBytes, int MaxHardwareBytes, int MaxSampleMetadataBytes, int MaxSidecarBytes,
    int MaxWorkerBytes, int MaxSamples, int MaxCgroupAncestors, int RuntimeReadBufferBytes, int MaxOutputBytes, int StandardErrorOutputDivisor);
