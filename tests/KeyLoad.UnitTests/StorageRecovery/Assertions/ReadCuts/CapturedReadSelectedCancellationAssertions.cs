using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.UnitTests.StorageRecovery.CapturedReadScopeScenario;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class CapturedReadSelectedCancellationAssertions
{
    internal static async Task BeforeAsync(ZoneTreeCapturedReadView frozen, bool reverse)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var delivered = 0;
        var observed = 0;
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => Visit(frozen, reverse,
            (_, _) =>
            {
                delivered++;
                return true;
            }, _ => observed++, cancellation.Token));
        await RequireAsync(failure, delivered, observed, expectedDelivered: 0, expectedObserved: 0, token: cancellation.Token);
    }

    internal static async Task ObserverAsync(ZoneTreeCapturedReadView frozen, bool reverse)
    {
        using var cancellation = new CancellationTokenSource();
        var delivered = 0;
        var observed = 0;
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => Visit(frozen, reverse,
            (_, _) =>
            {
                delivered++;
                return true;
            }, _ =>
            {
                observed++;
                cancellation.Cancel();
            }, cancellation.Token));
        await RequireAsync(failure, delivered, observed, expectedDelivered: 0, expectedObserved: 1, token: cancellation.Token);
    }

    internal static async Task VisitorAsync(ZoneTreeCapturedReadView frozen, bool reverse, bool keepGoing)
    {
        using var cancellation = new CancellationTokenSource();
        var delivered = 0;
        var observed = 0;
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => Visit(frozen, reverse,
            (key, value) =>
            {
                var expectedKey = ReadCutFixture.Key(reverse ? Second : First);
                var expectedValue = ReadCutFixture.Value(reverse ? OldSecond : OldFirst);
                if (!key.SequenceEqual(expectedKey) || !value.SequenceEqual(expectedValue))
                {
                    throw new InvalidDataException(UnexpectedOriginalRow);
                }
                delivered++;
                cancellation.Cancel();
                return keepGoing;
            }, _ => observed++, cancellation.Token));
        await RequireAsync(failure, delivered, observed, expectedDelivered: 1, expectedObserved: 1, token: cancellation.Token);
    }

    private const string UnexpectedOriginalRow = "The frozen traversal did not deliver its original ordered row.";

    private static StorageScanResult Visit(ZoneTreeCapturedReadView frozen, bool reverse,
        StorageRecordVisitor visitor, StorageReadObserver observer, CancellationToken token)
        => reverse
            ? frozen.VisitReverseRange(ReadCutFixture.Key(Group), MaximumRecords, visitor,
                observer: observer, cancellationToken: token)
            : frozen.VisitRange(ReadCutFixture.Key(Group), MaximumRecords, visitor,
                observer: observer, cancellationToken: token);

    private static async Task RequireAsync(OperationCanceledException failure, int delivered, int observed,
        int expectedDelivered, int expectedObserved, CancellationToken token)
    {
        await Assert.That(failure.CancellationToken).IsEqualTo(token);
        await Assert.That(token.IsCancellationRequested).IsTrue();
        await Assert.That(delivered).IsEqualTo(expectedDelivered);
        await Assert.That(observed).IsEqualTo(expectedObserved);
    }
}
