using System.Text.Json;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class CatalogArchiveRetryCold
{
    private const string HealthySpace = "retained-archive";
    private const string HealthyKey = "healthy";
    private const string HealthyValue = "original-cut-restored";
    private const int ColdOwners = 2;

    internal static async Task RunAsync(EventingArtifactFixture fixture, string target, long restoredPosition,
        string[] domains, byte[] outcome, CancellationToken token)
    {
        var store = fixture.Target!;
        var identity = JsonSerializer.Serialize(store.Identity, JsonDefaults.Options);
        await Assert.That(store.Position).IsEqualTo(restoredPosition);
        var before = EventingArtifactState.FullBytes(store);
        var key = KeyCodec.Encode(HealthySpace, HealthyKey);
        var value = NativeSerialization.Serialize(HealthyValue);
        store.Commit((transaction, _) => { transaction.PutRecord(key, HealthyValue); return true; });
        var expected = before.Append(Convert.ToHexString(key) + ":" + Convert.ToHexString(value))
            .Order(StringComparer.Ordinal).ToArray();
        for (var cold = 0; cold <= ColdOwners; cold++)
        {
            token.ThrowIfCancellationRequested();
            if (cold != 0)
            {
                fixture.ReopenTarget(target);
            }
            var current = fixture.Target!;
            await Assert.That(current.Position).IsEqualTo(restoredPosition + 1L);
            await Assert.That(JsonSerializer.Serialize(current.Identity, JsonDefaults.Options)).IsEqualTo(identity);
            await Assert.That(current.Identity.DispatchPaused).IsTrue();
            await Assert.That(EventingArtifactState.FullBytes(current))
                .IsEquivalentTo(expected, CollectionOrdering.Matching);
            await EventingArtifactState.SameAsync(domains, current, fixture.Source.Partition);
            await EventingArtifactState.AssertSeedAsync(fixture, fixture.Database);
            await Assert.That(fixture.Database.GetDocument(EventingArtifactFixture.Principal, new(fixture.Source.Partition, EventingArtifactFixture.Documents, CatalogArchiveRetryAssertions.LaterId), cancellationToken: token))
                .IsNull();
            await Assert.That(BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(current, fixture.Source.Partition,
                EventingArtifactFixture.Principal, fixture.Original.Id)).IsEquivalentTo(outcome, CollectionOrdering.Matching);
        }
    }
}
