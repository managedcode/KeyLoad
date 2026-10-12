using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Orleans;

internal static class GrainRemoteGraphCommand
{
    internal static string Resolve(DecodedGrainRequest request)
    {
        var operation = Read(request);
        var partition = operation.Kind switch
        {
            OperationKind.RemoteGraphSource => NativeCommandPayload.Read<RemoteGraphSourceMutationV1>(operation).Original.SourcePartition,
            OperationKind.RemoteGraphReceive => NativeCommandPayload.Read<RemoteGraphReceiverMutationV1>(operation).SourceProof.Intent.Content.Destination.Partition,
            OperationKind.RemoteGraphComplete => NativeCommandPayload.Read<RemoteGraphCompletionMutationV1>(operation).Receipt.Intent.Content.SourcePartition,
            _ => throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest)
        };
        ArgumentNullException.ThrowIfNull(partition);
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
        return partition.AtomicPartitionId;
    }

    internal static Task<OperationResult> SubmitAsync(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, PrincipalRecord principal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(principal);
        cancellationToken.ThrowIfCancellationRequested();
        var operation = database.VerifyOperationAuthority(Read(request));
        if (operation.PrincipalId != principal.Id)
        { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
        return coordinator.SubmitVerifiedAsync(operation, cancellationToken);
    }

    private static ReplicatedOperation Read(DecodedGrainRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Envelope);
        if (!RemoteGraphRequestScope.Validate(request.Envelope))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var operation = GrainNativePayload.Read<ReplicatedOperation>(request.Payload);
        if (operation.Kind is not (OperationKind.RemoteGraphSource or OperationKind.RemoteGraphReceive or OperationKind.RemoteGraphComplete)
            || operation.Kind != request.Envelope.CommandKind || operation.Id != request.Envelope.CommandId
            || operation.PrincipalId != request.Envelope.PrincipalId)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return operation;
    }
}
