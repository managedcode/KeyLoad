using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class NativeSignedPurposeCold
{
    internal static async Task RequireAsync(TestDatabase fixture, NativeSignedPurposeCut expected,
        EntityRef reference, string json, CancellationToken token)
    {
        for (var cold = 0; cold < 2; cold++)
        {
            token.ThrowIfCancellationRequested();
            using var store = new ZoneTreeStore(new(fixture.Directory), fixture.StorageExecution,
                UnitExecutionOptions.PointCacheExecution());
            await WalFileFixture.AssertPreservedIdentity(fixture.Store.Identity, store.Identity);
            await Assert.That(store.Position).IsEqualTo(expected.Position);
            var rows = store.Read(view => view.Scan([], fixture.Database.Limits.MaxScanRecords));
            await Assert.That(rows.HasMore).IsFalse();
            var actual = rows.Records.Select(static row => Convert.ToHexString(row.Key.Span) + ":"
                + Convert.ToHexString(row.Value.Span)).ToArray();
            await Assert.That(actual).IsEquivalentTo(expected.Rows, TUnit.Assertions.Enums.CollectionOrdering.Matching);
            var database = fixture.CreateConfiguredDatabase(store);
            var document = database.GetDocument("root", reference, cancellationToken: token);
            await Assert.That(document).IsNotNull();
            await Assert.That(document!.Reference).IsEqualTo(reference);
            await Assert.That(document.Json).IsEqualTo(json);
            await Assert.That(document.Revision).IsEqualTo(1L);
        }
    }
}
