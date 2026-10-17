using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;
using static KeyLoad.Core.Features.ResourceExecution.ResourceRetirementTableCleanupFamilies;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementTableCleanupKeys
{
    internal static byte[] Prefix(ResourceLifetimeV1 lifetime, PartitionRef partition,
        ResourceRetirementTableCleanupCursor cursor)
    {
        if (lifetime is null || partition is null || cursor is null || lifetime.Kind != ResourceKind.Collection
            || lifetime.TenantId != partition.TenantId || lifetime.DatabaseId != partition.DatabaseId
            || lifetime.TransactionDomainId != partition.TransactionDomainId
            || cursor.FamilyOrdinal < Empty || cursor.FamilyOrdinal > All.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        ResourceLifetimeIdentity.Validate(lifetime, lifetime.TenantId, lifetime.DatabaseId,
            new(lifetime.ResourceName, lifetime.Kind, lifetime.TransactionDomainId));
        JsonData.Identifier(partition.PartitionKey);
        var after = cursor.AfterKey;
        if (cursor.FamilyOrdinal == All.Length)
        {
            if (after.Length != Empty)
            {
                throw Errors.Fail(ErrorCode.Corruption, Invalid);
            }

            return [];
        }
        var prefix = KeySpace.Partition(All[cursor.FamilyOrdinal], partition, lifetime.ResourceName);
        if (after.Length != Empty)
        {
            Require(after, prefix, All[cursor.FamilyOrdinal]);
        }

        return prefix;
    }

    internal static void Require(ReadOnlySpan<byte> key, byte[] prefix, string family)
    {
        if (!key.StartsWith(prefix))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }

        var parts = KeyCodec.Decode(key);
        if (!KeyCodec.Encode(parts).AsSpan().SequenceEqual(key) || !Shape(parts, family))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }

    private static bool Shape(object?[] parts, string family)
        => family switch
        {
            PartitionRecordFamilies.Document => parts.Length == DocumentComponents && parts[^Step] is string,
            PartitionRecordFamilies.Index => parts.Length >= IndexMinimumComponents && parts[PrefixComponents] is string
                && parts[^Step] is string,
            PartitionRecordFamilies.Unique => parts.Length >= UniqueMinimumComponents && parts[PrefixComponents] is string,
            PartitionRecordFamilies.DocumentEpoch or PartitionRecordFamilies.VisibilityEpoch
                => parts.Length == PrefixComponents,
            PartitionRecordFamilies.Vector or PartitionRecordFamilies.VectorProjectionLineage
                => parts.Length == VectorComponents && parts[PrefixComponents] is string && parts[^Step] is string,
            PartitionRecordFamilies.Bm25CorpusCurrent => parts.Length == PublicationComponents
                && parts.Skip(PrefixComponents).All(value => value is string),
            PartitionRecordFamilies.OnlineTextCurrent => parts.Length == PublicationComponents
                && parts[PrefixComponents] is string && parts[DocumentComponents] is string && parts[^Step] is long,
            _ => false
        };
}
