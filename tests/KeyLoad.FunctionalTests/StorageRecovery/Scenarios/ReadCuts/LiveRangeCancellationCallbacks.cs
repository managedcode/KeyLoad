using KeyLoad.Storage;
using static KeyLoad.FunctionalTests.StorageRecovery.CapturedReadScopeScenario;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class LiveRangeCancellationCallbacks(CancellationTokenSource selected, bool staged,
    bool reverse, LiveRangeCancellationPhase phase)
{
    private const int StagedCallback = 2;
    private const string StagedFirst = "staged-first-not-published";
    private const string StagedSecond = "staged-second-not-published";
    private const string UnexpectedRow = "The live range did not deliver the independently expected ordered record.";
    private int delivered;
    private int observed;

    internal static void Stage(IAtomicTransaction tx)
    {
        tx.Put(ReadCutFixture.Key(First), ReadCutFixture.Value(StagedFirst));
        tx.Put(ReadCutFixture.Key(Second), ReadCutFixture.Value(StagedSecond));
        tx.Delete(ReadCutFixture.Key(Point));
    }

    internal StorageScanResult Visit(IKeyValueView view)
        => reverse
            ? view.VisitReverseRange(ReadCutFixture.Key(Group), MaximumRecords,
                Deliver, observer: Observe, cancellationToken: selected.Token)
            : view.VisitRange(ReadCutFixture.Key(Group), MaximumRecords,
                Deliver, observer: Observe, cancellationToken: selected.Token);

    private void Observe(long bytes)
    {
        observed++;
        var cancel = phase == LiveRangeCancellationPhase.BaselineObserver && observed == 1
            || phase == LiveRangeCancellationPhase.StagedObserver && observed == StagedCallback;
        if (cancel)
        {
            selected.Cancel();
        }
    }

    private bool Deliver(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var expectedKey = ReadCutFixture.Key(reverse ? Second : First);
        var text = staged ? reverse ? StagedSecond : StagedFirst : reverse ? OldSecond : OldFirst;
        if (!key.SequenceEqual(expectedKey) || !value.SequenceEqual(ReadCutFixture.Value(text)))
        {
            throw new InvalidDataException(UnexpectedRow);
        }
        delivered++;
        selected.Cancel();
        return phase != LiveRangeCancellationPhase.VisitorStop;
    }

    internal async Task RequireCountsAsync()
    {
        var visitorSelected = phase is LiveRangeCancellationPhase.VisitorStop or LiveRangeCancellationPhase.VisitorContinue;
        var expectedDelivered = visitorSelected ? 1 : 0;
        var expectedObserved = phase == LiveRangeCancellationPhase.Before ? 0
            : phase == LiveRangeCancellationPhase.BaselineObserver ? 1
            : staged ? StagedCallback : 1;
        await Assert.That(delivered).IsEqualTo(expectedDelivered);
        await Assert.That(observed).IsEqualTo(expectedObserved);
    }
}
