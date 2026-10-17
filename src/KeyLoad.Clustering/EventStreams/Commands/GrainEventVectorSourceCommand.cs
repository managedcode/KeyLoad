using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Clustering;

internal static class GrainEventVectorSourceCommand
{
    internal static string Resolve(DecodedGrainRequest request)
    {
        var operation = Read(request);
        var source = operation.Kind == OperationKind.EventFeedControl
            ? NativeCommandPayload.Read<EventVectorControlPhase>(operation).OriginalRequest.ControlPartition
            : NativeCommandPayload.Read<EventVectorSourceMutation>(operation).Source.Partition;
        ArgumentNullException.ThrowIfNull(source);
        JsonData.Identifier(source.TenantId);
        JsonData.Identifier(source.DatabaseId);
        JsonData.Identifier(source.TransactionDomainId);
        JsonData.Identifier(source.PartitionKey);
        return source.AtomicPartitionId;
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
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return coordinator.SubmitVerifiedAsync(operation, cancellationToken);
    }

    private static ReplicatedOperation Read(DecodedGrainRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.Envelope);
        if (!EventVectorRequestScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var operation = GrainNativePayload.Read<ReplicatedOperation>(request.Payload);
        if (operation.Kind is not (OperationKind.EventFeedSourcePhase or OperationKind.EventFeedControl) || operation.Id != request.Envelope.CommandId
            || operation.PrincipalId != request.Envelope.PrincipalId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return operation;
    }
}
