using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

/// <summary>One uniquely keyed actor for a single public database operation.</summary>
[global::Orleans.Alias(GrainRoutingProtocol.RequestAlias), global::Orleans.CodeGeneration.Version(GrainRoutingProtocol.RequestInterfaceVersion)]
public interface IRequestGrain : global::Orleans.IGrainWithGuidKey
{
    /// <summary>Validate trusted scope and invoke the required database capability.</summary>
    /// <param name="signedRequest">Authenticated server envelope with an exact bounded payload.</param>
    /// <param name="cancellationToken">Caller cancellation propagated through Orleans.</param>
    /// <returns>The lazy native Started and bounded final result stream.</returns>
    [global::Orleans.Alias(GrainRoutingProtocol.ExecuteStreamAlias)]
    IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        string signedRequest, CancellationToken cancellationToken);
}

/// <summary>Command routing actor keyed by canonical atomic partition or an administrative catalog.</summary>
[global::Orleans.Alias(GrainRoutingProtocol.CommandAlias), global::Orleans.CodeGeneration.Version(GrainRoutingProtocol.CapabilityInterfaceVersion)]
public interface ICommandPartitionGrain : global::Orleans.IGrainWithStringKey
{
    /// <summary>Revalidate authority and submit the stable command to the node-owned consensus host.</summary>
    /// <param name="signedRequest">Signed request routed to this exact partition key.</param>
    /// <param name="cancellationToken">Bounded caller cancellation.</param>
    /// <returns>Committed result or typed safe rejection, preserving unknown-write outcomes.</returns>
    [global::Orleans.Alias(GrainRoutingProtocol.ExecuteAlias)]
    Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken);
}

/// <summary>Independently keyed read capability with its own quorum cut and persisted authorization.</summary>
[global::Orleans.Alias(GrainRoutingProtocol.ReadAlias), global::Orleans.CodeGeneration.Version(GrainRoutingProtocol.CapabilityInterfaceVersion)]
public interface IDatabaseReadGrain : global::Orleans.IGrainWithGuidKey
{
    /// <summary>Acquire a quorum cut and execute the requested authorized database read.</summary>
    /// <param name="signedRequest">Signed request whose GUID equals this actor's key.</param>
    /// <param name="cancellationToken">Cancellation shared by barrier and read execution.</param>
    /// <returns>Bounded exact JSON or typed safe rejection.</returns>
    [global::Orleans.Alias(GrainRoutingProtocol.ExecuteAlias)]
    Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken);
}
