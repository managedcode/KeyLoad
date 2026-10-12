using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.Search;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25PortableMovementAssertions
{
    internal static async Task HeadAsync(ControlledPartitionMovementNativeHost owner,
        Bm25CorpusCurrentV1 original, byte[] originalBytes, CancellationToken token)
    {
        var work = new ReadExecutionBudget(owner.Runtime.Core.DatabaseLimits, owner.Clock, token);
        var key = Bm25CorpusKeys.Current(original);
        var actual = owner.Store.Read(view => work.Read(view, key));
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.AsSpan().SequenceEqual(originalBytes)).IsTrue();
        Bm25CorpusShape.RequireRecord(key, actual, original.Partition, owner.Database.Limits);
    }

    internal static async Task UnchangedAsync(
        ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> before,
        ControlledPartitionMovementNativeHosts owners, CancellationToken token)
    {
        var after = await Bm25PortableMovementCapture.SixAsync(owners, token);
        await Assert.That(before.Length).IsEqualTo(6);
        await Assert.That(after.Length).IsEqualTo(6);
        for (var index = 0; index < before.Length; index++)
        {
            await Assert.That(NativeSerialization.Serialize(after[index]).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(before[index]))).IsTrue();
        }
    }

    internal static async Task HistoricalAsync(ControlledPartitionMovementNativeHosts owners,
        Bm25CorpusCurrentV1 original, byte[] originalBytes, CancellationToken token)
    {
        var before = await Bm25PortableMovementCapture.SixAsync(owners, token);
        var target = owners.Target;
        var work = new ReadExecutionBudget(target.Runtime.Core.DatabaseLimits, target.Clock, token);
        var observed = target.Database.WithQueryView(original.PrincipalId, original.Partition,
            original.Collection, (view, principal, resource) =>
            {
                var bounded = work.CreateView(view);
                var catalog = PhysicalShardCatalogRecordSerialization.Read(bounded)
                    ?? throw new InvalidOperationException("The actual destination catalog is absent.");
                var statistics = target.Database.ReadBm25CorpusCurrent(bounded, principal, resource,
                    original.Partition, original.Collection, original.Field, original.TokenizerVersion,
                    original.Statistics.Terms, work);
                return (Statistics: statistics, PrincipalId: principal.Id, PolicyEpoch: principal.PolicyEpoch,
                    SchemaVersion: resource.SchemaVersion, ResourcePolicyDigest: Bm25CorpusDigest.Resource(resource, work),
                    SourceOutboxTail: target.Database.ReadOutboxHead(bounded, original.Partition).Tail,
                    Owner: catalog.DefaultShard);
            });
        await Assert.That(observed.PrincipalId).IsEqualTo(original.PrincipalId);
        await Assert.That(observed.PolicyEpoch).IsEqualTo(original.PolicyEpoch);
        await Assert.That(observed.SchemaVersion).IsEqualTo(original.SchemaVersion);
        await Assert.That(observed.ResourcePolicyDigest).IsEqualTo(original.ResourcePolicyDigest);
        await Assert.That(observed.SourceOutboxTail).IsEqualTo(original.SourceOutboxTail);
        await Assert.That(observed.Owner.Incarnation).IsEqualTo(target.Store.Identity.Incarnation);
        await Assert.That(observed.Owner.PhysicalShardId == original.SourceOwner.PhysicalShardId).IsFalse();
        await Assert.That(observed.Owner.Incarnation == original.Incarnation).IsFalse();
        await Assert.That(observed.Statistics).IsNull();
        var search = new SearchEngine(target.Database,
            target.Silo.RuntimeServices.GetRequiredService<IOptions<QueryExecutionOptions>>());
        var hits = await search.SearchAsync(original.PrincipalId,
            new(original.Partition, original.Collection, original.Field, "knowledge",
                TextWeight: 1d, FusionConstant: 60), token);
        await Assert.That(hits.Length).IsEqualTo(1);
        var expectedDocument = new DocumentResult(new(original.Partition, original.Collection,
            ControlledPartitionMovementCorpus.DocumentId), 1L, ControlledPartitionMovementCorpus.OriginalJson, false, []);
        await Assert.That(NativeSerialization.Serialize(hits[0].Document).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expectedDocument))).IsTrue();
        await Assert.That(hits[0].Score).IsEqualTo(1d / 61d);
        await HeadAsync(target, original, originalBytes, token);
        await UnchangedAsync(before, owners, token);
    }

    internal static async Task<PartitionMoveResult> ResultAsync(GrainOperationReply reply, PartitionMoveRequest request)
    {
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionMoveResult
            ?? throw new InvalidOperationException("The authentic original movement result is absent.");
        await Assert.That(result.MoveId).IsEqualTo(request.MoveId);
        await Assert.That(result.Partition).IsEqualTo(request.Partition);
        await Assert.That(result.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await Assert.That(result.InstalledReceipt).IsNotNull();
        await Assert.That(result.PublishedPlacement).IsNotNull();
        await Assert.That(result.DestinationOwner.PhysicalShardId).IsEqualTo(request.DestinationPhysicalShardId);
        return result;
    }

    internal static async Task HealthyAsync(ControlledPartitionMovementNativeHost target, CancellationToken token)
    {
        var reference = new EntityRef(ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementCorpus.Collection, "bm25-moved-followup");
        var request = new CommandRequest(Guid.NewGuid(), reference.Partition,
            [new PutDocument(reference.Collection, reference.Id, """{"text":"healthy after BM25 move"}""")]);
        var reply = await target.SubmitAsync(OperationKind.Batch, request.CommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, NativeSerialization.Serialize(request), token);
        await Assert.That(reply.Error).IsNull();
        var receipt = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as CommitReceipt;
        await Assert.That(receipt!.CommandId).IsEqualTo(request.CommandId);
        var actual = target.Database.GetDocument(PhysicalShardCatalogFixture.RootPrincipalId,
            reference, cancellationToken: token);
        await Assert.That(actual!.Json).IsEqualTo("""{"text":"healthy after BM25 move"}""");
        var replay = await target.SubmitAsync(OperationKind.Batch, request.CommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, NativeSerialization.Serialize(request), token);
        await Assert.That(replay.Error).IsNull();
        await Assert.That(replay.Payload.Span.SequenceEqual(reply.Payload.Span)).IsTrue();
    }
}
