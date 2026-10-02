using System.Buffers.Text;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests;

/// <summary>AC-ROUTE-001: real database signatures preserve exact payloads and bind independently keyed requests.</summary>
internal sealed class GrainRequestCodecTests
{
    private const string Principal = "root";
    private const string WrongPurpose = "another-purpose";
    private const string Payload = "{\"text\":\"Україна \\u2603 \\\"quoted\\\"\"}";
    private const string NullPayload = "null";
    private const string RolePayload = "{\"clusterAdministrator\":true}";
    private const string PaddedNull = "bnVsbA==";
    private const string WhitespaceBase64 = " bnVsbA";
    private const string InvalidJson = "{\"unclosed\":";
    private const byte InvalidUtf8 = 0xff;
    private const char Padding = 'x';
    private const char SignatureSeparator = '.';
    private const int SignatureBytes = 32;
    private const int JsonStringQuoteBytes = 2;
    private const int AdditionalByte = 1;

    /// <summary>AC-ROUTE-001: Unicode and quotes survive one signed request without recursive JSON escaping.</summary>
    [Test]
    public async Task ExactUnicodePayloadAndDistinctReadActorKeysArePreserved()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var bytes = GrainPayloadJson.Utf8.GetBytes(Payload);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = codec.VerifyRead(codec.CreateRead(firstId, Principal, GrainReadKind.Document, bytes), firstId);
        var second = codec.VerifyRead(codec.CreateRead(secondId, Principal, GrainReadKind.Document, bytes), secondId);
        await Assert.That(first.Payload).IsEqualTo(bytes);
        await Assert.That(first.Envelope.RequestId).IsNotEqualTo(second.Envelope.RequestId);
        await Assert.That(first.Envelope.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
    }

    /// <summary>AC-ROUTE-001: maximum exact input is accepted, while one extra UTF8 byte is rejected.</summary>
    [Test]
    public async Task LargeSignedPayloadIsBoundedByActualUtf8Bytes()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var bytes = JsonDefaults.Serialize(new string(Padding, fixture.Database.Limits.MaxBatchBytes - JsonStringQuoteBytes));
        var id = Guid.NewGuid();
        var signed = codec.CreateRead(id, null, GrainReadKind.Authenticate, bytes);
        await Assert.That(codec.VerifyRead(signed, id).Payload).IsEqualTo(bytes);
        var oversized = JsonDefaults.Serialize(new string(Padding, fixture.Database.Limits.MaxBatchBytes - JsonStringQuoteBytes + AdditionalByte));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(Guid.NewGuid(), null,
            GrainReadKind.Authenticate, oversized)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>AC-ROUTE-001: properly signed but invalid metadata cannot cross the request boundary.</summary>
    [Test]
    [Arguments(GrainRequestMutation.Purpose)]
    [Arguments(GrainRequestMutation.Incarnation)]
    [Arguments(GrainRequestMutation.Expired)]
    [Arguments(GrainRequestMutation.Future)]
    [Arguments(GrainRequestMutation.BothKinds)]
    [Arguments(GrainRequestMutation.NoKind)]
    [Arguments(GrainRequestMutation.CommandId)]
    [Arguments(GrainRequestMutation.UnknownKind)]
    public async Task InvalidSignedScopeIsDenied(GrainRequestMutation mutation)
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var id = Guid.NewGuid();
        var envelope = codec.VerifyRead(codec.CreateRead(id, Principal, GrainReadKind.QueryCapabilities,
            GrainPayloadJson.Utf8.GetBytes(NullPayload)), id).Envelope;
        var changed = mutation switch
        {
            GrainRequestMutation.Purpose => envelope with { Purpose = WrongPurpose },
            GrainRequestMutation.Incarnation => envelope with { Incarnation = Guid.NewGuid() },
            GrainRequestMutation.Expired => envelope with { ExpiresAt = TimeProvider.System.GetUtcNow() - GrainRoutingProtocol.RequestLifetime },
            GrainRequestMutation.Future => envelope with
            {
                ExpiresAt = TimeProvider.System.GetUtcNow()
                + GrainRoutingProtocol.MaximumFuture + GrainRoutingProtocol.RequestLifetime
            },
            GrainRequestMutation.BothKinds => envelope with { CommandKind = OperationKind.Batch, CommandId = Guid.NewGuid() },
            GrainRequestMutation.NoKind => envelope with { ReadKind = null },
            GrainRequestMutation.CommandId => envelope with { CommandId = Guid.NewGuid() },
            _ => envelope with { ReadKind = (GrainReadKind)int.MaxValue }
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(fixture.Database.Sign(changed), id)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }

    /// <summary>AC-ROUTE-001: native signatures cannot authorize a different actor key or substituted payload.</summary>
    [Test]
    public async Task WrongActorKeyAndTamperedSignatureAreDenied()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var id = Guid.NewGuid();
        var signed = codec.CreateRead(id, Principal, GrainReadKind.Document, GrainPayloadJson.Utf8.GetBytes(Payload));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(signed, Guid.NewGuid())).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        var separator = signed.LastIndexOf(SignatureSeparator);
        var tampered = signed[..(separator + AdditionalByte)] + Base64Url.EncodeToString(new byte[SignatureBytes]);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(tampered, id)).Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    /// <summary>AC-ROUTE-001: protected Membership cannot be issued through public write actors.</summary>
    [Test]
    public async Task MembershipAndForgedAuthenticationShapeAreDenied()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateCommand(Guid.NewGuid(), Principal,
            OperationKind.Membership, Guid.NewGuid(), GrainPayloadJson.Utf8.GetBytes(NullPayload))).Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(Guid.NewGuid(), Principal,
            GrainReadKind.Authenticate, GrainPayloadJson.Utf8.GetBytes(RolePayload))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => GrainPayloadJson.Read<string>(
            GrainPayloadJson.Utf8.GetBytes(RolePayload))).Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>AC-ROUTE-001: signed payload encodings use the exact unpadded base64url alphabet.</summary>
    [Test]
    [Arguments(PaddedNull)]
    [Arguments(WhitespaceBase64)]
    public async Task NoncanonicalPayloadEncodingIsDenied(string encoded)
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var id = Guid.NewGuid();
        var request = codec.VerifyRead(codec.CreateRead(id, Principal, GrainReadKind.QueryCapabilities,
            GrainPayloadJson.Utf8.GetBytes(NullPayload)), id).Envelope;
        var signed = fixture.Database.Sign(request with { EncodedPayload = encoded });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(signed, id)).Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    /// <summary>AC-ROUTE-001: exact bytes must be valid UTF8 JSON before a signed request can be issued.</summary>
    [Test]
    public async Task MalformedJsonAndInvalidUtf8AreDeniedBeforeIssuance()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var invalid = new byte[][] { GrainPayloadJson.Utf8.GetBytes(InvalidJson), [InvalidUtf8] };
        foreach (var bytes in invalid)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(Guid.NewGuid(), null,
                GrainReadKind.Authenticate, bytes)).Code).IsEqualTo(ErrorCode.Validation);
        }
    }
}

/// <summary>Invalid signed request scope cases.</summary>
internal enum GrainRequestMutation
{
    /// <summary>A different protocol purpose.</summary>
    Purpose,
    /// <summary>A different installation.</summary>
    Incarnation,
    /// <summary>An expired request.</summary>
    Expired,
    /// <summary>An excessive future expiry.</summary>
    Future,
    /// <summary>Both read and command kinds.</summary>
    BothKinds,
    /// <summary>No operation kind.</summary>
    NoKind,
    /// <summary>A command ID on a read.</summary>
    CommandId,
    /// <summary>An undefined operation kind.</summary>
    UnknownKind
}
