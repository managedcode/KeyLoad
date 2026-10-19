using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class PartitionRecordOwnedCutAssertions
{
    internal const int MaximumImageRecords = 5;
    internal const long MaximumImageBytes = 4096;
    internal const int LimitedRecords = 2;
    internal const int Second = 1;
    internal const int NoRecords = 0;
    internal const int ColdCuts = 2;
    internal const int FirstCold = 0;

    internal static (byte[] Key, byte[] Value)[] Image(ZoneTreeStore store, CancellationToken token)
        => store.Read(view =>
        {
            var rows = new List<(byte[] Key, byte[] Value)>();
            long examined = 0;
            var range = view.VisitRange([], MaximumImageRecords, (key, value) =>
            {
                rows.Add((key.ToArray(), value.ToArray()));
                return true;
            }, observer: bytes =>
            {
                token.ThrowIfCancellationRequested();
                if (bytes < NoRecords || bytes > MaximumImageBytes - examined)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, "The bounded execution fixture image is exceeded.");
                }
                examined = checked(examined + bytes);
            }, cancellationToken: token);
            if (range.HasMore || range.StoppedByVisitor || range.Records != rows.Count
                || range.ReadBytes != examined)
            {
                throw Errors.Fail(ErrorCode.Corruption, "The bounded execution fixture image is incomplete.");
            }
            return rows.ToArray();
        });

    internal static async Task ImageAsync(ZoneTreeStore store, (byte[] Key, byte[] Value)[] expected,
        long position, CancellationToken token)
    {
        await Assert.That(store.Position).IsEqualTo(position);
        var actual = Image(store, token);
        var ordered = expected.OrderBy(row => Convert.ToHexString(row.Key), StringComparer.Ordinal).ToArray();
        await Assert.That(actual.Length).IsEqualTo(ordered.Length);
        await Assert.That(actual.Zip(ordered).All(pair => pair.First.Key.AsSpan().SequenceEqual(pair.Second.Key)
            && pair.First.Value.AsSpan().SequenceEqual(pair.Second.Value))).IsTrue();
        await Assert.That(store.Position).IsEqualTo(position);
    }

    internal static async Task FullAsync(ZoneTreeStore store, PartitionRef partition, string family,
        (byte[] Key, byte[] Value)[] rows, long position, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await PartitionRecordCancellationStateAssertions.VerifyHealthyAsync(store, partition, family,
            rows, position, MaximumImageBytes);
        token.ThrowIfCancellationRequested();
    }

    internal static async Task RetainedSecondAsync(PartitionRecordPage page, (byte[] Key, byte[] Value)[] original)
    {
        await Assert.That(page.Records[Second].Key.Span.SequenceEqual(original[Second].Key)).IsTrue();
        await Assert.That(page.Records[Second].Value.Span.SequenceEqual(original[Second].Value)).IsTrue();
    }

    internal static async Task HeldAsync(PartitionRecordPage page, (byte[] Key, byte[] Value)[] original)
    {
        await Assert.That(page.Records.Length).IsEqualTo(LimitedRecords);
        await Assert.That(page.Records.Zip(original.Take(LimitedRecords)).All(pair =>
            pair.First.Key.Span.SequenceEqual(pair.Second.Key)
            && pair.First.Value.Span.SequenceEqual(pair.Second.Value))).IsTrue();
        await Assert.That(page.HasMore).IsTrue();
        await Assert.That(page.Continuation.HasValue).IsTrue();
        await Assert.That(page.Continuation!.Value.Span.SequenceEqual(original[Second].Key)).IsTrue();
    }
}
