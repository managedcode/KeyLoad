using KeyLoad.Core;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineCheckpointWork
{
    private const long InitialProjectionSequence = 0;
    private readonly byte[] commandBytes;
    private readonly string principalId;
    private readonly DateTimeOffset expiry;
    private readonly long through;

    internal NativeTextOnlineCheckpointWork(Guid sessionId, CommitProjectionBatchRequest command,
        string principalId, DateTimeOffset originalExpiry, long through, ReadExecutionBudget budget)
    {
        budget.Check();
        if (command.CommandId == Guid.Empty || through < InitialProjectionSequence || command.Effects.IsDefault
            || !command.Effects.IsEmpty)
        { throw NativeTextErrors.Mismatch(); }
        budget.ChargeBytes(NativeSerialization.Measure(command));
        commandBytes = NativeSerialization.Serialize(command);
        this.principalId = principalId;
        expiry = originalExpiry;
        this.through = through;
        Work = new(sessionId, command.CommandId);
    }

    internal OnlineTextPublicationWork Work { get; }

    internal OnlineTextPublicationWork? TryMatch(CommitProjectionBatchRequest command,
        string actualPrincipal, DateTimeOffset actualExpiry, ReadExecutionBudget budget)
    {
        if (command.CommandId != Work.CommandId)
        { return null; }
        budget.Check();
        if (actualPrincipal != principalId || actualExpiry != expiry)
        { throw NativeTextErrors.Mismatch(); }
        budget.ChargeBytes(NativeSerialization.Measure(command));
        if (!commandBytes.AsSpan().SequenceEqual(NativeSerialization.Serialize(command)))
        { throw NativeTextErrors.Mismatch(); }
        return Work;
    }

    internal async Task<ProjectionBatchResult> RequireAcknowledgedAsync(ProjectionBatchResult acknowledged,
        ReadExecutionBudget budget)
    {
        var original = await Work.RequireOriginal().ConfigureAwait(false);
        var actual = RequireReceipt(original);
        budget.Check();
        budget.ChargeBytes(NativeSerialization.Measure(actual));
        budget.ChargeBytes(NativeSerialization.Measure(acknowledged));
        if (!NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(acknowledged)))
        { throw NativeTextErrors.Corrupt(); }
        return actual;
    }

    internal ProjectionBatchResult RequireReceipt(OperationResult original)
    {
        var actual = original.Get<ProjectionBatchResult>();
        if (actual.Receipt is null || actual.Receipt.CommandId != Work.CommandId
            || actual.Checkpoint != through || actual.Receipt.Token is null
            || actual.Receipt.Mutations.IsDefault || !actual.Receipt.Mutations.IsEmpty)
        { throw NativeTextErrors.Corrupt(); }
        return actual;
    }
}
