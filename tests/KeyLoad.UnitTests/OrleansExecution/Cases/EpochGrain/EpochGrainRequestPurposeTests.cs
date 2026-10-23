using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class EpochGrainRequestPurposeTests
{
    private const string UnsupportedPurposeOne = "unsupported-grain-purpose-one";
    private const string UnsupportedPurposeTwo = "unsupported-grain-purpose-two";
    private const string UnsupportedPurposeThree = "unsupported-grain-purpose-three";
    private const string CurrentPurpose = "keyload-grain-request-data-epoch7-rpc3";
    private const string PrincipalId = "root";
    private const int NoDtoMarker = 0;

    [Test]
    [Arguments(UnsupportedPurposeOne)]
    [Arguments(UnsupportedPurposeTwo)]
    [Arguments(UnsupportedPurposeThree)]
    public async Task AcEpoch005UnsupportedRequestPurposeFailsBeforeActorRouting(string rejectedPurpose)
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var payload = OrleansSerialization.Serialize(NoDtoMarker);
        var current = codec.CreateRead(requestId, PrincipalId, GrainReadKind.QueryCapabilities, payload);
        var envelope = codec.DecodeRead(current, requestId).Envelope;
        await Assert.That(envelope.Purpose).IsEqualTo(CurrentPurpose);
        await Assert.That(GrainContracts.EnvelopeAlias).IsEqualTo("keyload.request.envelope");

        var stale = envelope with
        {
            Purpose = rejectedPurpose
        };
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() => codec.DecodeRead(stale, requestId));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(GrainContracts.RequestPurpose).IsEqualTo(CurrentPurpose);
    }
}
