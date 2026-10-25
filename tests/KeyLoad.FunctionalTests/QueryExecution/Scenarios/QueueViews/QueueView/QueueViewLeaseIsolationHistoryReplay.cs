using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueueViewLeaseIsolationHistoryReplay
{
    internal static async Task RunAsync(QueueViewLeaseIsolationState state)
    {
        foreach (var retained in state.History)
        {
            await OriginalAsync(state, retained);
            var operation = QueueViewLeaseIsolationOperations.Original(retained);
            await CurrentAsync(state, retained, () =>
                state.Owner.Database.ApplyStorageOperation(operation, retained.Entry.CanonicalSequence,
                    state.Token).Result);
            await CurrentAsync(state, retained, () => state.Owner.Submit(operation));
            await CurrentAsync(state, retained, () => state.Owner.Database.ResolveOutcome(operation));
            await OriginalAsync(state, retained);
        }
    }

    private static Task CurrentAsync(QueueViewLeaseIsolationState state, QueueViewLeaseIsolationHistory retained,
        Func<Result<object>> operation)
        => QueueViewLeaseIsolationReads.ObserveAsync(state, async () =>
        {
            var actual = operation();
            if (retained == state.Claim && state.Acked)
            {
                await QueueViewLeaseIsolationResults.FailureAsync(actual, ErrorCode.StaleLease);
            }
            else
            {
                await QueueViewLeaseIsolationResults.SameAsync(actual, retained.Result);
            }
        });

    private static async Task OriginalAsync(QueueViewLeaseIsolationState state, QueueViewLeaseIsolationHistory retained)
    {
        await QueueViewLeaseIsolationOperations.BytesAsync(state.Owner.Store.Read(view => view.ReadOwnedValue(retained.OutcomeKey)), retained.Outcome);
        await QueueViewLeaseIsolationOperations.BytesAsync(state.Owner.Store.Read(view => view.ReadOwnedValue(retained.LocatorKey)), retained.Locator);
        var entry = state.Owner.Entry(retained.Operation.CommandId);
        await Assert.That(entry.CanonicalSequence).IsEqualTo(retained.Entry.CanonicalSequence);
        var expected = QueueViewLeaseIsolationOperations.Original(retained);
        var actual = entry.PreparedCommand;
        await Assert.That(actual.CommandId == expected.CommandId && actual.Value!.Kind == expected.Value!.Kind
            && actual.UserId == expected.UserId && actual.Timestamp == expected.Timestamp).IsTrue();
        await Assert.That(actual.Value!.Input.Span.SequenceEqual(expected.Value!.Input.Span)).IsTrue();
        await Assert.That(actual.Value.Fingerprint).IsEqualTo(expected.Value.Fingerprint);
        var stored = OrleansSerialization.Deserialize<StoredOutcome>(retained.Outcome);
        await QueueViewLeaseIsolationResults.SameAsync(stored.Result, retained.Result);
    }
}
