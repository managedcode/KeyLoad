using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class RemoteDocumentNativeAssertions
{
    private const string MutationKind = "putDocument";

    internal static async Task RunAsync(Func<RemoteDocumentNativeFixture, Task> scenario)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new RemoteDocumentNativeFixture();
            await ServerFailureObserver.ObserveAsync(() => scenario(fixture), failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task ReceiptAsync(RemoteDocumentNativeFixture fixture)
    {
        var expected = new CommitReceipt(RemoteDocumentNativeFixture.WriteId,
            new(PhysicalOwnerDirectoryWholeFlow.Destination.Owner.Incarnation,
                RemoteDocumentNativeFixture.Partition.AtomicPartitionId, RemoteDocumentNativeFixture.First,
                RemoteDocumentNativeFixture.First),
            [new(MutationKind, RemoteDocumentNativeFixture.Collection,
                RemoteDocumentNativeFixture.DocumentId, RemoteDocumentNativeFixture.First)], DurabilityProfile.ProcessDurable);
        await Assert.That(NativeSerialization.Serialize(fixture.Receipt)
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }

    internal static async Task DocumentAsync(OwnedDocumentReadResultV1 actual,
        PhysicalShardCatalogFixture destination, long position)
    {
        var owner = PhysicalOwnerDirectoryWholeFlow.Destination.Owner;
        var expected = new OwnedDocumentReadResultV1(new(RemoteDocumentNativeFixture.Reference,
            RemoteDocumentNativeFixture.First, RemoteDocumentNativeFixture.ProjectedJson, true,
            [RemoteDocumentNativeFixture.Secret]), destination.Store.Identity.NodeId, owner.Incarnation,
            destination.Store.Identity.ReadGeneration,
            new(RemoteDocumentNativeFixture.Version, RemoteDocumentNativeFixture.Partition,
                owner.PhysicalShardId, owner.Incarnation, owner.VoterIds, owner.PlacementEpoch,
                RemoteDocumentNativeFixture.Empty, RemoteDocumentNativeFixture.Empty, true),
            RemoteDocumentNativeFixture.Second, RemoteDocumentNativeFixture.First, position);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static string[] Image(PhysicalShardCatalogFixture fixture) => PhysicalOwnerDirectoryWholeFlow.Image(fixture);
}
