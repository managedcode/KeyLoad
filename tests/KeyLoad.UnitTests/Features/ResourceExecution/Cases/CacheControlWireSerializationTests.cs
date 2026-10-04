using System.Net;
using KeyLoad.Orleans.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireSerializationTests
{

    [Test]
    public async Task AcCache014AllThirteenImmutableRecordsRoundTripThroughNativeOrleansSerialization()
    {
        using var services = CreateSerializerServices();
        var digest = CacheControlDigest.FromBytes(DigestBytes);
        var binding = Binding(CacheVoterSlot.Slot1);
        var header = Header(CacheControlOperation.Prepare, CacheVoterSlot.Slot0, CacheVoterSlot.Slot1);
        var proof = Proof(binding, digest, CacheVoterSlot.Slot0);
        var request = new CachePrepareRequest(header, CanonicalSiloAddress(CacheVoterSlot.Slot1), digest);
        var correlation = new CacheReplyCorrelation(header, digest);
        var grantHeader = Header(CacheControlOperation.Grant, CacheVoterSlot.Slot0, CacheVoterSlot.Slot2);
        var grantTarget = Binding(CacheVoterSlot.Slot2);
        var grantRequest = new CacheGrantRequest(grantHeader, GrantId, grantTarget,
            Proof(Binding(CacheVoterSlot.Slot0), digest, CacheVoterSlot.Slot0),
            Proof(Binding(CacheVoterSlot.Slot1), digest, CacheVoterSlot.Slot0),
            Proof(grantTarget, digest, CacheVoterSlot.Slot0), digest);
        var revokeHeader = Header(CacheControlOperation.Revoke, CacheVoterSlot.Slot0, CacheVoterSlot.Slot1);
        var revokeRequest = new CacheRevokeRequest(revokeHeader, GrantId, binding, digest);
        var refreshHeader = RefreshHeader();
        var refreshHint = new CacheRefreshHint(refreshHeader, Binding(CacheVoterSlot.Slot0), digest);
        var grantCorrelation = new CacheReplyCorrelation(grantHeader, digest);
        var revokeCorrelation = new CacheReplyCorrelation(revokeHeader, digest);
        var refreshCorrelation = new CacheReplyCorrelation(refreshHeader, digest);
        var grantReply = new CacheGrantReply(grantCorrelation, CacheControlStatus.AcceptedActive, GrantId,
            grantTarget, ChallengeSequence, digest);
        var revokeReply = new CacheRevokeReply(revokeCorrelation, CacheControlStatus.Revoked,
            GrantId, CacheRevokeEffect.Both, digest);
        var refreshReceipt = new CacheRefreshReceipt(refreshCorrelation, CacheControlStatus.HintAcknowledged,
            SessionId, RoundNonce, CanonicalSiloAddress(CacheVoterSlot.Slot0), digest);

        await AssertNativeRoundTripAsync(services, digest);
        await AssertNativeRoundTripAsync(services, binding);
        await AssertNativeRoundTripAsync(services, header);
        await AssertNativeRoundTripAsync(services, proof);
        await AssertNativeRoundTripAsync(services, request);
        await AssertNativeRoundTripAsync(services, grantRequest);
        await AssertNativeRoundTripAsync(services, revokeRequest);
        await AssertNativeRoundTripAsync(services, refreshHint);
        await AssertNativeRoundTripAsync(services, correlation);
        await AssertNativeRoundTripAsync(services, new CachePrepareReply(correlation, CacheControlStatus.Ready, proof, digest));
        await AssertNativeRoundTripAsync(services, grantReply);
        await AssertNativeRoundTripAsync(services, revokeReply);
        await AssertNativeRoundTripAsync(services, refreshReceipt);
    }

    private static readonly byte[] DigestBytes =
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
    ];

    private static readonly Guid SessionId = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid RoundNonce = Guid.Parse("00000000-0000-0000-0000-000000000102");
    private static readonly Guid RequestNonce = Guid.Parse("00000000-0000-0000-0000-000000000103");
    private static readonly Guid GrantId = Guid.Parse("00000000-0000-0000-0000-000000000104");
    private static readonly Guid ChallengeId = Guid.Parse("00000000-0000-0000-0000-000000000105");
    private const long PolicyRevision = 7;
    private const long ChallengeSequence = 11;
    private const long SentUnixMilliseconds = 1_798_992_000_000;

    private static ServiceProvider CreateSerializerServices()
    {
        var collection = new ServiceCollection();
        collection.AddSerializer(builder => builder.AddAssembly(typeof(CacheControlDigest).Assembly));
        return collection.BuildServiceProvider();
    }

    private static async Task AssertNativeRoundTripAsync<T>(IServiceProvider services, T value)
    {
        var serializer = services.GetRequiredService<Serializer<T>>();
        var bytes = serializer.SerializeToArray(value);
        var restored = serializer.Deserialize(bytes);
        await Assert.That(restored).IsEqualTo(value);
    }

    private static CachePhysicalBinding Binding(CacheVoterSlot slot)
        => new(slot, GuidFor(slot, 1), IncarnationId, CanonicalSiloAddress(slot), GuidFor(slot, 3), CachePhysicalRole.Canonical);

    private static CacheControlHeader Header(CacheControlOperation operation, CacheVoterSlot origin, CacheVoterSlot target)
        => new(1, operation, CacheControlDigest.FromBytes(DigestBytes), CacheControlDigest.FromBytes(PolicyDigestBytes),
            PolicyRevision, origin, target, SessionId, RoundNonce, RequestNonce, SentUnixMilliseconds);

    private static CacheControlHeader RefreshHeader()
        => new(1, CacheControlOperation.Refresh, CacheControlDigest.FromBytes(DigestBytes),
            CacheControlDigest.FromBytes(PolicyDigestBytes), PolicyRevision, CacheVoterSlot.Slot0, null,
            Guid.Empty, Guid.Empty, RequestNonce, SentUnixMilliseconds);

    private static CacheReadyProof Proof(CachePhysicalBinding binding, CacheControlDigest digest, CacheVoterSlot originSlot)
        => new(1, digest, CacheControlDigest.FromBytes(PolicyDigestBytes), PolicyRevision, originSlot,
            SessionId, RoundNonce, ChallengeId, ChallengeSequence, binding, CacheControlStatus.Ready, digest);

    private static Guid GuidFor(CacheVoterSlot slot, int suffix)
        => Guid.Parse($"00000000-0000-000{(int)slot}-0000-000000000{suffix:000}");

    private static readonly Guid IncarnationId = Guid.Parse("00000000-0000-0000-0000-000000000110");

    private static string CanonicalSiloAddress(CacheVoterSlot slot)
        => SiloAddress.New(IPAddress.Parse($"127.0.0.{(int)slot + 1}"), 11111 + (int)slot, 1).ToParsableString();

    private static readonly byte[] PolicyDigestBytes =
    [
        0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
        0x28, 0x29, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F,
        0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37,
        0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F
    ];
}
