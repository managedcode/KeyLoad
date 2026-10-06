namespace KeyLoad.Comparisons;

/// <summary>Exact request and bounded latency-sample accounting for a scaled operation case.</summary>
/// <param name="Requested">Declared operation count.</param>
/// <param name="Attempted">Operations actually started.</param>
/// <param name="Successes">Caller operations that passed their result oracle.</param>
/// <param name="Failures">Started operations that failed.</param>
/// <param name="DeadlineTimeouts">Operations canceled by their per-operation deadline.</param>
/// <param name="Rejections">Started operations rejected by the target or result oracle.</param>
/// <param name="Unfinished">Declared operations not started due to cancellation or interruption.</param>
/// <param name="SamplingAlgorithm">Stable latency sample-selection algorithm identifier.</param>
/// <param name="SampleCapacity">Maximum number of retained sample values.</param>
/// <param name="CollectedSamples">Latency slots actually retained.</param>
/// <param name="MissingSamples">Selected operation indices that did not complete.</param>
public sealed record ScaledOperationAccounting(int Requested, int Attempted, int Successes, int Failures,
    int DeadlineTimeouts, int Rejections, int Unfinished, string SamplingAlgorithm, int SampleCapacity,
    int CollectedSamples, int MissingSamples)
{
    private const string SampledLatencyQuantileMethod = "sampled-estimate";

    /// <summary>Gets the interpretation of the reported latency quantiles.</summary>
    public string LatencyQuantileMethod => SampledLatencyQuantileMethod;
}
