using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3DemotionAccounting
{
    private const char RowSeparator = ':';
    private const int RowFieldCount = 2;
    private const int KeyField = 0;
    private const int ValueField = 1;
    private const long EmptyBytes = 0;
    private const int FormatVersion = 1;
    private const int PhysicalGroupMembers = 3;
    private const long OriginalPromotionPublication = 2;
    private const long OriginalDirectoryRevision = 2;

    internal static async Task<(AtomicPartitionPlacementResolution Owner, long Bytes)> ReadAsync(
        TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var current = await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ReadAtomicPartitionPlacementAsync(new(FormatVersion, seed.Partition), token));
        await Assert.That(current.IsFallback).IsFalse();
        await Assert.That(current.PhysicalShardId).IsEqualTo(seed.FirstRequest.DestinationPhysicalShardId);
        await Assert.That(current.Revision).IsEqualTo(OriginalPromotionPublication);
        await Assert.That(current.DirectoryRevision).IsEqualTo(OriginalDirectoryRevision);
        var stopped = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, seed.FirstRequest, token);
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, seed.FirstRequest, stopped);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(stopped);
        var copies = TwoRf3MembershipProtocol.Nodes.Skip(PhysicalGroupMembers).Select(node => stopped.Single(cut => cut.Node == node)).ToArray();
        var bytes = Count(copies.First().Rows, seed.Partition);
        await Assert.That(bytes).IsGreaterThan(EmptyBytes);
        foreach (var copy in copies)
        { await Assert.That(Count(copy.Rows, seed.Partition)).IsEqualTo(bytes); }
        await TenantCapacityPlacementRf3Cold.ReopenAsync(wave, seed, token);
        return (current, bytes);
    }

    private static long Count(string[] rows, PartitionRef partition)
    {
        var total = EmptyBytes;
        var prefixes = PartitionRecordFamilies.All.Select(family => KeySpace.Partition(family, partition)).ToArray();
        foreach (var row in rows)
        {
            var fields = row.Split(RowSeparator);
            if (fields.Length != RowFieldCount)
            { throw new InvalidDataException(TwoRf3MembershipProtocol.MissingState); }
            var key = Convert.FromHexString(fields[KeyField]);
            if (prefixes.Any(prefix => key.AsSpan().StartsWith(prefix)))
            { total = checked(total + key.Length + Convert.FromHexString(fields[ValueField]).Length); }
        }
        return total;
    }
}
