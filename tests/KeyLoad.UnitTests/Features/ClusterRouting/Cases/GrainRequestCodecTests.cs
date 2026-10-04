using System.Buffers.Text;
using System.Text;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests;

/// <summary>AC-ROUTE-001: real database signatures preserve exact payloads and bind independently keyed requests.</summary>
internal sealed class GrainRequestCodecTests
{
    private const string Principal = "root";
    private const string WrongPurpose = "another-purpose";
    private const string Payload = "{\"text\":\"Україна \\u2603 \\\"quoted\\\"\"}";
    private const string InvalidJson = "{\"unclosed\":";
    private const byte InvalidUtf8 = 0xff;
    private const char Padding = 'x';
    private const char SignatureSeparator = '.';
    private const int SignatureBytes = 32;
    private const int RepresentativeCharacters = 4_096;
    private const int NoDtoMarker = 0;
    private const int AdditionalByte = 1;

    /// <summary>AC-ROUTE-001: Unicode and quotes survive one signed request without recursive JSON escaping.</summary>
    [Test]
    public async Task ExactUnicodePayloadAndDistinctReadActorKeysArePreserved()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var bytes = NativeSerialization.Serialize(Payload);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = codec.VerifyRead(codec.CreateRead(firstId, Principal, GrainReadKind.Document, bytes), firstId);
        var second = codec.VerifyRead(codec.CreateRead(secondId, Principal, GrainReadKind.Document, bytes), secondId);
        await Assert.That(first.Payload.Span.SequenceEqual(bytes)).IsTrue();
        await Assert.That(GrainNativePayload.Read<string>(first.Payload)).IsEqualTo(Payload);
        await Assert.That(first.Envelope.RequestId).IsNotEqualTo(second.Envelope.RequestId);
        await Assert.That(first.Envelope.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
    }

    /// <summary>AC-ROUTE-001: exact native input bytes are accepted at the existing budget, with one extra byte rejected.</summary>
    [Test]
    public async Task SignedPayloadIsBoundedByActualNativeBytes()
    {
        var bytes = NativeSerialization.Serialize(new string(Padding, RepresentativeCharacters));
        using var fixture = new TestDatabase(new DatabaseLimits { MaxBatchBytes = bytes.Length });
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var id = Guid.NewGuid();
        var signed = codec.CreateRead(id, null, GrainReadKind.Authenticate, bytes);
        await Assert.That(codec.VerifyRead(signed, id).Payload.Span.SequenceEqual(bytes)).IsTrue();
        await Assert.That(NativeSerialization.Measure(new string(Padding, RepresentativeCharacters))).IsEqualTo((long)bytes.Length);
        var oversized = bytes.Append((byte)AdditionalByte).ToArray();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(Guid.NewGuid(), null,
            GrainReadKind.Authenticate, oversized)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>AC-ROUTE-001: issuance snapshots caller bytes before the signature is published.</summary>
    [Test]
    public async Task SignedPayloadNeverRetainsCallersMutableBuffer()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var bytes = NativeSerialization.Serialize(Payload);
        var expected = bytes.ToArray();
        var id = Guid.NewGuid();
        var signed = codec.CreateRead(id, null, GrainReadKind.Authenticate, bytes);
        bytes.AsSpan().Fill(InvalidUtf8);
        var decoded = codec.VerifyRead(signed, id);
        await Assert.That(decoded.Payload.Span.SequenceEqual(expected)).IsTrue();
        await Assert.That(GrainNativePayload.Read<string>(decoded.Payload)).IsEqualTo(Payload);
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
            NativeSerialization.Serialize(NoDtoMarker)), id).Envelope;
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
        var signed = codec.CreateRead(id, Principal, GrainReadKind.Document, NativeSerialization.Serialize(Payload));
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
            OperationKind.Membership, Guid.NewGuid(), NativeSerialization.Serialize(NoDtoMarker))).Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(Guid.NewGuid(), Principal,
            GrainReadKind.Authenticate, NativeSerialization.Serialize(new PrincipalRecord(Principal, Principal, [], []) { ClusterAdministrator = true }))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => GrainNativePayload.Read<string>(
            NativeSerialization.Serialize(new PrincipalRecord(Principal, Principal, [], []) { ClusterAdministrator = true }))).Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>AC-ROUTE-001: genuinely signed malformed or trailing native payload bytes are never accepted.</summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SignedMalformedOrTrailingPayloadIsDenied(bool trailing)
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var id = Guid.NewGuid();
        var marker = NativeSerialization.Serialize(NoDtoMarker);
        var request = codec.VerifyRead(codec.CreateRead(id, Principal, GrainReadKind.QueryCapabilities, marker), id).Envelope;
        var invalid = trailing ? marker.Append((byte)AdditionalByte).ToArray() : new byte[] { InvalidUtf8 };
        var signed = fixture.Database.Sign(request with { Payload = invalid });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRead(signed, id)).Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>AC-ROUTE-001: exact bytes must be complete native payloads before a signed request can be issued.</summary>
    [Test]
    public async Task LegacyJsonAndMalformedNativeAreDeniedBeforeIssuance()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database, TimeProvider.System);
        var invalid = new byte[][] { Encoding.UTF8.GetBytes(InvalidJson), [InvalidUtf8] };
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
