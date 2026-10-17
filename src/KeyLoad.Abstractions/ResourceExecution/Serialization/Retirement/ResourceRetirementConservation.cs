using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Shared bounded native conservation encoding, with no storage authority or Core dependency.</summary>
internal static class ResourceRetirementConservation
{
    private const int Version = 1;
    private const long EmptyRecords = 0;
    private const long RecordStep = 1;
    private const int FirstVoterIndex = 0;
    private const int SecondVoterIndex = 1;
    private const int ThirdVoterIndex = 2;
    private const int DigestBytes = 32;
    private const int IncarnationCharacters = 64;
    private const int VoterCount = 3;
    private const string Domain = "keyload.resource-retirement.conservation-step.v1";
    private const string Invalid = "The native retirement conservation proof is invalid.";

    internal static ResourceRetirementConservationFloorV1 Initial(PhysicalShardRecord owner)
    {
        ValidateOwner(owner);
        return new(Version, owner, EmptyRecords, EmptyRecords, new byte[DigestBytes])
        {
            AuthorityRecordCount = EmptyRecords,
            AuthorityDigest = new byte[DigestBytes]
        };
    }

    internal static void Validate(ResourceRetirementConservationFloorV1 floor)
    {
        if (floor is null || floor.Version != Version || floor.Revision < EmptyRecords
            || floor.RetiredLifetimeCount != floor.Revision || floor.Digest.Length != DigestBytes
            || floor.Revision == EmptyRecords && !floor.Digest.Span.SequenceEqual(new byte[DigestBytes]))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        ValidateOwner(floor.PhysicalOwner);
        new ResourceRetirementAuthoritySummary(floor.AuthorityRecordCount, floor.AuthorityDigest).RequireValid();
    }

    internal static ResourceRetirementConservationStepV1 CreateStep(ResourceRetirementConservationFloorV1 before,
        ResourceLifetimeV1 lifetime, Guid retirementId, Guid releaseCommandId, long appliedPosition, ResourceRetirementAuthoritySummary addedAuthority)
    {
        Validate(before);
        if (before.Revision == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, Invalid);
        }
        addedAuthority.RequireValid();
        var step = new ResourceRetirementConservationStepV1(Version, before.PhysicalOwner,
            checked(before.Revision + RecordStep), lifetime, retirementId, releaseCommandId, appliedPosition,
            before.Digest.ToArray(), ReadOnlyMemory<byte>.Empty)
        {
            AddedAuthorityRecords = addedAuthority.Records,
            AddedAuthorityDigest = addedAuthority.Digest.ToArray()
        };
        return step with
        {
            Digest = Compute(step)
        };
    }

    internal static ResourceRetirementConservationFloorV1 Advance(ResourceRetirementConservationFloorV1 before,
        ResourceRetirementConservationStepV1 step)
    {
        Validate(before);
        if (step is null || before.Revision == long.MaxValue || step.Revision != before.Revision + RecordStep
            || !SameOwner(before.PhysicalOwner, step.PhysicalOwner)
            || step.PreviousDigest.Length != DigestBytes || step.Digest.Length != DigestBytes
            || !CryptographicOperations.FixedTimeEquals(before.Digest.Span, step.PreviousDigest.Span)
            || !CryptographicOperations.FixedTimeEquals(Compute(step), step.Digest.Span))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var authority = new ResourceRetirementAuthorityAccumulator();
        authority.Add(new ResourceRetirementAuthoritySummary(before.AuthorityRecordCount, before.AuthorityDigest));
        authority.Add(new ResourceRetirementAuthoritySummary(step.AddedAuthorityRecords, step.AddedAuthorityDigest));
        var summary = authority.Complete();
        return new(Version, step.PhysicalOwner, step.Revision, checked(before.RetiredLifetimeCount + RecordStep),
            step.Digest.ToArray())
        {
            AuthorityRecordCount = summary.Records,
            AuthorityDigest = summary.Digest,
            BaselineOriginKey = before.BaselineOriginKey.ToArray()
        };
    }

    internal static bool Same(ResourceRetirementConservationFloorV1 left, ResourceRetirementConservationFloorV1 right)
    {
        Validate(left);
        Validate(right);
        return SameOwner(left.PhysicalOwner, right.PhysicalOwner) && left.Revision == right.Revision
            && left.RetiredLifetimeCount == right.RetiredLifetimeCount
            && CryptographicOperations.FixedTimeEquals(left.Digest.Span, right.Digest.Span)
            && new ResourceRetirementAuthoritySummary(left.AuthorityRecordCount, left.AuthorityDigest)
                .Same(new ResourceRetirementAuthoritySummary(right.AuthorityRecordCount, right.AuthorityDigest))
            && left.BaselineOriginKey.Span.SequenceEqual(right.BaselineOriginKey.Span);
    }

    internal static void ValidateStep(ResourceRetirementConservationStepV1 step)
    {
        if (step is null || step.Digest.Length != DigestBytes
            || !CryptographicOperations.FixedTimeEquals(Compute(step), step.Digest.Span))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }

    private static byte[] Compute(ResourceRetirementConservationStepV1 step)
    {
        if (step is null || step.Version != Version || step.Revision <= EmptyRecords || step.Lifetime is null
            || step.RetirementId == Guid.Empty || step.ReleaseCommandId == Guid.Empty || step.AppliedPosition <= EmptyRecords
            || step.PreviousDigest.Length != DigestBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        ValidateOwner(step.PhysicalOwner);
        new ResourceRetirementAuthoritySummary(step.AddedAuthorityRecords, step.AddedAuthorityDigest).RequireValid();
        var lifetime = step.Lifetime;
        if (string.IsNullOrWhiteSpace(lifetime.TenantId) || string.IsNullOrWhiteSpace(lifetime.DatabaseId)
            || string.IsNullOrWhiteSpace(lifetime.ResourceName) || string.IsNullOrWhiteSpace(lifetime.TransactionDomainId)
            || lifetime.Kind != ResourceKind.Collection || lifetime.Incarnation is null
            || !Digest(lifetime.Incarnation.Value) || !Digest(lifetime.CreationFingerprint))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        var owner = step.PhysicalOwner;
        return SHA256.HashData(KeyCodec.Encode(Domain, Version, owner.PhysicalShardId, owner.Incarnation,
            owner.PlacementEpoch, VoterCount, owner.VoterIds[FirstVoterIndex], owner.VoterIds[SecondVoterIndex], owner.VoterIds[ThirdVoterIndex],
            step.Revision, lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName,
            lifetime.TransactionDomainId, (int)lifetime.Kind, lifetime.Incarnation.Value,
            lifetime.CreationFingerprint, step.RetirementId, step.ReleaseCommandId, step.AppliedPosition,
            step.PreviousDigest.ToArray(), step.AddedAuthorityRecords, step.AddedAuthorityDigest.ToArray()));
    }

    internal static void ValidateOwner(PhysicalShardRecord owner)
    {
        if (owner is null || owner.PhysicalShardId == Guid.Empty || owner.Incarnation == Guid.Empty
            || owner.PlacementEpoch <= EmptyRecords || owner.VoterIds.IsDefault || owner.VoterIds.Length != VoterCount
            || owner.VoterIds.Any(string.IsNullOrWhiteSpace)
            || owner.VoterIds.Distinct(StringComparer.Ordinal).Count() != VoterCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }

    internal static bool SameOwner(PhysicalShardRecord left, PhysicalShardRecord right)
        => left is not null && right is not null && left.PhysicalShardId == right.PhysicalShardId
            && left.Incarnation == right.Incarnation && left.PlacementEpoch == right.PlacementEpoch
            && !left.VoterIds.IsDefault && !right.VoterIds.IsDefault
            && left.VoterIds.SequenceEqual(right.VoterIds, StringComparer.Ordinal);

    private static bool Digest(string value)
        => value is { Length: IncarnationCharacters } && value.All(char.IsAsciiHexDigitLower);
}
