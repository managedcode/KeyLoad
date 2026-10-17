using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.StorageRecovery.NativeCapturedReadScopeScenario;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class LiveRangeCancellationCallbacks(CancellationTokenSource selected, bool staged,
    bool reverse, LiveRangeCancellationPhase phase)
{
    private const int NoCallbacks = 0;
    private const int FirstCallback = 1;
    private const int StagedCallback = 2;
    private const string StagedFirst = "staged-first-not-published";
    private const string StagedSecond = "staged-second-not-published";
    private const string UnexpectedRow = "The live range did not deliver the independently expected ordered record.";
    private int delivered;
    private int observed;

    internal static void Stage(IAtomicTransaction tx)
    {
        tx.Put(NativeReadCutFixture.Key(First), NativeReadCutFixture.Value(StagedFirst));
        tx.Put(NativeReadCutFixture.Key(Second), NativeReadCutFixture.Value(StagedSecond));
        tx.Delete(NativeReadCutFixture.Key(Point));
    }

    internal StorageScanResult Visit(IKeyValueView view)
        => reverse
            ? view.VisitReverseRange(NativeReadCutFixture.Key(Group), MaximumRecords,
                Deliver, observer: Observe, cancellationToken: selected.Token)
            : view.VisitRange(NativeReadCutFixture.Key(Group), MaximumRecords,
                Deliver, observer: Observe, cancellationToken: selected.Token);

    private void Observe(long bytes)
    {
        observed++;
        var cancel = phase == LiveRangeCancellationPhase.BaselineObserver && observed == FirstCallback
            || phase == LiveRangeCancellationPhase.StagedObserver && observed == StagedCallback;
        if (cancel)
        {
            selected.Cancel();
        }
    }

    private bool Deliver(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        var expectedKey = NativeReadCutFixture.Key(reverse ? Second : First);
        var text = staged ? reverse ? StagedSecond : StagedFirst : reverse ? OldSecond : OldFirst;
        if (!key.SequenceEqual(expectedKey) || !value.SequenceEqual(NativeReadCutFixture.Value(text)))
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
        var expectedDelivered = visitorSelected ? FirstCallback : NoCallbacks;
        var expectedObserved = phase == LiveRangeCancellationPhase.Before ? NoCallbacks
            : phase == LiveRangeCancellationPhase.BaselineObserver ? FirstCallback
            : staged ? StagedCallback : FirstCallback;
        await Assert.That(delivered).IsEqualTo(expectedDelivered);
        await Assert.That(observed).IsEqualTo(expectedObserved);
    }
}
