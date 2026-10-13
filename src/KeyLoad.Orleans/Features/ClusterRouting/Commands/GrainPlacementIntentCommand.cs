using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Orleans;

internal static class GrainPlacementIntentCommand
{
    internal static string Resolve(DecodedGrainRequest request)
        => NativeCommandPayload.Read<PlacementIntentPhaseRequestV1>(Read(request)).ControlPartition.AtomicPartitionId;

    internal static async Task<OperationResult> SubmitAsync(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, PrincipalRecord principal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var operation = database.VerifyOperationAuthority(Read(request));
        if (operation.PrincipalId != principal.Id || !principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
        return await coordinator.SubmitVerifiedAsync(operation, token).ConfigureAwait(true);
    }

    private static ReplicatedOperation Read(DecodedGrainRequest request)
    {
        var envelope = request.Envelope;
        if (envelope.Purpose != PlacementIntentNativeProtocol.Purpose
            || envelope.CommandKind != OperationKind.PlacementIntentPhase || envelope.ReadKind is not null)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var operation = GrainNativePayload.Read<ReplicatedOperation>(request.Payload);
        if (operation.Kind != OperationKind.PlacementIntentPhase || operation.Id != envelope.CommandId
            || operation.PrincipalId != envelope.PrincipalId)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        DatabaseEngine.ValidatePartition(NativeCommandPayload.Read<PlacementIntentPhaseRequestV1>(operation).ControlPartition);
        return operation;
    }
}
