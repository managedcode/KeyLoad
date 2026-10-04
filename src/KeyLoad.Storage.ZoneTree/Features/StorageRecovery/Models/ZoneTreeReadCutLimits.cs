namespace KeyLoad.Storage.ZoneTree;

internal sealed record ZoneTreeReadCutLimits(int MaxRecords, long MaxExaminedBytes, TimeSpan MaxElapsed);
