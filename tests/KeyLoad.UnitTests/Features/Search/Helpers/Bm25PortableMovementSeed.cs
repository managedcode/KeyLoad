using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25PortableMovementSeed
{
    internal const string Field = "/text";

    internal static async Task<(Bm25CorpusCurrentV1 Head, byte[] Bytes)> CreateAsync(
        ControlledPartitionMovementNativeHosts owners, CancellationToken token)
    {
        await ControlledPartitionMovementNativeRegistration.VerifyAsync(owners);
        _ = await ControlledPartitionMovementNativeSeed.ExecuteAsync(owners.Source, token);
        var source = owners.Source;
        var commandId = Guid.NewGuid();
        var expiry = source.Clock.GetUtcNow() + source.Runtime.GrainRouting.Value.RequestLifetime;
        var work = new ReadExecutionBudget(source.Runtime.Core.DatabaseLimits, source.Clock, token);
        var receipt = await NativeBm25CorpusMaintenance.PublishAsync(source.Silo, source.Partition,
            PhysicalShardCatalogFixture.RootPrincipalId, PhysicalShardCatalogFixture.RootPrincipalId,
            new(ControlledPartitionMovementCorpus.Partition, ControlledPartitionMovementCorpus.Collection,
                Field, "knowledge"), commandId, null, expiry, work, token);
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await source.Partition.Coordinator.ReadBarrierAsync(token);
        var entry = ControlledPartitionMovementNativeEntryReader.Read(source, commandId, token);
        await Assert.That(entry.Operation!.Kind).IsEqualTo(OperationKind.Bm25CorpusPublicationPhase);
        var key = KeySpace.Partition(Bm25CorpusProtocol.CurrentFamily, ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementCorpus.Collection, Field,
            KeyLoad.Query.Features.Search.TextProjectionProtocol.TokenizerVersion, PhysicalShardCatalogFixture.RootPrincipalId);
        var bytes = source.Store.Read(view => work.Read(view, key))
            ?? throw new InvalidOperationException("The actual native maintenance publication head is absent.");
        var head = NativeSerialization.Deserialize<Bm25CorpusCurrentV1>(bytes);
        Bm25CorpusShape.RequireRecord(key, bytes, ControlledPartitionMovementCorpus.Partition, source.Database.Limits);
        await Assert.That(head.Generation).IsEqualTo(commandId);
        await Assert.That(head.Statistics.DocumentCount).IsEqualTo(1);
        await Assert.That(head.Statistics.TotalLength).IsEqualTo(2L);
        await Assert.That(head.Statistics.Terms).IsEquivalentTo(new[] { "knowledge" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(head.Statistics.DocumentFrequencies).IsEquivalentTo(new[] { 1 },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        return (head, bytes);
    }
}
