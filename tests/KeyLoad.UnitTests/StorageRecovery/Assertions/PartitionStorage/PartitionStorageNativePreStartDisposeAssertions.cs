using KeyLoad.Clustering;
using KeyLoad.RecoveryHost.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativePreStartDisposeAssertions
{
    private const int NoEntries = 0;
    private const int FirstOwner = 0;
    private const int EntryLookahead = 1;

    internal static async Task BeforeAsync(WebApplication application,
        PartitionStorageNativePreStartDisposeOwners captured, ControlledPartitionMovementNativeHostSettings settings)
    {
        await Assert.That(ReferenceEquals(captured.Physical,
            application.Services.GetRequiredService<PartitionHostApplicationOwner>())).IsTrue();
        await Assert.That(ReferenceEquals(captured.Native,
            application.Services.GetRequiredService<OrleansNode>())).IsTrue();
        await Assert.That(ReferenceEquals(captured.Connections,
            application.Services.GetRequiredService<ServerConnectionRegistry>())).IsTrue();
        await Assert.That(ReferenceEquals(captured.Transport,
            application.Services.GetRequiredService<ReplicaTransportLifecycle>())).IsTrue();
        await NeverOpenedAsync(captured, settings).ConfigureAwait(false);
        await Assert.That(captured.Physical.Closing is null).IsTrue();
        await Assert.That(captured.Native.Closing is null).IsTrue();
    }

    internal static async Task AfterAsync(Task originalDisposal,
        PartitionStorageNativePreStartDisposeOwners captured, ControlledPartitionMovementNativeHostSettings settings)
    {
        await Assert.That(originalDisposal.IsCompletedSuccessfully).IsTrue();
        await Assert.That(captured.Physical.Closing is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(captured.Native.Closing is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(captured.Native.RequestWork.IsJoined).IsTrue();
        await Assert.That(captured.Native.HasJoinedRequestWork).IsTrue();
        await Assert.That(captured.Physical.CanReleaseRoot).IsTrue();
        await NeverOpenedAsync(captured, settings).ConfigureAwait(false);
    }

    private static async Task NeverOpenedAsync(PartitionStorageNativePreStartDisposeOwners captured,
        ControlledPartitionMovementNativeHostSettings settings)
    {
        await Assert.That(captured.Physical.Opening is null).IsTrue();
        await Assert.That(captured.Physical.StorageProducerJoins is null).IsTrue();
        await Assert.That(captured.Native.Grains is null).IsTrue();
        await Assert.That(captured.Native.Discovery is null).IsTrue();
        await Assert.That(captured.Native.SiloJoined).IsFalse();
        await Assert.That(captured.Native.DatabaseReady).IsFalse();
        await Assert.That(Directory.Exists(settings.Root)).IsTrue();
        var entries = Directory.EnumerateFileSystemEntries(settings.Root)
            .Take(ControlledPartitionMovementNativeHostEndpoints.OwnerCount + EntryLookahead).ToArray();
        await Assert.That(entries.Length).IsEqualTo(NoEntries);
        for (var index = FirstOwner; index < ControlledPartitionMovementNativeHostEndpoints.OwnerCount; index++)
        {
            var directory = settings.DirectoryPath(index);
            await Assert.That(Directory.Exists(directory)).IsFalse();
            await Assert.That(File.Exists(Path.Combine(directory, PartitionStoreProtocol.OwnershipFile))).IsFalse();
            await Assert.That(Directory.Exists(Path.Combine(directory, PartitionStoreProtocol.CanonicalDirectory)))
                .IsFalse();
            await Assert.That(Directory.Exists(Path.Combine(directory, KeyLoad.Replication.ReplicaProtocol.ReplicaDirectory)))
                .IsFalse();
        }
    }
}
