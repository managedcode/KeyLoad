using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3CanonicalCut
{
    private const int FirstNode = 0;
    private const int GroupMembers = 3;
    private const int NoRecords = 0;
    private const int RecordStep = 1;

    internal static async Task RequireOriginalAsync(ClusterRestoreRf3Fixture target,
        ImmutableArray<ClusterBackupOwnerReceipt> originals, ImmutableArray<string> archives, CancellationToken token)
    {
        await Assert.That(archives.Length).IsEqualTo(originals.Length);
        var images = new Dictionary<Guid, ImmutableArray<ClusterRestoreRf3CanonicalImage>>();
        foreach (var (original, archive) in originals.Zip(archives))
        {
            token.ThrowIfCancellationRequested();
            var actual = ZoneTreeStore.ReadVerifiedCatalogBackup(archive, IntegrationExecutionOptions.StorageExecution(),
                (view, identity, position, metadata) =>
                {
                    if (identity.NodeId != original.Cut.SourceNodeId || identity.Incarnation != original.Cut.Owner.Incarnation
                        || position != original.Cut.StorePosition)
                    { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
                    var cut = NativeSerialization.Deserialize<ClusterBackupOwnerCut>(metadata.Span);
                    if (!NativeSerialization.Serialize(cut).AsSpan().SequenceEqual(NativeSerialization.Serialize(original.Cut)))
                    { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
                    return Read(view, original, token);
                }, token);
            images.Add(original.Cut.Owner.PhysicalShardId, actual);
        }
        var expected = target.Mappings.SelectMany(mapping => Enumerable.Repeat(images[mapping.Source.PhysicalShardId], GroupMembers))
            .ToImmutableArray();
        await EqualAsync(expected, Target(target, originals, token)).ConfigureAwait(false);
    }

    internal static ImmutableArray<ImmutableArray<ClusterRestoreRf3CanonicalImage>> Target(ClusterRestoreRf3Fixture target,
        ImmutableArray<ClusterBackupOwnerReceipt> originals, CancellationToken token)
    {
        var results = ImmutableArray.CreateBuilder<ImmutableArray<ClusterRestoreRf3CanonicalImage>>();
        var failures = new List<Exception>();
        for (var index = FirstNode; index < ClusterRestoreRf3Protocol.Nodes.Length; index++)
        {
            var mapping = target.Mappings[index / GroupMembers];
            var original = originals.Single(receipt => receipt.Cut.Owner.PhysicalShardId == mapping.Source.PhysicalShardId);
            ZoneTreeStore? store = null;
            ServerFailureObserver.Observe(() =>
            {
                token.ThrowIfCancellationRequested();
                store = new(new(Path.Combine(target.DataRoot, ClusterRestoreRf3Protocol.Nodes[index],
                    ClusterRestoreRf3Protocol.DatabaseDirectory)), IntegrationExecutionOptions.StorageExecution(),
                    IntegrationExecutionOptions.PointCacheExecution());
                results.Add(store.Read(view => Read(view, original, token)));
            }, failures);
            if (store is { } owned)
            { ServerFailureObserver.Observe(owned.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return results.ToImmutable();
    }

    internal static Task RequireColdAsync(ClusterRestoreRf3Fixture target, ImmutableArray<ClusterBackupOwnerReceipt> originals,
        ImmutableArray<ImmutableArray<ClusterRestoreRf3CanonicalImage>> expected, CancellationToken token)
        => EqualAsync(expected, Target(target, originals, token));

    private static async Task EqualAsync(ImmutableArray<ImmutableArray<ClusterRestoreRf3CanonicalImage>> expected,
        ImmutableArray<ImmutableArray<ClusterRestoreRf3CanonicalImage>> actual)
    {
        await Assert.That(actual.Length).IsEqualTo(expected.Length);
        for (var index = FirstNode; index < expected.Length; index++)
        { await Assert.That(actual[index]).IsEquivalentTo(expected[index], CollectionOrdering.Matching); }
    }

    private static ImmutableArray<ClusterRestoreRf3CanonicalImage> Read(IKeyValueView view,
        ClusterBackupOwnerReceipt original, CancellationToken token)
        => [.. original.Cut.Partitions.Select(partition => Partition(view, partition.Roster.Partition, token))];

    private static ClusterRestoreRf3CanonicalImage Partition(IKeyValueView view, PartitionRef partition, CancellationToken token)
    {
        var failures = new List<Exception>();
        ClusterRestoreRf3CanonicalImage result = null!;
        ServerFailureObserver.Observe(() =>
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            ServerFailureObserver.Observe(() => result = ReadPartition(view, partition, hash, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private static ClusterRestoreRf3CanonicalImage ReadPartition(IKeyValueView view, PartitionRef partition,
        IncrementalHash hash, CancellationToken token)
    {
        var maximum = IntegrationExecutionOptions.DatabaseLimits().Value.MaxScanRecords;
        var count = NoRecords;
        foreach (var family in PartitionRecordFamilies.All)
        {
            token.ThrowIfCancellationRequested();
            if (count >= maximum)
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
            var page = view.VisitRange(KeySpace.Partition(family, partition), maximum - count, (key, value) =>
            {
                Append(hash, key);
                Append(hash, value);
                count = checked(count + RecordStep);
                return true;
            }, cancellationToken: token);
            if (page.HasMore || page.StoppedByVisitor)
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
        }
        return new(partition, count, Convert.ToHexString(hash.GetHashAndReset()));
    }

    private static void Append(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
