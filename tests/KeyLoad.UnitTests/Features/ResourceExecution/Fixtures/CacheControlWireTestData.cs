using System.Net;
using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class CacheControlWireTestData
{
    internal static CacheControlDigest ScopeHash => Digest(0x11);
    internal static CacheControlDigest PolicyHash => Digest(0x31);
    internal static CacheControlDigest NondefaultMac => Digest(0x71);

    internal static CachePhysicalBinding Binding(CacheVoterSlot slot)
        => new(slot, GuidFor(slot, 1), IncarnationId, Address(slot), GuidFor(slot, 3), CachePhysicalRole.Canonical);

    internal static CacheControlHeader Header(CacheControlOperation operation, CacheVoterSlot origin, CacheVoterSlot? target)
        => new(Version, operation, ScopeHash, PolicyHash, PolicyRevision, origin, target,
            SessionId, RoundNonce, RequestNonce, SentUnixMilliseconds);

    internal static CacheControlHeader RefreshHeader()
        => new(Version, CacheControlOperation.Refresh, ScopeHash, PolicyHash, PolicyRevision,
            CacheVoterSlot.Slot0, null, Guid.Empty, Guid.Empty, RequestNonce, SentUnixMilliseconds);

    internal static CacheReadyProof Proof(CacheVoterSlot origin, CacheVoterSlot bindingSlot, long sequence = ChallengeSequence)
        => new(Version, ScopeHash, PolicyHash, PolicyRevision, origin, SessionId, RoundNonce,
            ChallengeId, sequence, Binding(bindingSlot), CacheControlStatus.Ready, NondefaultMac);

    internal static CachePrepareRequest PrepareRequest()
        => new(Header(CacheControlOperation.Prepare, CacheVoterSlot.Slot0, CacheVoterSlot.Slot1),
            Address(CacheVoterSlot.Slot1), NondefaultMac);

    internal static CacheGrantRequest GrantRequest()
    {
        var target = Binding(CacheVoterSlot.Slot2);
        return new(Header(CacheControlOperation.Grant, CacheVoterSlot.Slot0, CacheVoterSlot.Slot2), GrantId,
            target, Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot0, Slot0ChallengeSequence),
            Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot1, Slot1ChallengeSequence),
            Proof(CacheVoterSlot.Slot0, CacheVoterSlot.Slot2, Slot2ChallengeSequence),
            NondefaultMac);
    }

    internal static CacheRevokeRequest RevokeRequest()
        => new(Header(CacheControlOperation.Revoke, CacheVoterSlot.Slot0, CacheVoterSlot.Slot0),
            GrantId, Binding(CacheVoterSlot.Slot0), NondefaultMac);

    internal static CacheRefreshHint RefreshHint()
        => new(RefreshHeader(), Binding(CacheVoterSlot.Slot0), NondefaultMac);

    internal static string Address(CacheVoterSlot slot)
        => SiloAddress.New(IPAddress.Parse($"127.0.0.{(int)slot + 1}"), 11111 + (int)slot, Generation).ToParsableString();

    internal static CacheControlDigest Digest(byte seed)
    {
        Span<byte> bytes = stackalloc byte[DigestLength];
        bytes.Clear();
        bytes[0] = seed;
        bytes[^1] = (byte)(seed + 1);
        return CacheControlDigest.FromBytes(bytes);
    }

    internal static Guid GuidFor(CacheVoterSlot slot, int suffix)
        => Guid.Parse($"01234567-89ab-cdef-8012-3456789abc{(int)slot}{suffix}");

    internal const byte Version = 1;
    internal const int Generation = 7;
    internal const long PolicyRevision = 4;
    internal const long ChallengeSequence = 12;
    internal const long Slot0ChallengeSequence = 12;
    internal const long Slot1ChallengeSequence = 13;
    internal const long Slot2ChallengeSequence = 14;
    internal const long SentUnixMilliseconds = 1_798_992_000_000;
    internal static readonly Guid SessionId = Guid.Parse("10203040-5060-7080-90a0-b0c0d0e00301");
    internal static readonly Guid RoundNonce = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff02");
    internal static readonly Guid RequestNonce = Guid.Parse("22334455-6677-8899-aabb-ccddeeff0303");
    internal static readonly Guid GrantId = Guid.Parse("32334455-6677-8899-aabb-ccddeeff0304");
    internal static readonly Guid ChallengeId = Guid.Parse("42334455-6677-8899-aabb-ccddeeff0305");
    internal static readonly Guid IncarnationId = Guid.Parse("52334455-6677-8899-aabb-ccddeeff0310");
    private const int DigestLength = 32;
}
