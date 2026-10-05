using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class PartitionQueryLeafReadGrant(ReadExecutionBudgetReadGrant grant, int maximumRecords)
{
    internal int ExaminedRecords => grant.ExaminedRecords;
    internal long ReadBytes => grant.ReadBytes;
    internal ReadExecutionBudgetReadGrant NativeGrant => grant;

    internal StorageScanResult VisitRange(IKeyValueView view, byte[] prefix, StorageRecordVisitor visitor)
    {
        var remaining = maximumRecords - grant.ExaminedRecords;
        return grant.VisitRange(view, prefix, Math.Max(1, remaining), visitor);
    }

    internal void ReadValue(IKeyValueView view, byte[] key, StorageValueReader reader)
    {
        _ = grant.ReadValue(view, key, reader);
    }
}
