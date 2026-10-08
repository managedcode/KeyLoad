using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Queries;

internal static class PartitionMoveFamilyCapture
{
    internal static PartitionMoveImageFamily Capture(IKeyValueView view, PartitionMoveSourceFenceRecord fence,
        string family, ReadExecutionBudget work, PartitionMoveCaptureBudget captureBudget, DatabaseLimits limits, int maximumPageBytes,
        long maximumImageBytes, ImmutableArray<PartitionMoveImagePage>.Builder pages, ref long retained)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPageBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumImageBytes);
        if (maximumPageBytes > limits.MaxBatchBytes || maximumImageBytes > limits.MaxQueryReadBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var before = pages.Count;
        long count = PartitionMoveProtocol.EmptyCount;
        long rawBytes = PartitionMoveProtocol.EmptyCount;
        ReadOnlyMemory<byte> after = default;
        while (true)
        {
            work.Check();
            var remaining = maximumImageBytes - retained;
            var page = PartitionRecordPageReader.Read(view, fence.Partition, family, limits.MaxBatchMutations,
                Math.Min(maximumPageBytes, remaining), limits.MaxQueryReadBytes, captureBudget.Observe,
                after, work.Cancellation);
            foreach (var record in page.Records)
            {
                work.Check();
                PartitionMoveImageDigest.Append(digest, record.Key.Span);
                PartitionMoveImageDigest.Append(digest, record.Value.Span);
                count = checked(count + PartitionMoveProtocol.SequenceStep);
                rawBytes = checked(rawBytes + record.Key.Length + record.Value.Length);
            }
            if (!ControlOwned(family) && !page.Records.IsEmpty)
            {
                var captured = new PartitionMoveImagePage(PartitionMoveProtocol.Version, fence.MoveId,
                    fence.Partition, family, pages.Count, page.Records, PartitionMoveImageDigest.Records(page.Records));
                if (NativeSerialization.Measure(captured) > maximumPageBytes)
                { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
                retained = checked(retained + page.RetainedBytes);
                pages.Add(captured);
            }
            if (!page.HasMore)
            { break; }
            after = page.Continuation ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage);
        }
        return new(family, count, rawBytes, pages.Count - before, Convert.ToHexStringLower(digest.GetHashAndReset()));
    }

    internal static bool ControlOwned(string family)
        => family is PartitionRecordFamilies.OutcomeV2 or PartitionRecordFamilies.OutcomeLocator
            or PartitionRecordFamilies.OutcomeLocatorV2;
}
