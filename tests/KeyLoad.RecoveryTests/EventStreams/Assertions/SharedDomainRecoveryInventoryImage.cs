using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.CrashHost.Features.EventStreams;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class SharedDomainRecoveryInventoryImage
{
    private const string RowSeparator = ":";
    private const string MissingSeedRecord = "The original shared-domain seed record is absent.";
    private const string UncontrolledCoverageDigest = "";

    internal static async Task<string[]> ExpectedAsync(SharedDomainRecoveryInput input,
        SharedDomainScope scope, bool committed)
    {
        var rows = input.SeedImage.ToDictionary(Key, Value, StringComparer.Ordinal);
        var originKey = ResourceOwnerInventoryKeys.Origin();
        var origin = NativeSerialization.Deserialize<ResourceOwnerInventoryOriginV1>(Read(rows, originKey));
        ResourceOwnerInventoryValidation.Origin(origin, requireProven: true);
        await Assert.That(origin.StoreIncarnation).IsEqualTo(input.OriginalIdentityDigest.Incarnation);
        var witnessKey = ResourceOwnerInventoryKeys.Witness(scope.Partition);
        await Assert.That(rows.ContainsKey(Convert.ToHexString(witnessKey))).IsFalse();
        foreach (var definition in scope.Resources.OrderBy(definition =>
            Convert.ToHexString(KeySpace.Resource(scope.Partition.TenantId, scope.Partition.DatabaseId, definition.Name)),
            StringComparer.Ordinal))
        {
            var lifetime = await LifetimeAsync(rows, scope.Partition, definition);
            var entry = new ResourceOwnerInventoryEnrollmentV1(ResourceOwnerInventoryProtocol.Version,
                lifetime, scope.Partition, origin);
            var memberKey = ResourceOwnerInventoryKeys.Partition(entry);
            var reverseKey = ResourceOwnerInventoryKeys.Entry(lifetime, scope.Partition);
            await Assert.That(rows.ContainsKey(Convert.ToHexString(memberKey))).IsFalse();
            await Assert.That(rows.ContainsKey(Convert.ToHexString(reverseKey))).IsFalse();
            if (committed)
            {
                var bytes = NativeSerialization.Serialize(entry);
                Put(rows, memberKey, bytes);
                Put(rows, reverseKey, bytes);
                origin = origin with
                {
                    Revision = checked(origin.Revision + ResourceOwnerInventoryProtocol.Next)
                };
            }
        }
        if (committed)
        {
            var revision = NativeSerialization.Deserialize<long>(Read(rows, ResourceOwnerInventoryKeys.Domain(scope.Partition)));
            await Assert.That(revision).IsEqualTo((long)scope.Resources.Length);
            Put(rows, originKey, NativeSerialization.Serialize(origin));
            Put(rows, witnessKey, NativeSerialization.Serialize(new ResourceOwnerInventoryDomainWitnessV1(
                origin.StoreIncarnation, revision, UncontrolledCoverageDigest)));
        }
        return rows.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + RowSeparator + pair.Value).ToArray();
    }

    internal static bool Retain(ReadOnlySpan<byte> key, PartitionRef partition)
    {
        if (!SharedDomainStoredImage.Owns(key, partition))
        {
            return true;
        }
        var family = KeyCodec.Decode(key)[ResourceOwnerInventoryProtocol.FirstField];
        return Equals(family, PartitionRecordFamilies.ResourceOwnerInventoryMember)
            || Equals(family, PartitionRecordFamilies.ResourceOwnerInventoryProof)
            || Equals(family, ResourceOwnerInventoryProtocol.WitnessSpace);
    }

    private static async Task<ResourceLifetimeV1> LifetimeAsync(Dictionary<string, string> rows,
        PartitionRef partition, ResourceDefinition definition)
    {
        var actualDefinition = NativeSerialization.Deserialize<ResourceDefinition>(Read(rows,
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, definition.Name)));
        await Assert.That(actualDefinition).IsEqualTo(definition);
        var lifetime = NativeSerialization.Deserialize<ResourceLifetimeV1>(Read(rows,
            ResourceLifetimeCatalog.Key(partition.TenantId, partition.DatabaseId, definition.Name)));
        ResourceLifetimeIdentity.Validate(lifetime, partition.TenantId, partition.DatabaseId, definition);
        var creation = NativeSerialization.Deserialize<ResourceLifetimeV1>(Read(rows,
            ResourceOwnerInventoryKeys.Creation(lifetime)));
        await Assert.That(creation).IsEqualTo(lifetime);
        return lifetime;
    }

    private static string Key(string row) => row[..row.IndexOf(RowSeparator, StringComparison.Ordinal)];
    private static string Value(string row) => row[(row.IndexOf(RowSeparator, StringComparison.Ordinal) + RowSeparator.Length)..];

    private static byte[] Read(Dictionary<string, string> rows, byte[] key)
        => rows.TryGetValue(Convert.ToHexString(key), out var value)
            ? Convert.FromHexString(value) : throw new InvalidOperationException(MissingSeedRecord);

    private static void Put(Dictionary<string, string> rows, byte[] key, byte[] value)
        => rows[Convert.ToHexString(key)] = Convert.ToHexString(value);
}
