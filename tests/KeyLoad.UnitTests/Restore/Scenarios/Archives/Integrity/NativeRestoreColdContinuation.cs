using System.Text.Json;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class NativeRestoreColdContinuation
{
    private const string HealthyNamespace = "backup-cancel";
    private const string HealthyKey = "healthy";
    private const string HealthyValue = "same-path-restored";
    private const int ColdOwners = 2;

    internal static async Task RunAsync(EventingArtifactFixture fixture, string target,
        long restoredPosition, CancellationToken token)
    {
        var store = fixture.Target!;
        var identity = JsonSerializer.Serialize(store.Identity, JsonDefaults.Options);
        var before = EventingArtifactState.FullBytes(store);
        var key = KeyCodec.Encode(HealthyNamespace, HealthyKey);
        var value = NativeSerialization.Serialize(HealthyValue);
        store.Commit((transaction, _) =>
        {
            transaction.PutRecord(key, HealthyValue);
            return true;
        });
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
            await EventingArtifactState.SameAsync(
                EventingArtifactState.Bytes(fixture.Source.Store, fixture.Source.Partition),
                current, fixture.Source.Partition);
            await Assert.That(BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(current,
                fixture.Source.Partition, EventingArtifactFixture.Principal, fixture.Original.CommandId).AsSpan()
                .SequenceEqual(BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
                    fixture.Source.Partition, EventingArtifactFixture.Principal, fixture.Original.CommandId))).IsTrue();
            await EventingArtifactState.AssertSeedAsync(fixture, fixture.Database);
        }
    }
}
