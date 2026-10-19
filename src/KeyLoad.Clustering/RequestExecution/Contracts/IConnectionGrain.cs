using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>One uniquely keyed actor for a single public database operation.</summary>
[Orleans.Alias(GrainRoutingProtocol.RequestAlias), Orleans.CodeGeneration.Version(GrainRoutingProtocol.RequestInterfaceVersion)]
public interface IConnectionGrain : Orleans.IGrainWithGuidKey
{
    /// <summary>Validate trusted scope and invoke the required database capability.</summary>
    /// <param name="signedRequest">Authenticated server envelope with an exact bounded payload.</param>
    /// <param name="cancellationToken">Caller cancellation propagated through Orleans.</param>
    /// <returns>The lazy execution Started and bounded final result stream.</returns>
    [Orleans.Alias(GrainRoutingProtocol.ExecuteStreamAlias)]
    IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        string signedRequest, CancellationToken cancellationToken);

    /// <summary>Close this server-owned connection and join its original operation work.</summary>
    /// <param name="signedControl">The expiring control bound to this database and connection.</param>
    /// <param name="cancellationToken">Caller cancellation while observing the original cleanup.</param>
    /// <returns>Completion after admission closes and original work settles.</returns>
    [Orleans.Alias(GrainRoutingProtocol.CloseConnectionAlias), Orleans.Concurrency.AlwaysInterleave]
    Task CloseAsync(string signedControl, CancellationToken cancellationToken);
}
