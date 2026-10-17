using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Search;

internal static class NativeTextBm25EligibilityPolicy
{
    private const string Use = "captured.corpus.use";
    internal static async Task RunAsync(Bm25OriginalPhaseRecoveryFlow flow, ReplicatedOperation original)
    {
        var owner = flow.Owner;
        var tail = owner.Database.GetOutboxStatus("root", owner.Source.Partition).Head.Tail;
        var resource = owner.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, "orders")))!;
        var request = new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId,
            resource with
            {
                SchemaVersion = resource.SchemaVersion + 1,
                FieldPolicies = [new("/text", "captured-private", "captured.corpus.read", Use, "captured.corpus.write")]
            })
        {
            ExpectedSchemaVersion = resource.SchemaVersion
        };
        var configuration = owner.Database.CreateNativeOperation(OperationKind.ConfigureResource, Guid.NewGuid(), "root",
            owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var changed = owner.Submit(configuration).Get<ResourceDefinition>();
        await Assert.That(changed.SchemaVersion).IsEqualTo(resource.SchemaVersion + 1);
        var revoked = flow.Configure(flow.Reader with
        {
            PolicyEpoch = 2
        });
        await Assert.That(revoked.PolicyEpoch).IsEqualTo(2L);
        await NativeTextBm25EligibilityPages.DeniedAsync(flow);
        var repaired = flow.Configure(revoked with
        {
            PolicyEpoch = 3,
            FieldGrants = [Use]
        });
        await Assert.That(repaired.PolicyEpoch).IsEqualTo(3L);
        await Assert.That(owner.Database.GetOutboxStatus("root", owner.Source.Partition).Head.Tail).IsEqualTo(tail);
        await NativeTextBm25EligibilityPages.RequireAsync(flow, false, true, true);
        await Bm25ColdInvalidationAssertions.PhaseDeniedAsync(flow, original);
        var fresh = flow.Capture(original.Id);
        var result = flow.Publish(fresh);
        await Assert.That(result.Error).IsNull();
        var phase = NativeCommandPayload.Read<Bm25CorpusPublicationPhaseCommand>(fresh);
        await Assert.That(phase.Current.PolicyEpoch).IsEqualTo(3L);
        await Assert.That(phase.Current.Statistics.DocumentCount).IsEqualTo(2);
        await Assert.That(phase.Current.Statistics.TotalLength).IsEqualTo(5L);
        await Assert.That(phase.Current.Statistics.DocumentFrequencies.Single()).IsEqualTo(2);
        await Assert.That(phase.Current.SourceOutboxTail).IsEqualTo(tail);
        await Assert.That(phase.Current.SchemaVersion).IsEqualTo(changed.SchemaVersion);
        var entry = NativeSerialization.Serialize(owner.Entry(fresh.Id));
        await NativeTextBm25EligibilityPages.RequireAsync(flow, true, true, true);
        await Bm25ColdInvalidationAssertions.ReplayAsync(flow, fresh, result, entry);
        var before = Bm25OriginalPhaseRecoveryState.Read(owner);
        owner.Reopen();
        await before.RequireAsync(Bm25OriginalPhaseRecoveryState.Read(owner));
        await NativeTextBm25EligibilityPages.RequireAsync(flow, true, true, true);
        await Bm25ColdInvalidationAssertions.ReplayAsync(flow, fresh, result, entry);
    }
}
