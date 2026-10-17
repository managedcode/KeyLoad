using System.Text;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireShape(IOptions<RequestProbeExecutionOptions> limits)
{
    private const long InitialPolicyEpoch = 0;
    private const int FirstArmIndex = 0;
    private const int EmptyCount = 0;
    private const int DigestHexCharacters = 64;
    private const char FirstDecimalHexCharacter = '0';
    private const char LastDecimalHexCharacter = '9';
    private const char FirstLowercaseHexCharacter = 'a';
    private const char LastLowercaseHexCharacter = 'f';
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal void RequireOwner(NativeBlobWireOwner owner)
    {
        Version(owner.Version);
        if (!RequestCqrsProbeOptionsReader.IsSessionId(owner.SessionId))
        {
            throw Invalid();
        }
        Text(owner.ClusterId);
        Tuple(owner.ControlOwner);
        Tuple(owner.SourceOwner);
        Tuple(owner.DestinationOwner);
        if (!Same(owner.ControlOwner, owner.SourceOwner)
            || owner.SourceOwner.PhysicalShardId == owner.DestinationOwner.PhysicalShardId
            || owner.SourceOwner.Incarnation == owner.DestinationOwner.Incarnation
            || owner.SourceOwner.VoterIds.Intersect(owner.DestinationOwner.VoterIds, StringComparer.Ordinal).Any())
        {
            throw Invalid();
        }
    }

    internal void RequireArms(NativeBlobWireOwner owner, string ownerDigest, NativeBlobWireArms arms)
    {
        Version(arms.Version);
        if (arms.SessionId != owner.SessionId || arms.OwnerDigest != ownerDigest || arms.MoveId == Guid.Empty
            || arms.PolicyEpoch <= InitialPolicyEpoch || arms.ResourceKind != ResourceKind.BlobStore
            || arms.Partition is null || arms.TransactionDomainId != arms.Partition.TransactionDomainId
            || arms.TenantId != arms.Partition.TenantId || arms.Arms is null
            || arms.Arms.Length != NativeBlobWireProbeProtocol.IdentityCount
            || arms.Arms.Length > limits.Value.MaximumArms)
        {
            throw Invalid();
        }
        Text(arms.PrincipalId);
        Text(arms.TenantId);
        Text(arms.ResourceName);
        Text(arms.Partition.DatabaseId);
        Text(arms.Partition.PartitionKey);
        Text(arms.TransactionDomainId);
        for (var index = FirstArmIndex; index < arms.Arms.Length; index++)
        {
            var arm = arms.Arms[index] ?? throw Invalid();
            NativeBlobWireIdentityRules.Require(arm);
            if ((int)arm.Identity != index)
            {
                throw Invalid();
            }
            Text(arm.BlobId);
        }
        if (arms.Arms.Select(arm => arm.ArmId).Distinct().Count() != arms.Arms.Length
            || arms.Arms.Where(arm => arm.OriginalCommandId.HasValue)
                .Select(arm => arm.OriginalCommandId).Distinct().Count() != (int)NativeBlobWireIdentity.Metadata)
        {
            throw Invalid();
        }
    }

    internal void Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Utf8.GetByteCount(value) > limits.Value.MaximumPrincipalBytes)
        {
            throw Invalid();
        }
    }

    internal static bool IsDigest(string? value)
        => value is { Length: DigestHexCharacters } && value.All(character => character is >= FirstDecimalHexCharacter and <= LastDecimalHexCharacter
            or >= FirstLowercaseHexCharacter and <= LastLowercaseHexCharacter);

    internal static bool Same(NativeBlobWireOwnerTuple first, NativeBlobWireOwnerTuple second)
        => first.PhysicalShardId == second.PhysicalShardId && first.Incarnation == second.Incarnation
            && first.VoterIds.SequenceEqual(second.VoterIds, StringComparer.Ordinal);

    internal static bool Same(NativeBlobWireOwnerTuple expected, PhysicalShardRecord actual)
        => expected.PhysicalShardId == actual.PhysicalShardId && expected.Incarnation == actual.Incarnation
            && expected.VoterIds.SequenceEqual(actual.VoterIds, StringComparer.Ordinal);

    private void Tuple(NativeBlobWireOwnerTuple tuple)
    {
        if (tuple is null || tuple.PhysicalShardId == Guid.Empty || tuple.Incarnation == Guid.Empty
            || tuple.VoterIds is null || tuple.VoterIds.Length == EmptyCount
            || tuple.VoterIds.Distinct(StringComparer.Ordinal).Count() != tuple.VoterIds.Length)
        {
            throw Invalid();
        }
        foreach (var voter in tuple.VoterIds)
        {
            Text(voter);
        }
    }

    private static void Version(int version)
    {
        if (version != NativeBlobWireProbeProtocol.Version)
        {
            throw Invalid();
        }
    }
    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidOwner);
}
