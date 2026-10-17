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
                    state.Owner.Database.Apply(operation, sample.Entry.Index), sample.Result));
            await CurrentAsync(state, sample, () => state.Owner.Submit(operation));
            await CurrentAsync(state, sample, () => state.Owner.Database.ResolveOutcome(operation));
            await OriginalAsync(state, sample);
        }
    }

    private static Task CurrentAsync(QueueViewLeaseIsolationState state, QueueViewLeaseIsolationHistory sample,
        Func<OperationResult> operation)
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
        var entry = state.Owner.Entry(sample.Operation.Id);
        await Assert.That(entry.Index == sample.Entry.Index && entry.Term == sample.Entry.Term).IsTrue();
        var expected = QueueViewLeaseIsolationOperations.Original(sample);
        var actual = entry.Operation ?? throw new InvalidOperationException(Absent);
        await Assert.That(actual.Id == expected.Id && actual.Kind == expected.Kind
            && actual.PrincipalId == expected.PrincipalId && actual.EvaluatedAt == expected.EvaluatedAt
            && actual.PayloadJson == expected.PayloadJson).IsTrue();
        await Assert.That(actual.NativePayload.Span.SequenceEqual(expected.NativePayload.Span)).IsTrue();
        var stored = NativeSerialization.Deserialize<StoredOutcome>(sample.Outcome);
        await QueueViewLeaseIsolationResults.SameAsync(stored.Result, sample.Result);
    }
}
