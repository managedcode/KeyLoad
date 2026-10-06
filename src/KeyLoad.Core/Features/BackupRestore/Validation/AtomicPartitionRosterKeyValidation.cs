using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BackupRestore.Validation;

internal static class AtomicPartitionRosterKeyValidation
{
    private const int NoComponents = 0;
    private const int FamilyComponentIndex = 0;
    private const int TenantComponentIndex = 1;
    private const int DatabaseComponentIndex = 2;
    private const int DomainComponentIndex = 3;
    private const int PartitionKeyComponentIndex = 4;
    private const int MinimumScopedComponentCount = 5;
    private const int PlacementTenantComponentIndex = 2;
    private const int PlacementDatabaseComponentIndex = 3;
    private const int PlacementDomainComponentIndex = 4;
    private const int PlacementPartitionKeyComponentIndex = 5;
    private const int PlacementComponentCount = 6;
    private const int PlacementVersionIndex = 1;
    private const int GlobalOutcomePrincipalComponentIndex = 2;
    private const int GlobalOutcomeCommandComponentIndex = 3;
    private const int GlobalOutcomeComponentCount = 4;

    internal static bool TryReadCandidate(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        bool hasValue, out PartitionRef? partition)
    {
        var components = KeyCodec.Decode(key);
        partition = null;
        if (components.Length == NoComponents || components[FamilyComponentIndex] is not string family)
        {
            return false;
        }

        if (StringComparer.Ordinal.Equals(family, PartitionRecordFamilies.OutcomeV2)
            && IsCanonicalGlobalOutcome(components, key))
        {
            return false;
        }

        if (PartitionRecordFamilies.All.Contains(family, StringComparer.Ordinal))
        {
            partition = ReadScopedPartition(components, key);
            return true;
        }
        if (StringComparer.Ordinal.Equals(family, AtomicPartitionPlacementSerialization.RowKeySpace))
        {
            partition = ReadPlacementPartition(components, key, value, hasValue);
            return true;
        }
        return false;
    }

    private static PartitionRef ReadScopedPartition(object?[] components, ReadOnlySpan<byte> key)
    {
        if (components.Length < MinimumScopedComponentCount
            || components[TenantComponentIndex] is not string tenant
            || components[DatabaseComponentIndex] is not string database
            || components[DomainComponentIndex] is not string domain
            || components[PartitionKeyComponentIndex] is not string partitionKey)
        {
            throw Errors.Fail(ErrorCode.Corruption, AtomicPartitionRosterProtocol.MalformedScopedKey);
        }

        var candidate = new PartitionRef(tenant, database, domain, partitionKey);
        ValidateCanonicalPartition(candidate, components, key);
        return candidate;
    }

    private static PartitionRef ReadPlacementPartition(object?[] components, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value, bool hasValue)
    {
        if (components.Length != PlacementComponentCount
            || components[PlacementVersionIndex] is not string version
            || !StringComparer.Ordinal.Equals(version, AtomicPartitionPlacementSerialization.RowVersion)
            || components[PlacementTenantComponentIndex] is not string tenant
            || components[PlacementDatabaseComponentIndex] is not string database
            || components[PlacementDomainComponentIndex] is not string domain
            || components[PlacementPartitionKeyComponentIndex] is not string partitionKey)
        {
            throw Errors.Fail(ErrorCode.Corruption, AtomicPartitionRosterProtocol.MalformedPlacement);
        }

        var candidate = new PartitionRef(tenant, database, domain, partitionKey);
        ValidatePartition(candidate, AtomicPartitionRosterProtocol.MalformedPlacement);
        var canonicalKey = AtomicPartitionPlacementSerialization.RowKey(candidate);
        if (!canonicalKey.AsSpan().SequenceEqual(key))
        {
            throw Errors.Fail(ErrorCode.Corruption, AtomicPartitionRosterProtocol.MalformedPlacement);
        }
        if (hasValue)
        {
            ValidatePlacementValue(value, candidate);
        }
        return candidate;
    }

    private static bool IsCanonicalGlobalOutcome(object?[] components, ReadOnlySpan<byte> key)
    {
        if (components.Length != GlobalOutcomeComponentCount
            || components[GlobalOutcomePrincipalComponentIndex] is not string principal
            || components[GlobalOutcomeCommandComponentIndex] is not string commandText
            || !Guid.TryParse(commandText, out var commandId))
        {
            return false;
        }

        return KeySpace.GlobalOutcome(principal, commandId).AsSpan().SequenceEqual(key)
            || KeySpace.UnknownOutcome(principal, commandId).AsSpan().SequenceEqual(key);
    }

    private static void ValidateCanonicalPartition(PartitionRef candidate, object?[] components, ReadOnlySpan<byte> key)
    {
        ValidatePartition(candidate, AtomicPartitionRosterProtocol.MalformedScopedKey);
        if (!KeyCodec.Encode(components).AsSpan().SequenceEqual(key))
        {
            throw Errors.Fail(ErrorCode.Corruption, AtomicPartitionRosterProtocol.MalformedScopedKey);
        }
    }

    private static void ValidatePartition(PartitionRef candidate, string errorMessage)
    {
        try
        {
            DatabaseEngine.ValidatePartition(candidate);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.Corruption, errorMessage);
        }
    }

    private static void ValidatePlacementValue(ReadOnlySpan<byte> value, PartitionRef expectedPartition)
    {
        if (value.Length > AtomicPartitionPlacementProtocol.MaximumEncodedBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, AtomicPartitionRosterProtocol.MalformedPlacement);
        }
        AtomicPartitionPlacementV1? row;
        try
        {
            row = NativeSerialization.Deserialize<AtomicPartitionPlacementV1>(value);
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.FormatUnsupported)
        {
            throw Errors.Fail(ErrorCode.Corruption, AtomicPartitionRosterProtocol.MalformedPlacement);
        }
        if (row is null || row.Version != AtomicPartitionPlacementProtocol.CurrentVersion
            || row.Partition != expectedPartition)
        {
            throw Errors.Fail(ErrorCode.Corruption, AtomicPartitionRosterProtocol.MalformedPlacement);
        }
    }
}
