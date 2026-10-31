using KeyLoad.Clustering;
using KeyLoad.Core;
using ManagedCode.Communication;

namespace KeyLoad.Server.Search;

internal sealed class TextOnlineCheckpointWork
{
    private const long InitialProjectionSequence = 0;
    private readonly byte[] commandBytes;
    private readonly string principalId;
    private readonly long through;

    internal TextOnlineCheckpointWork(Guid sessionId, CommitProjectionBatchRequest command,
        string principalId, long through, ReadExecutionBudget budget)
    {
        budget.Check();
        if (command.CommandId == Guid.Empty || through < InitialProjectionSequence || command.Effects.IsDefault
            || !command.Effects.IsEmpty)
        {
            throw TextErrors.Mismatch();
        }
        commandBytes = OrleansSerialization.Serialize(command);
        this.principalId = principalId;
        this.through = through;
        Work = new(sessionId, command.CommandId);
    }

    internal OnlineTextPublicationWork Work
    {
        get;
    }

    internal (bool HasValue, OnlineTextPublicationWork? Value) TryMatch(CommitProjectionBatchRequest command,
        string actualPrincipal, ReadExecutionBudget budget)
    {
        if (command.CommandId != Work.CommandId)
        {
            return (false, default(OnlineTextPublicationWork?));
        }
        budget.Check();
        if (actualPrincipal != principalId)
        {
            throw TextErrors.Mismatch();
        }
        if (!commandBytes.AsSpan().SequenceEqual(OrleansSerialization.Serialize(command)))
        {
            throw TextErrors.Mismatch();
        }
        return (true, ((OnlineTextPublicationWork?)(Work)));
    }

    internal async Task<ProjectionBatchResult> RequireAcknowledgedAsync(ProjectionBatchResult acknowledged,
        ReadExecutionBudget budget)
    {
        var original = await Work.RequireOriginal();
        var actual = RequireReceipt(original);
        budget.Check();
        if (!OrleansSerialization.Serialize(actual).AsSpan().SequenceEqual(OrleansSerialization.Serialize(acknowledged)))
        {
            throw TextErrors.Corrupt();
        }
        return actual;
    }

    internal ProjectionBatchResult RequireReceipt(Result<object> original)
    {
        var actual = original.Get<ProjectionBatchResult>();
        if (actual.Receipt is null || actual.Receipt.CommandId != Work.CommandId
            || actual.Checkpoint != through || actual.Receipt.Token is null
            || actual.Receipt.Mutations.IsDefault || !actual.Receipt.Mutations.IsEmpty)
        {
            throw TextErrors.Corrupt();
        }
        return actual;
    }
}
