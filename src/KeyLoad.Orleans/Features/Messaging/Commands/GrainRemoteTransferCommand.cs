using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Orleans;

internal static class GrainRemoteTransferCommand
{
    internal static string Resolve(DecodedGrainRequest request)
    {
        var command = NativeCommandPayload.Read<CommandRequest>(Read(request));
        if (command.Partition is null || command.Mutations.Length != RemoteTransferPeerProtocol.SingleMutation
            || command.Mutations[RemoteTransferPeerProtocol.FirstMutation] is not AcceptQueueTransfer accept
            || accept.DestinationQueue.Partition != command.Partition)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        JsonData.Identifier(command.Partition.TenantId);
        JsonData.Identifier(command.Partition.DatabaseId);
        JsonData.Identifier(command.Partition.TransactionDomainId);
        JsonData.Identifier(command.Partition.PartitionKey);
        return command.Partition.AtomicPartitionId;
    }

    internal static Task<OperationResult> SubmitAsync(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, PrincipalRecord principal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var operation = database.VerifyOperationAuthority(Read(request));
        if (operation.PrincipalId != principal.Id || !principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
        return coordinator.SubmitVerifiedAsync(operation, token);
    }

    private static ReplicatedOperation Read(DecodedGrainRequest request)
    {
        if (!RemoteTransferRequestScope.Validate(request.Envelope)
            || request.Envelope.CommandKind != OperationKind.Batch)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var operation = GrainNativePayload.Read<ReplicatedOperation>(request.Payload);
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        if (operation.Kind != OperationKind.Batch || operation.Id != request.Envelope.CommandId
            || operation.PrincipalId != request.Envelope.PrincipalId || payload.TransferProof.IsEmpty)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return operation;
    }
}
