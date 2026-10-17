using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Queries;

internal static class TenantPlacementByteAccounting
{
    private const long EmptyBytes = 0;
    private const int MinimumScanRecords = 1;

    internal static long Read(IKeyValueView charged, PartitionRef partition,
        ReadExecutionBudgetReadGrant grant, CancellationToken token)
    {
        DatabaseEngine.ValidatePartition(partition);
        var total = EmptyBytes;
        foreach (var family in PartitionRecordFamilies.All)
        {
            token.ThrowIfCancellationRequested();
            var prefix = KeySpace.Partition(family, partition);
            var readBefore = grant.ReadBytes;
            var recordsBefore = grant.ExaminedRecords;
            var page = charged.VisitRange(prefix, Math.Max(MinimumScanRecords, grant.RemainingRecords),
                (key, value) => { total = checked(total + key.Length + value.Length); return true; },
                cancellationToken: token);
            if (page.ReadBytes != grant.ReadBytes - readBefore || page.Records != grant.ExaminedRecords - recordsBefore)
            {
                throw Errors.Fail(ErrorCode.Corruption, TenantPlacementCapacityProtocol.Incomplete);
            }
            if (page.HasMore || page.StoppedByVisitor)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ReadExecutionBudget.ReadBytesExceeded);
            }
        }
        return total;
    }
}
