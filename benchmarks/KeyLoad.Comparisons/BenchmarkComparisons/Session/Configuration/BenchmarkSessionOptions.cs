namespace KeyLoad.Comparisons;

/// <summary>Frozen production session counts, with bounded outstanding native requests.</summary>
public sealed record BenchmarkSessionOptions
{
    /// <summary>The initial acknowledged and independently verified corpus size.</summary>
    public int Records { get; init; } = BenchmarkSessionLimits.InitialRecords;
    /// <summary>The operations in each measured stream, including each mixed mutation stream.</summary>
    public int Operations { get; init; } = BenchmarkSessionLimits.StreamOperations;
    /// <summary>The total admitted clients across concurrent streams.</summary>
    public int Concurrency { get; init; } = BenchmarkSessionLimits.Clients;
    /// <summary>The deadline for each actual native operation.</summary>
    public int TimeoutSeconds { get; init; } = BenchmarkSessionLimits.TimeoutSeconds;

    internal void Validate(bool development)
    {
        if (Concurrency is < BenchmarkSessionLimits.DocumentMixedLanes or > BenchmarkSessionLimits.MaximumClients || TimeoutSeconds is < BenchmarkSessionLimits.SingleItemCount or > BenchmarkSessionLimits.MaximumTimeoutSeconds
            || Records < BenchmarkSessionLimits.MinimumRecordGroups * Operations || Records > BenchmarkSessionLimits.InitialRecords || Operations < BenchmarkSessionLimits.SingleItemCount || Operations > BenchmarkSessionLimits.StreamOperations
            || (!development && (Records != BenchmarkSessionLimits.InitialRecords || Operations != BenchmarkSessionLimits.StreamOperations
                || Concurrency != BenchmarkSessionLimits.Clients || TimeoutSeconds != BenchmarkSessionLimits.TimeoutSeconds)))
        {
            throw new InvalidOperationException(BenchmarkSessionProtocol.InvalidSessionOptions);
        }
    }
}
