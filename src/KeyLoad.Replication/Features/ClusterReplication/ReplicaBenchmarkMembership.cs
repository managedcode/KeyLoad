using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

/// <summary>Fences private benchmark authority in the existing node-owned replica store.</summary>
internal static class ReplicaBenchmarkMembership
{
    private const string MembershipKeyName = "replica-benchmark-membership";
    private const string InvalidMembership = "The persisted benchmark replica authority does not match this startup.";
    private const string CorruptMembership = "The persisted benchmark replica authority is inconsistent or corrupt.";
    private const int FormatVersion = 1;
    private const int MinimumVoters = 1;
    private const int MaximumVoters = 3;
    private static readonly JsonSerializerOptions Options = new(JsonDefaults.Options) { AllowDuplicateProperties = false };
    internal static readonly byte[] StorageKey = KeyCodec.Encode(MembershipKeyName);

    internal static void Validate(byte[]? bytes, bool hasHardState, ReplicaConfiguration configuration)
    {
        if (bytes is null)
        {
            if (hasHardState && configuration.BenchmarkTopology)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidMembership);
            }
            return;
        }
        if (!hasHardState)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptMembership);
        }
        var membership = Decode(bytes);
        if (membership.Version != FormatVersion || membership.Incarnation == Guid.Empty
            || membership.VoterIds.IsDefault || membership.VoterIds.Length is < MinimumVoters or > MaximumVoters
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
        if (configuration.BenchmarkTopology)
        {
            transaction.PutRecord(StorageKey, new ReplicaBenchmarkMembershipRecord(FormatVersion,
                configuration.Incarnation, configuration.VoterIds));
        }
    }

    private static ReplicaBenchmarkMembershipRecord Decode(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<ReplicaBenchmarkMembershipRecord>(bytes, Options)
                ?? throw Errors.Fail(ErrorCode.Corruption, CorruptMembership);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptMembership);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw Errors.Fail(ErrorCode.Corruption, CorruptMembership);
        }
    }
}

internal sealed record ReplicaBenchmarkMembershipRecord(int Version, Guid Incarnation, ImmutableArray<string> VoterIds);
