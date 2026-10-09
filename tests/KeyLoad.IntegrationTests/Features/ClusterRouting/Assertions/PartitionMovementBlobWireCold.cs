using KeyLoad.Replication;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record PartitionMovementBlobWireCold(PartitionMovementBlobWireNativeCut Cut, ReplicaEntry[] Entries)
{
    private const int FirstOwner = 0;
    private const long FirstEntry = 1;

    internal static async Task<PartitionMovementBlobWireCold[]> RequireAsync(PartitionMovementLateNativeOwners owners,
        CancellationToken token)
    {
        var cuts = new List<PartitionMovementBlobWireCold>();
        for (var index = FirstOwner; index < owners.Nodes.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var node = owners.OpenStopped(index);
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() =>
            {
                var cut = PartitionMovementBlobWireNativeCut.Read(node);
                var limit = node.Application.Services.GetRequiredService<ServerRuntimeOptions>().Core.DatabaseLimits.Value.MaxScanRecords;
                if (cut.Applied > limit) { throw new InvalidOperationException("The complete original cold journal exceeded its native record budget."); }
                var entries = new List<ReplicaEntry>();
                for (var entry = FirstEntry; entry <= cut.Applied; entry++)
                { entries.Add(node.Partition.Materializer.Log.ReadEntry(entry)
                    ?? throw new InvalidOperationException("The original cold journal was missing or compacted.")); }
                cuts.Add(new(cut, entries.ToArray()));
            }, failures);
            await ServerFailureObserver.ObserveAsync(() => node.DisposeAsync().AsTask(), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return cuts.ToArray();
    }

    internal static async Task RequireUnchangedAsync(PartitionMovementBlobWireCold[] before,
        PartitionMovementBlobWireCold[] after)
    {
        await Assert.That(after.Length).IsEqualTo(before.Length);
        for (var index = FirstOwner; index < after.Length; index++)
        { await PartitionMovementBlobWireNativeCut.RequireEntriesAsync(before[index].Cut, after[index].Cut,
            after[index].Entries.Where(entry => entry.Index > before[index].Cut.Applied).ToArray()); }
    }
}
