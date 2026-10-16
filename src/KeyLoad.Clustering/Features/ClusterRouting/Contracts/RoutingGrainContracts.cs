using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>One uniquely keyed actor for a single public database operation.</summary>
[Orleans.Alias(GrainRoutingProtocol.RequestAlias), Orleans.CodeGeneration.Version(GrainRoutingProtocol.RequestInterfaceVersion)]
public interface IConnectionGrain : Orleans.IGrainWithGuidKey
{
    /// <summary>Validate trusted scope and invoke the required database capability.</summary>
    /// <param name="signedRequest">Authenticated server envelope with an exact bounded payload.</param>
    /// <param name="cancellationToken">Caller cancellation propagated through Orleans.</param>
    /// <returns>The lazy native Started and bounded final result stream.</returns>
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

/// <summary>Command routing actor keyed by canonical atomic partition or an administrative catalog.</summary>
[Orleans.Alias(GrainRoutingProtocol.CommandAlias), Orleans.CodeGeneration.Version(GrainRoutingProtocol.CapabilityInterfaceVersion)]
public interface ICommandPartitionGrain : Orleans.IGrainWithStringKey
{
    /// <summary>Revalidate authority and submit the stable command to the node-owned consensus host.</summary>
    /// <param name="signedRequest">Signed request routed to this exact partition key.</param>
    /// <param name="cancellationToken">Bounded caller cancellation.</param>
    /// <returns>Committed result or typed safe rejection, preserving unknown-write outcomes.</returns>
    [Orleans.Alias(GrainRoutingProtocol.ExecuteAlias)]
    Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken);

    /// <summary>Prepares the original authenticated command and returns its bounded target-owned claim without effects.</summary>
    /// <param name="signedRequest">The original signed command and finite observation admission.</param>
    /// <param name="cancellationToken">Original preparation cancellation.</param>
    /// <returns>The opaque original-owner proof; no command has been submitted.</returns>
    [Orleans.Alias(NativeCommandObservationPreparation.Alias)]
    Task<GrainOperationReply> PrepareOriginalCommandAsync(string signedRequest, CancellationToken cancellationToken);
}
