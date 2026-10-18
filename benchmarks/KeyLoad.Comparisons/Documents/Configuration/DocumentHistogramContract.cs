namespace KeyLoad.Comparisons;

/// <summary>Canonical bounded full-count latency histogram.</summary>
public sealed record DocumentHistogramContract
{
    /// <summary>Gets bucket resolution.</summary>
    public required int ResolutionMicroseconds
    {
        get; init;
    }
    /// <summary>Gets largest admitted measured latency.</summary>
    public required int MaximumMilliseconds
    {
        get; init;
    }
    /// <summary>Gets fail-qualification overflow policy.</summary>
    public required string Overflow
    {
        get; init;
    }
}
