using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>One server-owned connection routes independent operations and joins their original work.</summary>
[Orleans.Alias(GrainRoutingProtocol.RequestAlias), Orleans.CodeGeneration.Version(GrainRoutingProtocol.RequestInterfaceVersion)]
public interface IConnectionGrain : Orleans.IGrainWithGuidKey
{
    /// <summary>Consumes the bounded CQRS operation locally and returns its joined terminal reply.</summary>
    /// <param name="request">Authenticated server envelope with an exact bounded payload.</param>
    /// <param name="cancellationToken">Caller cancellation propagated through Orleans and joined cleanup.</param>
    /// <returns>The original terminal reply after complete stream settlement.</returns>
    [Orleans.Alias(GrainRoutingProtocol.ExecuteAlias), Orleans.Concurrency.AlwaysInterleave,
        Orleans.ResponseTimeout(GrainRoutingOptions.MaximumExecutionLifetime)]
    Task<GrainOperationReply> ExecuteAsync(GrainRequestEnvelope request, CancellationToken cancellationToken);

    /// <summary>Validate trusted scope and invoke the required database capability.</summary>
    /// <param name="request">Authenticated server envelope with an exact bounded payload.</param>
    /// <param name="cancellationToken">Caller cancellation propagated through Orleans.</param>
    /// <returns>The lazy execution Started and bounded final result stream.</returns>
    [Orleans.Alias(GrainRoutingProtocol.ExecuteStreamAlias)]
    IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        GrainRequestEnvelope request, CancellationToken cancellationToken);

    /// <summary>Close this server-owned connection and join its original operation work.</summary>
    /// <param name="closeControl">The expiring control bound to this database and connection.</param>
    /// <param name="cancellationToken">Caller cancellation while observing the original cleanup.</param>
    /// <returns>Completion after admission closes and original work settles.</returns>
    [Orleans.Alias(GrainRoutingProtocol.CloseConnectionAlias), Orleans.Concurrency.AlwaysInterleave]
    Task CloseAsync(GrainConnectionCloseControl closeControl, CancellationToken cancellationToken);
}
