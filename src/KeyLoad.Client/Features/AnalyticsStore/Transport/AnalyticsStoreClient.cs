using ManagedCode.Communication;

namespace KeyLoad.Client;

public sealed partial class KeyLoadClient
{
    /// <summary>Reads an authorized bounded canonical event page from an independent analytics store.</summary>
    /// <param name="request">Store, atomic partition, half-open UTC range and ordinal continuation.</param>
    /// <param name="cancellationToken">Cancellation throughout the original HTTP operation.</param>
    /// <returns>Complete authorized page with explicit continuation.</returns>
    public Task<Result<AnalyticsEventsPage>> ReadAnalyticsEventsAsync(ReadAnalyticsEventsRequest request,
        CancellationToken cancellationToken = default)
        => Send<AnalyticsEventsPage>(AnalyticsStoreProtocol.ReadRoute, request, false, null, cancellationToken);

    /// <summary>Queries complete native aggregates over the current authorized canonical event cut.</summary>
    /// <param name="request">Store, atomic partition, range, declared dimensions and metric selections.</param>
    /// <param name="cancellationToken">Cancellation throughout the original HTTP operation.</param>
    /// <returns>Complete bounded groups; incomplete native plans refuse the operation.</returns>
    public Task<Result<AnalyticsAggregateResult>> AggregateAnalyticsAsync(AggregateAnalyticsRequest request,
        CancellationToken cancellationToken = default)
        => Send<AnalyticsAggregateResult>(AnalyticsStoreProtocol.AggregateRoute, request, false, null, cancellationToken);
}
