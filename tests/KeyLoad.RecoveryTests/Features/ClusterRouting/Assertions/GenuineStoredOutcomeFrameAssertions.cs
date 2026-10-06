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
        var database = new DatabaseEngine(store, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
        database.Bootstrap(new(principalId, "system", [new("*", "*", Capability.All)], ["*"])
        { ClusterAdministrator = true }, DatabaseEngine.Credential("outcome-probe-key", principalId,
            "epoch-outcome-probe-owned-admin-key-2026"));
        var position = store.Position;
        var request = new ConfigureResourceRequest("epoch-outcome-tenant", "epoch-outcome-database",
            new ResourceDefinition("epoch-outcome-resource", ResourceKind.Collection, "epoch-outcome-domain"));
        var operation = database.CreateNativeOperation(OperationKind.ConfigureResource, commandId, principalId,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var actual = database.ResolveOutcome(operation);
        await Assert.That(actual).IsEqualTo(prior.Result);
        var changedRequest = request with
        { Definition = request.Definition with { Name = "epoch-outcome-changed-resource" } };
        var changed = database.CreateNativeOperation(OperationKind.ConfigureResource, commandId, principalId,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(changedRequest));
        await Assert.That(database.ResolveOutcome(changed).Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.GlobalOutcome(principalId, commandId))))
            .IsNull();
        await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.UnknownOutcome(principalId, commandId))))
            .IsNull();
        var raw = store.Read(view => view.ReadOwnedValue(KeySpace.LegacyOutcomeKey(principalId, commandId)))
            ?? throw new InvalidOperationException("The canonical outcome disappeared during its read.");
        await Assert.That(raw.AsSpan().SequenceEqual(frame)).IsTrue();
        var locators = store.Read(view => view.VisitRange(KeyCodec.Encode(PartitionRecordFamilies.OutcomeLocator),
            1, static (_, _) => true));
        await Assert.That(locators.Records).IsEqualTo(0);
        await Assert.That(store.Position).IsEqualTo(position);
    }
}
