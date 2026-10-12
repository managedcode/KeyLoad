using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Orleans;

internal static class GrainBm25CorpusPublicationCommand
{
    internal static string Resolve(DecodedGrainRequest request)
    {
        var phase = NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(Read(request));
        var partition = phase.Current.Partition;
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
        if (!Bm25RequestScope.Validate(request.Envelope))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var operation = GrainNativePayload.Read<ReplicatedOperation>(request.Payload);
        if (operation.Kind != OperationKind.Bm25CorpusPublicationPhase
            || operation.Kind != request.Envelope.CommandKind || operation.Id != request.Envelope.CommandId
            || operation.PrincipalId != request.Envelope.PrincipalId)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var phase = NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(operation);
        if (phase.CommandId != operation.Id || phase.Current is null || phase.Current.Partition is null)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return operation;
    }
}
