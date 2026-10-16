using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

internal interface IRemoteGraphParent
{
    Task<RemoteGraphEdgeResultV1> ExecuteAsync(GrainRequestEnvelope trusted, OperationKind kind,
        ReadOnlyMemory<byte> originalPayload, ReadExecutionBudget work, CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding);

    Task<RemoteIncomingGraphEdgesPageV1> ReadIncomingAsync(GrainRequestEnvelope trusted,
        ReadRemoteIncomingGraphEdgesRequestV1 request, ReadExecutionBudget work,
        CancellationToken cancellationToken, IResourceLifetimeWorkBinding binding);
}
