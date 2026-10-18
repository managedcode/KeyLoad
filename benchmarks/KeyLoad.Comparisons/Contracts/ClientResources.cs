namespace KeyLoad.Comparisons;

/// <summary>Reports resource counters sampled from the load-generator process during a measurement; they do not describe server use or attribute costs to individual operations.</summary>
/// <param name="CpuSeconds">CPU time consumed by the load-generator process during collection, in seconds.</param>
/// <param name="AllocatedBytes">The number of bytes allocated by the load-generator process during collection, not an allocation total attributed to individual operations.</param>
/// <param name="PeakObservedWorkingSetBytes">The largest load-generator process working-set value observed at a sampling point, in bytes; this may be below the process's true peak.</param>
/// <param name="SamplingIntervalMs">The interval between load-generator process resource samples, in milliseconds.</param>
public sealed record ClientResources(double CpuSeconds, long AllocatedBytes, long PeakObservedWorkingSetBytes, int SamplingIntervalMs);
