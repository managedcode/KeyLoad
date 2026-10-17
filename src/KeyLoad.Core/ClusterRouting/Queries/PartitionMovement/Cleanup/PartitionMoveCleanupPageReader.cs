using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Queries;

internal static class PartitionMoveCleanupPageReader
{
    internal static ScanPage Read(IKeyValueView view, byte[] prefix, int maximumRecords,
        long maximumRetainedBytes, long maximumExaminedBytes)
    {
        var work = new PartitionMoveCleanupReadBudget(maximumRetainedBytes, maximumExaminedBytes);
        var records = ImmutableArray.CreateBuilder<KeyValueRecord>();
        var actual = view.VisitRange(prefix, maximumRecords, (key, value) =>
        {
            work.Retain(checked((long)key.Length + value.Length));
            records.Add(new(key.ToArray(), value.ToArray()));
            return true;
        }, observer: work.Examine);
        if (actual.StoppedByVisitor || actual.Records != records.Count || actual.ReadBytes != work.ExaminedBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.InvalidImage);
        }
        return new(records.ToImmutable(), actual.HasMore);
    }
}
