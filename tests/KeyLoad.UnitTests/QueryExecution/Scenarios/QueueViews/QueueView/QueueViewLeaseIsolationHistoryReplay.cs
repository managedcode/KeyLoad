using ManagedCode.Communication;
using KeyLoad.Core;
using static KeyLoad.UnitTests.Features.QueryExecution.QueueViewLeaseIsolationProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationHistoryReplay
{
    internal static async Task RunAsync(QueueViewLeaseIsolationState state)
    {
        foreach (var sample in state.History)
        {
            await OriginalAsync(state, sample);
            var operation = QueueViewLeaseIsolationOperations.Original(sample);
            await QueueViewLeaseIsolationReads.ObserveAsync(state, async () =>
                await QueueViewLeaseIsolationResults.SameAsync(
                    state.Owner.Database.ApplyStorageOperation(operation, sample.Entry.CanonicalSequence,
                        state.Token).Result, sample.Result));
            await CurrentAsync(state, sample, () => state.Owner.Submit(operation));
            await CurrentAsync(state, sample, () => state.Owner.Database.ResolveOutcome(operation));
            await OriginalAsync(state, sample);
        }
    }

    private static Task CurrentAsync(QueueViewLeaseIsolationState state, QueueViewLeaseIsolationHistory sample,
        Func<Result<object>> operation)
        => QueueViewLeaseIsolationReads.ObserveAsync(state, async () =>
        {
            var actual = operation();
            if (sample == state.Claim && state.Acked)
            {
                await QueueViewLeaseIsolationResults.FailureAsync(actual, ErrorCode.StaleLease);
            }
            else
            {
                await QueueViewLeaseIsolationResults.SameAsync(actual, sample.Result);
            }
        });

    private static async Task OriginalAsync(QueueViewLeaseIsolationState state, QueueViewLeaseIsolationHistory sample)
    {
        await QueueViewLeaseIsolationOperations.BytesAsync(state.Owner.Store.Read(view => view.ReadOwnedValue(sample.OutcomeKey)), sample.Outcome);
        await QueueViewLeaseIsolationOperations.BytesAsync(state.Owner.Store.Read(view => view.ReadOwnedValue(sample.LocatorKey)), sample.Locator);
        var entry = state.Owner.Entry(sample.Operation.CommandId);
        await Assert.That(entry.CanonicalSequence).IsEqualTo(sample.Entry.CanonicalSequence);
        var expected = QueueViewLeaseIsolationOperations.Original(sample);
        var actual = entry.PreparedCommand;
        await Assert.That(actual.CommandId == expected.CommandId && actual.Value!.Kind == expected.Value!.Kind
            && actual.UserId == expected.UserId && actual.Timestamp == expected.Timestamp).IsTrue();
        await Assert.That(actual.Value!.Input.Span.SequenceEqual(expected.Value!.Input.Span)).IsTrue();
        await Assert.That(actual.Value.Fingerprint).IsEqualTo(expected.Value.Fingerprint);
        var stored = NativeSerialization.Deserialize<StoredOutcome>(sample.Outcome);
        await QueueViewLeaseIsolationResults.SameAsync(stored.Result, sample.Result);
    }
}
