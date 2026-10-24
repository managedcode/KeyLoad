using KeyLoad.Server;
using KeyLoad.UnitTests.OrleansExecution;

namespace KeyLoad.UnitTests.StorageRecovery;

internal static class IssuedPurposeCold
{
    internal static async Task RequireAsync(ConnectionColdSource fixture, IssuedPurposeCut expected,
        EntityRef reference, string json, CancellationToken token)
    {
        for (var cold = 0; cold < 2; cold++)
        {
            var failures = new List<Exception>();
            PartitionStores? stores = null;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                stores = fixture.OpenStores();
                var store = stores.Canonical;
                await WalFileFixture.AssertPreservedIdentity(fixture.Identity, store.Identity);
                await Assert.That(store.Position).IsEqualTo(expected.Position);
                var rows = store.Read(view => view.Scan([], fixture.Limits.MaxScanRecords));
                await Assert.That(rows.HasMore).IsFalse();
                var actual = rows.Records.Select(static row => Convert.ToHexString(row.Key.Span) + ":"
                    + Convert.ToHexString(row.Value.Span)).ToArray();
                await Assert.That(actual).IsEquivalentTo(expected.Rows, TUnit.Assertions.Enums.CollectionOrdering.Matching);
                var database = fixture.CreateDatabase(store);
                var document = database.GetDocument("root", reference, cancellationToken: token);
                await Assert.That(document).IsNotNull();
                await Assert.That(document!.Reference).IsEqualTo(reference);
                await Assert.That(document.Json).IsEqualTo(json);
                await Assert.That(document.Revision).IsEqualTo(1L);
            }, failures);
            if (stores is { } acquired)
            {
                ServerFailureObserver.Observe(acquired.Dispose, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
