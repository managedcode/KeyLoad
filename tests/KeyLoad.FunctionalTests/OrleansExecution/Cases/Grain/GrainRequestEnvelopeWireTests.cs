using KeyLoad.Clustering;

namespace KeyLoad.FunctionalTests;

/// <summary>AC-ROUTE-001/AC-IS-001: execution envelope fields and purpose fencing remain stable.</summary>
internal sealed class GrainRequestEnvelopeWireTests
{
    private const string Principal = "root";
    private const string RequestIdentifier = "11111111-1111-1111-1111-111111111111";
    private const string IncarnationIdentifier = "22222222-2222-2222-2222-222222222222";
    private const string PayloadJson = "{}";
    private const string UnsupportedPurpose = "unsupported-grain-request-purpose";
    private const int ExpiryYear = 2099;
    private const int FirstDay = 1;
    private const int OffsetHours = 2;
    private const int NoDtoMarker = 0;

    [Test]
    public async Task EnvelopeRetainsEveryIdentityFieldAndExactTypedPayload()
    {
        var envelope = new GrainRequestEnvelope
        {
            Purpose = GrainContracts.RequestPurpose,
            RequestId = Guid.Parse(RequestIdentifier),
            Incarnation = Guid.Parse(IncarnationIdentifier),
            PrincipalId = Principal,
            ReadKind = GrainReadKind.Document,
            Payload = PayloadJson,
            ExpiresAt = new DateTimeOffset(ExpiryYear, FirstDay, FirstDay, 0, 0, 0, TimeSpan.FromHours(OffsetHours))
        };
        var encoded = OrleansSerialization.Serialize(envelope);
        var decoded = OrleansSerialization.Deserialize<GrainRequestEnvelope>(encoded);
        await Assert.That(OrleansSerialization.Measure(envelope)).IsEqualTo((long)encoded.Length);
        await Assert.That(decoded.Purpose).IsEqualTo(envelope.Purpose);
        await Assert.That(decoded.RequestId).IsEqualTo(envelope.RequestId);
        await Assert.That(decoded.Incarnation).IsEqualTo(envelope.Incarnation);
        await Assert.That(decoded.PrincipalId).IsEqualTo(envelope.PrincipalId);
        await Assert.That(decoded.ReadKind).IsEqualTo(envelope.ReadKind);
        await Assert.That(decoded.CommandKind).IsNull();
        await Assert.That(decoded.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(decoded.Payload).IsEqualTo(PayloadJson);
        await Assert.That(decoded.ExpiresAt).IsEqualTo(envelope.ExpiresAt);
        await Assert.That(decoded.ExpiresAt.Offset).IsEqualTo(envelope.ExpiresAt.Offset);
    }

    [Test]
    public async Task IssuedReadPreservesPayloadAndVersionedPurpose()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System, UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var payload = new GetDocumentRequest(new(fixture.Partition, Principal, Principal));
        var envelope = codec.CreateRead(requestId, Principal, GrainReadKind.Document, payload);
        var request = codec.DecodeRead(envelope, requestId);
        await Assert.That(request.Payload).IsEqualTo(payload);
        await Assert.That(request.Envelope.RequestId).IsEqualTo(requestId);
        await Assert.That(envelope.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(envelope.PrincipalId).IsEqualTo(Principal);
        await Assert.That(envelope.Purpose).IsEqualTo(GrainContracts.RequestPurpose);
    }

    [Test]
    public async Task MissingPayloadIsRejectedAtTheRealScopeBoundary()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System, UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var invalid = codec.CreateRead(requestId, Principal, GrainReadKind.QueryCapabilities, NoDtoMarker) with
        {
            Payload = null!
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.DecodeRead(invalid, requestId)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }

    [Test]
    public async Task UnsupportedPurposeIsInvalidated()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System, UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var envelope = codec.CreateRead(requestId, Principal, GrainReadKind.QueryCapabilities, NoDtoMarker) with
        {
            Purpose = UnsupportedPurpose
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.DecodeRead(envelope, requestId)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }
}
