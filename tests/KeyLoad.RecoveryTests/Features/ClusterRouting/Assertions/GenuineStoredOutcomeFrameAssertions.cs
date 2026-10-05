using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class GenuineStoredOutcomeFrameAssertions
{
    private const string ResourceName = "epoch-outcome-resource";
    private const string DomainId = "epoch-outcome-domain";

    internal static async Task AssertPriorDefaultsAsync(StoredOutcome outcome, EpochPriorProbeReceipt receipt)
    {
        await Assert.That(outcome.Fingerprint.Length > 0).IsTrue();
        await Assert.That(outcome.Incarnation).IsEqualTo(receipt.Incarnation);
        await Assert.That(outcome.BlobAuthority).IsNull();
        await Assert.That(outcome.CompositionAuthority).IsNull();
        await Assert.That(outcome.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Unknown);
        await Assert.That(outcome.Partition).IsNull();
        await Assert.That(outcome.Result.Error).IsNull();
        var result = outcome.Result.Get<ResourceDefinition>();
        await Assert.That(result.Name).IsEqualTo(ResourceName);
        await Assert.That(result.Kind).IsEqualTo(ResourceKind.Collection);
        await Assert.That(result.TransactionDomainId).IsEqualTo(DomainId);
        await Assert.That(result.SchemaVersion).IsEqualTo(1L);
        await Assert.That(result.Indexes.IsEmpty).IsTrue();
        await Assert.That(result.Paused).IsFalse();
    }

    internal static async Task AssertCurrentReadAsync(ZoneTreeStore store, StoredOutcome prior, byte[] frame,
        string principalId, Guid commandId)
    {
        var position = store.Position;
        var actual = new DatabaseEngine(store, new AuthorizationPolicy()).Outcome(principalId, commandId);
        await Assert.That(actual).IsEqualTo(prior.Result);
        await Assert.That(store.Position).IsEqualTo(position);
        var raw = store.Read(view => view.ReadOwnedValue(KeySpace.Outcome(principalId, commandId)))
            ?? throw new InvalidOperationException("The canonical outcome disappeared during its read.");
        await Assert.That(raw.AsSpan().SequenceEqual(frame)).IsTrue();
        var locators = store.Read(view => view.VisitRange(KeyCodec.Encode(PartitionRecordFamilies.OutcomeLocator),
            1, static (_, _) => true));
        await Assert.That(locators.Records).IsEqualTo(0);
        await Assert.That(store.Position).IsEqualTo(position);
    }
}
