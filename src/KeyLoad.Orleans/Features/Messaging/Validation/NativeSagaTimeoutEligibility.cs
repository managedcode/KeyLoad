using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Orleans;

internal static class NativeSagaTimeoutEligibility
{
    internal static NativeSagaTimeoutState Read(DatabaseEngine database, DueWorkHint hint, DateTimeOffset now)
        => database.Store.Read(view => Read(view, database, hint, now));

    private static NativeSagaTimeoutState Read(IKeyValueView view, DatabaseEngine database,
        DueWorkHint hint, DateTimeOffset now)
    {
        var record = view.GetRecord<SagaRecord>(RecurringSagaStorage.SagaKey(hint.Lane, hint.Id));
        if (record is null)
        {
            return NativeSagaTimeoutState.Stale;
        }

        if (record.Lane is null || record.CreatorPrincipalId is null || record.SagaId != hint.Id
            || record.Lane != hint.Lane || record.Revision < 1 || !Enum.IsDefined(record.Phase)
            || record.StateJson is null || (record.Deadline is null) != (record.Timeout is null)
            || record.Phase == SagaPhase.TimedOut && record.Deadline is null
            || record.Phase is SagaPhase.Completed or SagaPhase.Cancelled && record.Deadline is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, "Persisted saga state is inconsistent.");
        }

        _ = JsonData.Validate(record.StateJson, database.Limits);
        if (record.Timeout is { } timeout)
        {
            if (timeout.Queue is null || timeout.Queue.Partition != hint.Lane.Partition
                || timeout.Queue.Queue is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, "Persisted saga timeout state is inconsistent.");
            }
            JsonData.Identifier(timeout.Queue.Queue);
        }

        if (record.Revision != hint.Revision || record.Phase != SagaPhase.Waiting
            || record.Deadline is not { } deadline || deadline != hint.DueAt || deadline > now
            || record.Timeout is null || record.CreatorPrincipalId != hint.CreatorPrincipalId)
        {
            return NativeSagaTimeoutState.Stale;
        }

        try
        {
            var creator = database.Principal(view, record.CreatorPrincipalId, now);
            return new NativeSagaTimeoutState(true, record, creator);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Unauthenticated)
        {
            return NativeSagaTimeoutState.Stale;
        }
    }
}

internal readonly record struct NativeSagaTimeoutState(
    bool IsEligible, SagaRecord? Record, PrincipalRecord? Creator)
{
    internal static NativeSagaTimeoutState Stale => new(false, null, null);
}
