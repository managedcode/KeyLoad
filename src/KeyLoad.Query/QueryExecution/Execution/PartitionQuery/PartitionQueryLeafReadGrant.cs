using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

internal sealed class PartitionQueryLeafReadGrant(ReadExecutionBudgetReadGrant grant, int maximumRecords)
{
    internal int ExaminedRecords => grant.ExaminedRecords;
    internal long ReadBytes => grant.ReadBytes;
    internal ReadExecutionBudgetReadGrant RuntimeGrant => grant;

    internal StorageScanResult VisitRange(IKeyValueView view, byte[] prefix, StorageRecordVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(view);
        var remaining = maximumRecords - grant.ExaminedRecords;
        return StorageRangePages.VisitAll(Math.Max(1, Math.Min(remaining, view.MaximumRangeRecords)),
            (pageRecords, pageVisitor, after) => grant.VisitRange(view, prefix, pageRecords, pageVisitor, after), visitor);
    }

    internal void ReadValue(IKeyValueView view, byte[] key, StorageValueReader reader)
    {
        _ = grant.ReadValue(view, key, reader);
    }
}
