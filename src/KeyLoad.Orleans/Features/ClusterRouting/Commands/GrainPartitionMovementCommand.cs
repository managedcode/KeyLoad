using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Orleans;

internal static class GrainPartitionMovementCommand
{
    internal static string Resolve(DecodedGrainRequest request)
    {
        var operation = Read(request);
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
        ArgumentNullException.ThrowIfNull(phase.Partition);
        JsonData.Identifier(phase.Partition.TenantId);
        JsonData.Identifier(phase.Partition.DatabaseId);
        JsonData.Identifier(phase.Partition.TransactionDomainId);
        JsonData.Identifier(phase.Partition.PartitionKey);
        return phase.Partition.AtomicPartitionId;
    }

    internal static async Task<OperationResult> SubmitAsync(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, PrincipalRecord principal, GrainRequestCodec? codec,
        IGrainContext? context, IGrainPartitionMovementSealedOperationObserver? sealedObserver, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = database.VerifyOperationAuthority(Read(request));
        if (operation.PrincipalId != principal.Id || !principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
        var phase = NativeCommandPayload.Read<PartitionMovePhaseCommand>(operation);
        if (phase.Stage == PartitionMovePeerStage.Retire && phase.ReceiverEffectAdmission is not null
            && codec is { HasPhaseObserver: true })
        {
            await codec.ObservePhaseAsync(request, GrainRequestPhase.RetireOperationSealed, context,
                cancellationToken).ConfigureAwait(true);
        }
        if (phase.Stage == PartitionMovePeerStage.Retire && phase.ReceiverEffectAdmission is not null
            && sealedObserver is not null)
        {
            await sealedObserver.BorrowAsync(operation, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            operation = database.VerifyOperationAuthority(operation);
        }
        return await coordinator.SubmitVerifiedAsync(operation, cancellationToken).ConfigureAwait(true);
    }

    private static ReplicatedOperation Read(DecodedGrainRequest request)
    {
        if (!PartitionMovementRequestScope.Validate(request.Envelope))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var operation = GrainNativePayload.Read<ReplicatedOperation>(request.Payload);
        if (operation.Kind != OperationKind.PartitionMovementPhase
            || operation.Id != request.Envelope.CommandId
            || operation.PrincipalId != request.Envelope.PrincipalId)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return operation;
    }
}
