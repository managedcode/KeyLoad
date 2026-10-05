using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class EpochGrainRequestPurposeTests
{
    private const string LegacyPurpose = "keyload-grain-request-v2";
    private const string CurrentPurpose = "keyload-grain-request-data-epoch7-rpc3";
    private const string PrincipalId = "root";
    private const int NoDtoMarker = 0;

    [Test]
    [Arguments(LegacyPurpose)]
    [Arguments("keyload-grain-request-data-epoch6")]
    [Arguments("keyload-grain-request-data-epoch6-rpc2")]
    public async Task AcEpoch005OldSignedRequestPurposeFailsBeforeActorRouting(string rejectedPurpose)
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System, UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var payload = NativeSerialization.Serialize(NoDtoMarker);
        var current = codec.CreateRead(requestId, PrincipalId, GrainReadKind.QueryCapabilities, payload);
        var envelope = codec.VerifyRead(current, requestId).Envelope;
        await Assert.That(envelope.Purpose).IsEqualTo(CurrentPurpose);
        await Assert.That(GrainNativeContracts.SignedTokenPrefix).IsEqualTo("KLT2.");
        await Assert.That(GrainNativeContracts.EnvelopeAlias).IsEqualTo("keyload.request.envelope.v2");

        var stale = fixture.Database.Sign(envelope with { Purpose = rejectedPurpose });
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(stale, requestId));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(GrainNativeContracts.RequestPurpose).IsEqualTo(CurrentPurpose);
    }
}
