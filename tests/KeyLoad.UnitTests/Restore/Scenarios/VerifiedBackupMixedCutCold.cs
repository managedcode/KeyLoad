using System.Text.Json;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class VerifiedBackupMixedCutCold
{
    private const string HealthyNamespace = "mixed-cut-healthy";
    private const string HealthyKey = "continuation";
    private const string HealthyValue = "original-current-format";
    private const int FirstWarmOwner = 0;
    private const int ColdOwners = 2;
    private const long HealthyCommit = 1;

    internal static async Task RunAsync(EventingArtifactFixture fixture, VerifiedBackupCutSnapshot selected,
        string target, long restoredPosition, CancellationToken token)
    {
        var store = fixture.Target!;
        var identity = JsonSerializer.Serialize(store.Identity, JsonDefaults.Options);
        var before = EventingArtifactState.FullBytes(store);
        var key = KeyCodec.Encode(HealthyNamespace, HealthyKey);
        var value = NativeSerialization.Serialize(HealthyValue);
        store.Commit((transaction, _) => { transaction.PutRecord(key, HealthyValue); return true; });
        var expected = before.Append(Convert.ToHexString(key) + ":" + Convert.ToHexString(value))
            .Order(StringComparer.Ordinal).ToArray();
        for (var cold = FirstWarmOwner; cold <= ColdOwners; cold++)
        {
            token.ThrowIfCancellationRequested();
            if (cold != FirstWarmOwner)
            {
                fixture.ReopenTarget(target);
            }
            var current = fixture.Target!;
            await Assert.That(current.Position).IsEqualTo(restoredPosition + HealthyCommit);
            await Assert.That(JsonSerializer.Serialize(current.Identity, JsonDefaults.Options)).IsEqualTo(identity);
            await Assert.That(current.Identity.DispatchPaused).IsTrue();
            await Assert.That(EventingArtifactState.FullBytes(current)).IsEquivalentTo(expected, CollectionOrdering.Matching);
            await Assert.That(EventingArtifactState.Bytes(current, fixture.Source.Partition))
                .IsEquivalentTo(selected.PartitionRows, CollectionOrdering.Matching);
            await Assert.That(BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(current,
                fixture.Source.Partition, EventingArtifactFixture.Principal, fixture.Original.CommandId).AsSpan()
                .SequenceEqual(BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
                    fixture.Source.Partition, EventingArtifactFixture.Principal, fixture.Original.CommandId))).IsTrue();
            await VerifiedBackupMixedCutState.StateAsync(fixture, fixture.Database, selected.SecondOperation is not null);
        }
    }
}
