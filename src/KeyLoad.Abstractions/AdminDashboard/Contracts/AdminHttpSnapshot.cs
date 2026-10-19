using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Actual process HTTP observations; counters reset with the host process.</summary>
/// <param name="StartedAt">Process counter start, independent of persisted storage incarnation.</param>
/// <param name="ProcessInstance">Unique nonsecret lifetime identity for rate reset detection.</param>
/// <param name="CompletedRequests">Completed public API HTTP requests excluding dashboard polling.</param>
/// <param name="FailedRequests">Completed requests with an error status or aborted response.</param>
/// <param name="ElapsedMilliseconds">Sum of measured request elapsed milliseconds.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.AdminHttpSnapshot)]
public sealed record AdminHttpSnapshot([property: Orleans.Id(0)] DateTimeOffset StartedAt, [property: Orleans.Id(1)] Guid ProcessInstance, [property: Orleans.Id(2)] long CompletedRequests,
    [property: Orleans.Id(3)] long FailedRequests, [property: Orleans.Id(4)] double ElapsedMilliseconds)
{
    /// <summary>Gets the newest-first bounded process-local failed request log; resets with the host process.</summary>
    [Orleans.Id(5)]
    public ImmutableArray<AdminHttpFailure> RecentFailures { get; init; } = [];
}
