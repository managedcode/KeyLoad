using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record PartitionMovementBlobWireNativeCut(PartitionMovementPublicParentRf3NativeCut Rows, long Applied)
{
    private const int EmptyCounter = 0;
    private const long NextEntry = 1;

    internal static PartitionMovementBlobWireNativeCut Read(PartitionMovementLateNativeNode owner)
    {
        var database = owner.Partition.Database;
        var limits = owner.Application.Services.GetRequiredService<ServerRuntimeOptions>().Core.DatabaseLimits.Value;
        return database.Store.Read(view =>
        {
            var page = view.Scan([], limits.MaxScanRecords);
            if (page.HasMore)
            { throw new InvalidOperationException("The complete native wire cut exceeded its original bound."); }
            var rows = page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
            var applied = view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes ? NativeSerialization.Deserialize<long>(bytes) : EmptyCounter;
            return new PartitionMovementBlobWireNativeCut(new(owner.Partition.Configuration.LocalId,
                database.Store.Position, rows, null, null, null, null, null, null, null, null, [], null,
                EmptyCounter, null, EmptyCounter), applied);
        });
    }

    internal static async Task RequireAsync(PartitionMovementLateNativeNode owner,
        PartitionMovementBlobWireNativeCut before, PartitionMovementBlobWireNativeCut after)
    {
        var entries = new List<ReplicaEntry>();
        for (var index = before.Applied + NextEntry; index <= after.Applied; index++)
        {
            entries.Add(owner.Partition.Materializer.Log.ReadEntry(index)
            ?? throw new InvalidOperationException("The real native wire cut entry was compacted or missing."));
        }
        await RequireEntriesAsync(before, after, entries.ToArray());
    }

    internal static async Task RequireEntriesAsync(PartitionMovementBlobWireNativeCut before,
        PartitionMovementBlobWireNativeCut after, ReplicaEntry[] entries)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        var membership = new PartitionMovementNativeMembershipRf3Cut(before.Rows, after.Rows, allowed);
        var count = EmptyCounter;
        for (var index = before.Applied + NextEntry; index <= after.Applied; index++)
        {
            var entry = entries.Single(value => value.Index == index);
            await Assert.That(entry.Index).IsEqualTo(index);
            if (entry.Operation is { } operation)
            { await Assert.That(await membership.TryApplyAsync(operation)).IsTrue(); }
            count++;
        }
        await Assert.That(entries.Length).IsEqualTo(count);
        if (count != EmptyCounter)
        { allowed.Add(Convert.ToHexString(KeySpace.AppliedBytes)); }
        await membership.RequireCompleteAsync();
        var left = PartitionMovementCapturePointerRf3Fault.Rows(before.Rows);
        var right = PartitionMovementCapturePointerRf3Fault.Rows(after.Rows);
        await Assert.That(left.Keys.Concat(right.Keys).Distinct(StringComparer.Ordinal)
            .Where(key => !allowed.Contains(key)).All(key => left.TryGetValue(key, out var value)
                && right.TryGetValue(key, out var actual) && actual == value)).IsTrue();
        await Assert.That(after.Rows.StorePosition - before.Rows.StorePosition).IsEqualTo((long)count);
    }
}
