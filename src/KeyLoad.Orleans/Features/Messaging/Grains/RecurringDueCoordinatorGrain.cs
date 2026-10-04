using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using ManagedCode.Communication.CQRS;
using Orleans.Serialization;

namespace KeyLoad.Orleans;

[global::Orleans.GrainType(DueCoordinatorAliases.Coordinator), global::Orleans.Placement.PreferLocalPlacement]
internal sealed class RecurringDueCoordinatorGrain(GrainRequestCodec codec, DatabaseEngine database,
    ICommitCoordinator coordinator, IServiceProvider services, TimeProvider clock,
    Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> chunkSerializer)
    : Grain, IRecurringDueCoordinatorGrain
{
    public async Task<DueDispatchResult> ProcessDueAsync(DueWorkHint hint, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(DueCoordinatorFields.DispatchDeadline);
        var token = deadline.Token;
        ValidateHint(hint);
        await coordinator.ReadBarrierAsync(token).ConfigureAwait(true);
        var principal = database.Store.Read(view =>
            database.Principal(view, hint.CreatorPrincipalId, clock.GetUtcNow()));
        var commandId = DueWorkCommandIdentity.Create(hint);
        var mutation = RecurringDueCommand.Create(hint);
        var payload = NativeSerialization.Serialize(new CommandRequest(commandId, hint.Lane.Partition, [mutation]));
        var result = await DispatchWithOneUncertaintyRetry(principal, commandId, payload, token).ConfigureAwait(true);
        return new(result.Error);
    }

    private async Task<GrainOperationReply> DispatchWithOneUncertaintyRetry(PrincipalRecord principal,
        Guid commandId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var first = await Dispatch(principal, requestId, commandId, payload, cancellationToken).ConfigureAwait(true);
        if (first.Error != ErrorCode.UnknownWriteOutcome)
        {
            return first;
        }
        cancellationToken.ThrowIfCancellationRequested();
        requestId = Guid.NewGuid();
        return await Dispatch(principal, requestId, commandId, payload, cancellationToken).ConfigureAwait(true);
    }

    private async Task<GrainOperationReply> Dispatch(PrincipalRecord principal, Guid requestId,
        Guid commandId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        using var identity = new GrainRequestIdentityScope(services, principal, requestId, commandId, cancellationToken);
        var signed = codec.CreateCommand(requestId, principal.Id, OperationKind.Batch, commandId, payload);
        var request = GrainFactory.GetGrain<IRequestGrain>(requestId);
        return await GrainRequestStreamConsumer.DrainAsync(
            token => request.ExecuteStreamAsync(signed, token), chunkSerializer, requestId, clock,
            cancellationToken).ConfigureAwait(true);
    }

    private void ValidateHint(DueWorkHint hint)
    {
        if (hint is null || hint.Lane is null || hint.Lane.Partition is null
            || hint.Lane.Partition.AtomicPartitionId != this.GetPrimaryKeyString()
            || hint.Id == Guid.Empty || string.IsNullOrWhiteSpace(hint.CreatorPrincipalId)
            || hint.Revision < 1 || hint.Generation < 0 || hint.Ordinal < 0
            || !Enum.IsDefined(hint.Kind) || hint.Kind == DueWorkKind.Schedule && hint.Generation < 1
            || hint.Kind == DueWorkKind.Saga && hint.Generation != 0)
        {
            throw Errors.Fail(ErrorCode.Validation, DueCoordinatorFields.InvalidHint);
        }
        DatabaseEngine.ValidatePartition(hint.Lane.Partition);
        JsonData.Identifier(hint.Lane.Queue);
        JsonData.Identifier(hint.CreatorPrincipalId);
    }
}
