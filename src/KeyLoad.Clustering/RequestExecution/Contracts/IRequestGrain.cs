using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>One stateless grain per admitted operation: it finds the storage owners, executes or fans out, and returns.</summary>
[Orleans.Alias(GrainRoutingProtocol.RequestGrainAlias), Orleans.CodeGeneration.Version(GrainRoutingProtocol.RequestGrainInterfaceVersion)]
public interface IRequestGrain : Orleans.IGrainWithGuidKey
{
    /// <summary>Runs the operation and returns its joined terminal reply.</summary>
    /// <param name="request">Authenticated server envelope with its typed operation.</param>
    /// <param name="cancellationToken">Caller cancellation propagated through Orleans and joined cleanup.</param>
    /// <returns>The original terminal reply after complete stream settlement.</returns>
    [Orleans.Alias(GrainRoutingProtocol.ExecuteAlias), Orleans.ResponseTimeout(GrainRoutingOptions.MaximumExecutionLifetime)]
    Task<GrainOperationReply> ExecuteAsync(GrainRequestEnvelope request, CancellationToken cancellationToken);

    /// <summary>Runs the operation as a bounded progress stream.</summary>
    /// <param name="request">Authenticated server envelope with its typed operation.</param>
    /// <param name="cancellationToken">Caller cancellation propagated through Orleans.</param>
    /// <returns>The lazy execution Started and bounded final result stream.</returns>
    [Orleans.Alias(GrainRoutingProtocol.ExecuteStreamAlias)]
    IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> ExecuteStreamAsync(
        GrainRequestEnvelope request, CancellationToken cancellationToken);

    /// <summary>Releases the activation after its caller disposed the operation's stream.</summary>
    /// <returns>Completion once deactivation is requested.</returns>
    [Orleans.Alias(GrainRoutingProtocol.ReleaseRequestAlias)]
    Task ReleaseAsync();
}
