using KeyLoad.Core.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlInnerJoinNativeReadMeasurement
{
    internal static NativeReadBytes Measure(TestDatabase database, string leftCollection, string rightCollection,
        string firstCustomer, string missingCustomer, string lastCustomer)
    {
        long leftScanBytes = 0;
        long rightProbeBytes = 0;
        var leftRows = 0;
        var rightProbes = 0;
        var foundRightProbes = 0;
        var hasMore = false;
        database.Store.Read(view =>
        {
            var scan = view.VisitRange(DocumentStorageKeys.Prefix(database.Partition, leftCollection),
                database.Database.Limits.MaxScanRecords, (_, _) =>
                {
                    leftRows++;
                    return true;
                }, observer: bytes => leftScanBytes = checked(leftScanBytes + bytes));
            hasMore = scan.HasMore;
            foreach (var customerId in new[] { firstCustomer, missingCustomer, lastCustomer })
            {
                var found = view.ReadValue(DocumentStorageKeys.RecordKey(database.Partition, rightCollection, customerId),
                    static _ => { }, bytes => rightProbeBytes = checked(rightProbeBytes + bytes));
                rightProbes++;
                if (found)
                {
                    foundRightProbes++;
                }
            }
            return true;
        });
        return new(leftScanBytes, rightProbeBytes, leftRows, rightProbes, foundRightProbes, hasMore);
    }

    internal sealed record NativeReadBytes(long LeftScanBytes, long RightProbeBytes, int LeftRows,
        int RightProbes, int FoundRightProbes, bool HasMore);
}
