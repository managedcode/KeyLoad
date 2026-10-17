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

internal static class SqlInnerJoinCancellationWholeState
{
    private const string AccessPath = "bounded-primary-key-inner-join";
    private const string Customer = "customer";
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const long SeedRevision = 1;

    internal static async Task UnchangedAsync(TestDatabase database, string[] before, long position, string identity)
    {
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(database.Store.Identity))).IsEqualTo(identity);
        await Assert.That(global::KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(database.Store))
            .IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task LiteralAsync(QueryPage page, string[] ids, long position)
    {
        var rows = ids.Select(id => new QueryRow(id, SeedRevision,
            "{\"order_id\":\"" + id + "\",\"name\":\"Ada\"}", Redacted: false, RedactedFields: [],
            Sources: [new(LeftAlias, id, SeedRevision), new(RightAlias, Customer, SeedRevision)])).ToArray();
        var expected = new QueryPage([.. rows], null, position, AccessPath);
        await Assert.That(JsonDefaults.Serialize(page).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
