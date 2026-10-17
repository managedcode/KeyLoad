using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

/// <summary>Fences private benchmark authority in the existing node-owned replica store.</summary>
internal static class ReplicaBenchmarkMembership
{
    private const string MembershipKeyName = "replica-benchmark-membership";
    private const string InvalidMembership = "The persisted benchmark replica authority does not match this startup.";
    private const string CorruptMembership = "The persisted benchmark replica authority is inconsistent or corrupt.";
    private const int MinimumVoters = 1;
    internal static readonly byte[] StorageKey = KeyCodec.Encode(MembershipKeyName);

    internal static void Validate(byte[]? bytes, bool hasHardState, ReplicaConfiguration configuration)
    {
        if (bytes is null)
        {
            if (hasHardState)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidMembership);
            }
            return;
        }
        if (!hasHardState)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptMembership);
        }
        var maximumVoters = Math.Max(configuration.MaxAppendEntries, configuration.VoterIds.Length);
        var membership = Decode(bytes, maximumVoters);
        if (membership.Version != ReplicaProtocol.FormatVersion)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, ReplicaProtocol.UnsupportedFormat);
        }
        if (membership.Incarnation == Guid.Empty
            || membership.VoterIds.IsDefault || membership.VoterIds.Length < MinimumVoters || membership.VoterIds.Length > maximumVoters
            || membership.VoterIds.Any(string.IsNullOrWhiteSpace)
            || membership.VoterIds.Distinct(StringComparer.Ordinal).Count() != membership.VoterIds.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptMembership);
        }
        if (membership.Incarnation != configuration.Incarnation
            || !membership.VoterIds.SequenceEqual(configuration.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidMembership);
        }
    }

    internal static void Initialize(IAtomicTransaction transaction, ReplicaConfiguration configuration)
    {
        transaction.Put(StorageKey, ReplicaProtocolCodec.Serialize(new ReplicaBenchmarkMembershipRecord(ReplicaProtocol.FormatVersion,
            configuration.Incarnation, configuration.VoterIds)));
    }

    private static ReplicaBenchmarkMembershipRecord Decode(byte[] bytes, int maximumVoters)
    {
        try
        {
            return ReplicaProtocolCodec.DeserializeStored<ReplicaBenchmarkMembershipRecord>(bytes, maximumVoters);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptMembership);
        }
    }
}

[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.BenchmarkMembership)]
internal sealed record ReplicaBenchmarkMembershipRecord([property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid Incarnation, [property: Orleans.Id(2)] ImmutableArray<string> VoterIds);
