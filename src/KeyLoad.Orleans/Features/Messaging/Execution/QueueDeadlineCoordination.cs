using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class QueueDeadlineCoordination
{
    private const string UnavailablePrincipal = "Queue deadline coordination has no configured principal.";

    internal static async Task<DueDispatchResult> ExecuteAsync(QueueDeadlineHint hint, string partitionId,
        DatabaseEngine database, ICommitCoordinator coordinator, TimeProvider clock,
        IOptions<DueCoordinationOptions> options, RuntimeJournalAdmission admission,
        Func<PrincipalRecord, Guid, ReadOnlyMemory<byte>, CancellationToken, Task<GrainOperationReply>> dispatch,
        CancellationToken cancellationToken)
    {
        if (hint is null || hint.Lane?.Partition is null || hint.Mutation is null
            || hint.Lane.Partition.AtomicPartitionId != partitionId || hint.Mutation.Queue != hint.Lane.Queue)
        { throw Errors.Fail(ErrorCode.Validation, DueCoordinatorFields.InvalidHint); }
        DatabaseEngine.ValidatePartition(hint.Lane.Partition);
        JsonData.Identifier(hint.Lane.Queue);
        var subject = options.Value.QueueDeadlinePrincipalId
            ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, UnavailablePrincipal);
        JsonData.Identifier(subject);
        using var timeout = new CancellationTokenSource(options.Value.DispatchDeadline, clock);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            admission.SchedulingToken, timeout.Token);
        await coordinator.ReadBarrierAsync(lifetime.Token).ConfigureAwait(true);
        var principal = database.Store.Read(view => database.Principal(view, subject, clock.GetUtcNow()));
        var commandId = Guid.NewGuid();
        var body = NativeSerialization.Serialize(new CommandRequest(commandId, hint.Lane.Partition, [hint.Mutation]));
        var reply = await dispatch(principal, commandId, body, lifetime.Token).ConfigureAwait(true);
        return new(reply.Error);
    }
}
