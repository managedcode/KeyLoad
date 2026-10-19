using KeyLoad.Clustering;

namespace KeyLoad.UnitTests;

/// <summary>AC-ROUTE-001/AC-IS-001: execution envelope fields and signed-purpose fencing remain stable.</summary>
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
        var payload = OrleansSerialization.Serialize(PayloadJson);
        var envelope = new GrainRequestEnvelope
        {
            Purpose = GrainContracts.RequestPurpose,
            RequestId = Guid.Parse(RequestIdentifier),
            Incarnation = Guid.Parse(IncarnationIdentifier),
            PrincipalId = Principal,
            ReadKind = GrainReadKind.Document,
            Payload = payload,
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
        await Assert.That(decoded.Payload.Span.SequenceEqual(payload)).IsTrue();
        await Assert.That(decoded.ExpiresAt).IsEqualTo(envelope.ExpiresAt);
        await Assert.That(decoded.ExpiresAt.Offset).IsEqualTo(envelope.ExpiresAt.Offset);
    }

    [Test]
    public async Task ActualDatabaseSignedReadPreservesPayloadAndVersionedPurpose()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, fixture.Database.Store.Identity.SigningKey,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var payload = OrleansSerialization.Serialize(new GetDocumentRequest(new(fixture.Partition, Principal, Principal)));
        var token = codec.CreateRead(requestId, Principal, GrainReadKind.Document, payload);
        var envelope = fixture.Database.Verify<GrainRequestEnvelope>(token);
        var request = codec.VerifyRead(token, requestId);
        await Assert.That(token.StartsWith(GrainContracts.SignedTokenPrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(request.Payload.Span.SequenceEqual(payload)).IsTrue();
        await Assert.That(request.Envelope.RequestId).IsEqualTo(requestId);
        await Assert.That(envelope.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(envelope.PrincipalId).IsEqualTo(Principal);
        await Assert.That(envelope.Purpose).IsEqualTo(GrainContracts.RequestPurpose);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DefaultOrEmptyPayloadIsRejectedAtTheRealScopeBoundary(bool missing)
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, fixture.Database.Store.Identity.SigningKey,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var token = codec.CreateRead(requestId, Principal, GrainReadKind.QueryCapabilities, OrleansSerialization.Serialize(NoDtoMarker));
        var envelope = fixture.Database.Verify<GrainRequestEnvelope>(token) with
        {
            Payload = missing ? default : ReadOnlyMemory<byte>.Empty
        };
        var invalid = fixture.Database.Sign(envelope);
        await Assert.That(fixture.Database.Verify<GrainRequestEnvelope>(invalid).Payload.IsEmpty).IsTrue();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(invalid, requestId)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }

    [Test]
    public async Task SignatureWithUnsupportedPurposeIsInvalidated()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, fixture.Database.Store.Identity.SigningKey,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var token = codec.CreateRead(requestId, Principal, GrainReadKind.QueryCapabilities, OrleansSerialization.Serialize(NoDtoMarker));
        var envelope = fixture.Database.Verify<GrainRequestEnvelope>(token) with
        {
            Purpose = UnsupportedPurpose
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(fixture.Database.Sign(envelope), requestId)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }
}
