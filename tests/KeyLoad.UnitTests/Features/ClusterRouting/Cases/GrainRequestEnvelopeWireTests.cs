using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests;

/// <summary>AC-ROUTE-001/AC-IS-001: native envelope fields remain stable and legacy signed JSON is explicitly invalidated.</summary>
internal sealed class GrainRequestEnvelopeWireTests
{
    private const string Principal = "root";
    private const string RequestIdentifier = "11111111-1111-1111-1111-111111111111";
    private const string IncarnationIdentifier = "22222222-2222-2222-2222-222222222222";
    private const string PayloadJson = "{}";
    private const string OriginalJson = """{"purpose":"keyload-grain-request-v1","requestId":"11111111-1111-1111-1111-111111111111","incarnation":"22222222-2222-2222-2222-222222222222","principalId":"root","readKind":"Document","commandKind":null,"commandId":"00000000-0000-0000-0000-000000000000","payloadBase64Url":"e30","expiresAt":"2099-01-01T00:00:00+02:00"}""";
    private const string WireMember = "\"payloadBase64Url\":\"e30\",";
    private const string NullWireMember = "\"payloadBase64Url\":null,";
    private const string LegacySeparator = ".";
    private const string LegacyPurpose = "keyload-grain-request-v1";
    private const int ExpiryYear = 2099;
    private const int FirstDay = 1;
    private const int OffsetHours = 2;
    private const int NoDtoMarker = 0;

    [Test]
    public async Task NativeEnvelopeRetainsEveryIdentityFieldAndExactTypedPayload()
    {
        var payload = NativeSerialization.Serialize(PayloadJson);
        var envelope = new GrainRequestEnvelope
        {
            Purpose = GrainNativeContracts.RequestPurpose,
            RequestId = Guid.Parse(RequestIdentifier),
            Incarnation = Guid.Parse(IncarnationIdentifier),
            PrincipalId = Principal,
            ReadKind = GrainReadKind.Document,
            Payload = payload,
            ExpiresAt = new DateTimeOffset(ExpiryYear, FirstDay, FirstDay, 0, 0, 0, TimeSpan.FromHours(OffsetHours))
        };
        var encoded = NativeSerialization.Serialize(envelope);
        var decoded = NativeSerialization.Deserialize<GrainRequestEnvelope>(encoded);
        await Assert.That(NativeSerialization.Measure(envelope)).IsEqualTo((long)encoded.Length);
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
    public async Task ActualDatabaseSignedReadPreservesNativePayloadAndVersionedPurpose()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System, UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var payload = NativeSerialization.Serialize(new GetDocumentRequest(new(fixture.Partition, Principal, Principal)));
        var token = codec.CreateRead(requestId, Principal, GrainReadKind.Document, payload);
        var envelope = fixture.Database.Verify<GrainRequestEnvelope>(token);
        var request = codec.VerifyRead(token, requestId);
        await Assert.That(token.StartsWith(GrainNativeContracts.SignedTokenPrefix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(request.Payload.Span.SequenceEqual(payload)).IsTrue();
        await Assert.That(request.Envelope.RequestId).IsEqualTo(requestId);
        await Assert.That(envelope.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(envelope.PrincipalId).IsEqualTo(Principal);
        await Assert.That(envelope.Purpose).IsEqualTo(GrainNativeContracts.RequestPurpose);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SignedLegacyNullOrMissingPayloadIsInvalidatedWithoutJsonFallback(bool missing)
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System, UnitRoutingOptions.Routing());
        var json = OriginalJson.Replace(WireMember, missing ? string.Empty : NullWireMember, StringComparison.Ordinal);
        await AssertLegacyRejectedAsync(fixture, codec, json);
    }

    [Test]
    public async Task GenuineLegacyJsonEnvelopeSignatureIsExplicitlyInvalidated()
    {
        using var fixture = new TestDatabase();
        await AssertLegacyRejectedAsync(fixture, new(fixture.Database, TimeProvider.System), OriginalJson);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DefaultOrEmptyNativePayloadIsRejectedAtTheRealScopeBoundary(bool missing)
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System, UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var token = codec.CreateRead(requestId, Principal, GrainReadKind.QueryCapabilities, NativeSerialization.Serialize(NoDtoMarker));
        var envelope = fixture.Database.Verify<GrainRequestEnvelope>(token) with
        { Payload = missing ? default : ReadOnlyMemory<byte>.Empty };
        var invalid = fixture.Database.Sign(envelope);
        await Assert.That(fixture.Database.Verify<GrainRequestEnvelope>(invalid).Payload.IsEmpty).IsTrue();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(invalid, requestId)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }

    [Test]
    public async Task NativeSignatureWithLegacyPurposeIsAlsoInvalidated()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System, UnitRoutingOptions.Routing());
        var requestId = Guid.NewGuid();
        var token = codec.CreateRead(requestId, Principal, GrainReadKind.QueryCapabilities, NativeSerialization.Serialize(NoDtoMarker));
        var envelope = fixture.Database.Verify<GrainRequestEnvelope>(token) with { Purpose = LegacyPurpose };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(fixture.Database.Sign(envelope), requestId)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }

    private static async Task AssertLegacyRejectedAsync(TestDatabase fixture, GrainRequestCodec codec, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var token = Base64Url.EncodeToString(bytes) + LegacySeparator
            + Base64Url.EncodeToString(HMACSHA256.HashData(fixture.Store.Identity.SigningKey.Span, bytes));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Verify<GrainRequestEnvelope>(token)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(token, Guid.Parse(RequestIdentifier))).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }
}
