using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal interface IRemoteGraphParent
{
    Task<RemoteGraphEdgeResultV1> ExecuteAsync(GrainRequestEnvelope trusted, OperationKind kind,
        ReadOnlyMemory<byte> originalPayload, ReadExecutionBudget work, CancellationToken cancellationToken);

    Task<RemoteIncomingGraphEdgesPageV1> ReadIncomingAsync(GrainRequestEnvelope trusted,
        ReadRemoteIncomingGraphEdgesRequestV1 request, ReadExecutionBudget work,
        CancellationToken cancellationToken);
}
