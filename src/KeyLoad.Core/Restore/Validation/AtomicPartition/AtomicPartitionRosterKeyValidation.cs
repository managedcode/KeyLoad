using KeyLoad.Core.Features.BackupRestore.Serialization;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;
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
    private const int GlobalOutcomePrincipalComponentIndex = 2;
    private const int GlobalOutcomeCommandComponentIndex = 3;
    private const int GlobalOutcomeComponentCount = 4;

    internal static bool TryReadCandidate(ReadOnlySpan<byte> key, out PartitionRef? partition)
    {
        var components = KeyCodec.Decode(key);
        partition = null;
        if (components.Length == NoComponents || components[FamilyComponentIndex] is not string family)
        {
            return false;
        }

        if (StringComparer.Ordinal.Equals(family, PartitionRecordFamilies.Outcome)
            && IsCanonicalGlobalOutcome(components, key))
        {
            return false;
        }

        if (PartitionRecordFamilies.All.Contains(family, StringComparer.Ordinal))
        {
            partition = ReadScopedPartition(components, key);
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
}
