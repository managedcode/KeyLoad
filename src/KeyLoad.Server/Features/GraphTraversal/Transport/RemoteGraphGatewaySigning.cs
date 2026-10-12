using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphGatewaySigning
{
    internal const string Interrupted =
        "The remote graph write outcome is uncertain; retry the original command to reconcile its receipts.";

    internal static bool Handles(GrainReadKind? readKind, OperationKind? commandKind)
        => readKind == GrainReadKind.RemoteGraphIncomingParent || HandlesCommand(commandKind);

    internal static bool HandlesCommand(OperationKind? kind)
        => kind is OperationKind.UpsertRemoteGraphEdge or OperationKind.DeleteRemoteGraphEdge
            or OperationKind.DeliverRemoteGraphEdge;

    internal static string Create(GrainRequestCodec codec, TimeProvider clock, Guid requestId,
        string principalId, GrainReadKind? readKind, OperationKind? commandKind, Guid commandId,
        ReadOnlyMemory<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(clock);
        var expiry = clock.GetUtcNow() + codec.ExecutionOptions.Value.RequestLifetime;
        if (readKind == GrainReadKind.RemoteGraphIncomingParent)
        {
            if (commandKind is not null || commandId != Guid.Empty)
            { throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest); }
            _ = GrainNativePayload.ReadPublicInput<ReadRemoteIncomingGraphEdgesRequestV1>(payload);
            return codec.RemoteGraph.CreateRemoteGraphParentRead(requestId, principalId, payload, expiry);
        }
        if (readKind is not null || !HandlesCommand(commandKind) || commandId == Guid.Empty
            || OriginalCommandId(commandKind!.Value, payload) != commandId)
        { throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest); }
        return codec.RemoteGraph.CreateRemoteGraphParentCommand(requestId, principalId, commandKind.Value,
            commandId, payload, expiry);
    }

    private static Guid OriginalCommandId(OperationKind kind, ReadOnlyMemory<byte> payload) => kind switch
    {
        OperationKind.UpsertRemoteGraphEdge => GrainNativePayload.ReadCommand<UpsertRemoteGraphEdgeRequestV1>(payload).CommandId,
        OperationKind.DeleteRemoteGraphEdge => GrainNativePayload.ReadCommand<DeleteRemoteGraphEdgeRequestV1>(payload).CommandId,
        OperationKind.DeliverRemoteGraphEdge => GrainNativePayload.ReadCommand<DeliverRemoteGraphEdgeRequestV1>(payload).CommandId,
        _ => throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest)
    };
}
