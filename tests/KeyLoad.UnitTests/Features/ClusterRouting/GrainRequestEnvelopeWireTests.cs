using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests;

/// <summary>AC-ROUTE-001 and AC-ROC-002/005: the renamed CLR payload member preserves original signed envelope JSON bytes.</summary>
internal sealed class GrainRequestEnvelopeWireTests
{
    private const string Principal = "root";
    private const string RequestIdentifier = "11111111-1111-1111-1111-111111111111";
    private const string IncarnationIdentifier = "22222222-2222-2222-2222-222222222222";
    private const string PayloadJson = "{}";
    private const string EncodedJson = "e30";
    private const string WireProperty = "payloadBase64Url";
    private const string RenamedProperty = "encodedPayload";
    private const string WireMember = "\"payloadBase64Url\":\"e30\",";
    private const string NullWireMember = "\"payloadBase64Url\":null,";
    private const char SignatureSeparator = '.';
    private const string OriginalJson = """{"purpose":"keyload-grain-request-v1","requestId":"11111111-1111-1111-1111-111111111111","incarnation":"22222222-2222-2222-2222-222222222222","principalId":"root","readKind":"Document","commandKind":null,"commandId":"00000000-0000-0000-0000-000000000000","payloadBase64Url":"e30","expiresAt":"2099-01-01T00:00:00+02:00"}""";

    /// <summary>Handcrafted original field names, order, enums, nulls, GUIDs and DateTimeOffset bytes survive round-trip unchanged.</summary>
    [Test]
    public async Task OriginalEnvelopeJsonRetainsEveryByteAndPayloadWireName()
    {
        var original = Encoding.UTF8.GetBytes(OriginalJson);
        var envelope = JsonDefaults.Deserialize<GrainRequestEnvelope>(original);
        var encoded = JsonDefaults.Serialize(envelope);
        await Assert.That(encoded.SequenceEqual(original)).IsTrue();
        await Assert.That(SHA256.HashData(encoded).SequenceEqual(SHA256.HashData(original))).IsTrue();
        await Assert.That(envelope.RequestId).IsEqualTo(Guid.Parse(RequestIdentifier));
        await Assert.That(envelope.Incarnation).IsEqualTo(Guid.Parse(IncarnationIdentifier));
        await Assert.That(envelope.ReadKind).IsEqualTo(GrainReadKind.Document);
        await Assert.That(envelope.CommandKind).IsNull();
        await Assert.That(envelope.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(envelope.EncodedPayload).IsEqualTo(EncodedJson);
        await AssertWirePayloadAsync(encoded, EncodedJson);
    }

    /// <summary>Current requests retain the old wire key while real database signatures and actor-key verification preserve exact payload bytes.</summary>
    [Test]
    public async Task ActualDatabaseSignedReadPreservesWireKeyAndExactPayload()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var requestId = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes(PayloadJson);
        var token = codec.CreateRead(requestId, Principal, GrainReadKind.Document, payload);
        var envelope = fixture.Database.Verify<GrainRequestEnvelope>(token);
        var request = codec.VerifyRead(token, requestId);
        await Assert.That(request.Payload.SequenceEqual(payload)).IsTrue();
        await Assert.That(request.Envelope.RequestId).IsEqualTo(requestId);
        await Assert.That(envelope.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(envelope.PrincipalId).IsEqualTo(Principal);
        var separator = token.IndexOf(SignatureSeparator, StringComparison.Ordinal);
        var originalSignedBytes = Base64Url.DecodeFromChars(token.AsSpan(0, separator));
        await AssertWirePayloadAsync(originalSignedBytes, Base64Url.EncodeToString(payload));
    }

    /// <summary>Null and missing required payload members fail strict JSON deserialization before scope or decoding can supply a default.</summary>
    /// <param name="missing">Whether to omit the original wire member instead of assigning JSON null.</param>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task NullOrMissingRequiredPayloadIsRejectedByCanonicalJson(bool missing)
    {
        var candidate = OriginalJson.Replace(WireMember, missing ? string.Empty : NullWireMember, StringComparison.Ordinal);
        var error = Assert.ThrowsExactly<JsonException>(() =>
            JsonDefaults.Deserialize<GrainRequestEnvelope>(Encoding.UTF8.GetBytes(candidate)));
        await Assert.That(error).IsNotNull();
    }

    /// <summary>Genuine database signatures cannot authorize a current request whose required original payload field is null or absent.</summary>
    /// <param name="missing">Whether to omit the original wire member instead of assigning JSON null.</param>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task SignedNullOrMissingPayloadIsRejectedThroughDatabaseAndCodec(bool missing)
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var requestId = Guid.NewGuid();
        var token = codec.CreateRead(requestId, Principal, GrainReadKind.Document, Encoding.UTF8.GetBytes(PayloadJson));
        var envelope = fixture.Database.Verify<GrainRequestEnvelope>(token);
        var candidate = JsonNode.Parse(JsonDefaults.Serialize(envelope))!.AsObject();
        if (missing)
        {
            candidate.Remove(WireProperty);
        }
        else
        {
            candidate[WireProperty] = null;
        }
        using var document = JsonDocument.Parse(candidate.ToJsonString(JsonDefaults.Options));
        var invalid = fixture.Database.Sign(document.RootElement);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Verify<GrainRequestEnvelope>(invalid)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(invalid, requestId)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }

    /// <summary>A present empty string satisfies required JSON shape but remains invalid at the real request scope boundary.</summary>
    [Test]
    public async Task PresentEmptyPayloadIsRejectedByCodecScopeWithoutRelaxingRequiredJson()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var requestId = Guid.NewGuid();
        var token = codec.CreateRead(requestId, Principal, GrainReadKind.Document, Encoding.UTF8.GetBytes(PayloadJson));
        var envelope = fixture.Database.Verify<GrainRequestEnvelope>(token) with { EncodedPayload = string.Empty };
        var invalid = fixture.Database.Sign(envelope);
        await Assert.That(fixture.Database.Verify<GrainRequestEnvelope>(invalid).EncodedPayload).IsEqualTo(string.Empty);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(invalid, requestId)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }

    private static async Task AssertWirePayloadAsync(byte[] bytes, string encodedPayload)
    {
        using var document = JsonDocument.Parse(bytes);
        await Assert.That(document.RootElement.GetProperty(WireProperty).GetString()).IsEqualTo(encodedPayload);
        await Assert.That(document.RootElement.TryGetProperty(RenamedProperty, out _)).IsFalse();
    }
}
