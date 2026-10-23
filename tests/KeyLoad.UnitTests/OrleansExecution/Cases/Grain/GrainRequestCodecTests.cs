using System.Text;
using KeyLoad.Clustering;

namespace KeyLoad.UnitTests;

/// <summary>AC-ROUTE-001: typed envelopes preserve exact payloads and bind independently keyed requests.</summary>
internal sealed class GrainRequestCodecTests
{
    private const string Principal = "root";
    private const string WrongPurpose = "another-purpose";
    private const string Payload = "{\"text\":\"Україна \\u2603 \\\"quoted\\\"\"}";
    private const string InvalidJson = "{\"unclosed\":";
    private const byte InvalidUtf8 = 0xff;
    private const char Padding = 'x';
    private const int RepresentativeCharacters = 4_096;
    private const int NoDtoMarker = 0;
    private const int AdditionalByte = 1;

    /// <summary>AC-ROUTE-001: Unicode and quotes survive one envelope without recursive JSON escaping.</summary>
    [Test]
    public async Task ExactUnicodePayloadAndDistinctReadActorKeysArePreserved()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var bytes = OrleansSerialization.Serialize(Payload);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = codec.DecodeRead(codec.CreateRead(firstId, Principal, GrainReadKind.Document, bytes), firstId);
        var second = codec.DecodeRead(codec.CreateRead(secondId, Principal, GrainReadKind.Document, bytes), secondId);
        await Assert.That(first.Payload.Span.SequenceEqual(bytes)).IsTrue();
        await Assert.That(GrainPayload.Read<string>(first.Payload)).IsEqualTo(Payload);
        await Assert.That(first.Envelope.RequestId).IsNotEqualTo(second.Envelope.RequestId);
        await Assert.That(first.Envelope.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
    }

    /// <summary>AC-ROUTE-001: exact execution input bytes are accepted at the existing budget, with one extra byte rejected.</summary>
    [Test]
    public async Task IssuedPayloadIsBoundedByActualBytes()
    {
        var bytes = OrleansSerialization.Serialize(new string(Padding, RepresentativeCharacters));
        using var fixture = new TestDatabase(new DatabaseLimits { MaxBatchBytes = bytes.Length });
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var id = Guid.NewGuid();
        var issued = codec.CreateRead(id, null, GrainReadKind.Authenticate, bytes);
        await Assert.That(codec.DecodeRead(issued, id).Payload.Span.SequenceEqual(bytes)).IsTrue();
        await Assert.That(OrleansSerialization.Measure(new string(Padding, RepresentativeCharacters))).IsEqualTo((long)bytes.Length);
        var oversized = bytes.Append((byte)AdditionalByte).ToArray();
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(Guid.NewGuid(), null,
            GrainReadKind.Authenticate, oversized)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>AC-ROUTE-001: issuance snapshots caller bytes before the envelope is handed to Orleans.</summary>
    [Test]
    public async Task IssuedPayloadNeverRetainsCallersMutableBuffer()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var bytes = OrleansSerialization.Serialize(Payload);
        var expected = bytes.ToArray();
        var id = Guid.NewGuid();
        var issued = codec.CreateRead(id, null, GrainReadKind.Authenticate, bytes);
        bytes.AsSpan().Fill(InvalidUtf8);
        var decoded = codec.DecodeRead(issued, id);
        await Assert.That(decoded.Payload.Span.SequenceEqual(expected)).IsTrue();
        await Assert.That(GrainPayload.Read<string>(decoded.Payload)).IsEqualTo(Payload);
    }

    /// <summary>AC-ROUTE-001: well-formed envelopes with invalid scope metadata cannot cross the request boundary.</summary>
    [Test]
    [Arguments(GrainRequestMutation.Purpose)]
    [Arguments(GrainRequestMutation.Incarnation)]
    [Arguments(GrainRequestMutation.Expired)]
    [Arguments(GrainRequestMutation.Future)]
    [Arguments(GrainRequestMutation.BothKinds)]
    [Arguments(GrainRequestMutation.NoKind)]
    [Arguments(GrainRequestMutation.CommandId)]
    [Arguments(GrainRequestMutation.UnknownKind)]
    public async Task InvalidScopeIsDenied(GrainRequestMutation mutation)
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var id = Guid.NewGuid();
        var envelope = codec.DecodeRead(codec.CreateRead(id, Principal, GrainReadKind.QueryCapabilities,
            OrleansSerialization.Serialize(NoDtoMarker)), id).Envelope;
        var changed = mutation switch
        {
            GrainRequestMutation.Purpose => envelope with { Purpose = WrongPurpose },
            GrainRequestMutation.Incarnation => envelope with { Incarnation = Guid.NewGuid() },
            GrainRequestMutation.Expired => envelope with { ExpiresAt = TimeProvider.System.GetUtcNow() - UnitRoutingOptions.Routing().Value.RequestLifetime },
            GrainRequestMutation.Future => envelope with
            {
                ExpiresAt = TimeProvider.System.GetUtcNow()
                + UnitRoutingOptions.Routing().Value.MaximumFuture + UnitRoutingOptions.Routing().Value.RequestLifetime
            },
            GrainRequestMutation.BothKinds => envelope with { CommandKind = OperationKind.Batch, CommandId = Guid.NewGuid() },
            GrainRequestMutation.NoKind => envelope with { ReadKind = null },
            GrainRequestMutation.CommandId => envelope with { CommandId = Guid.NewGuid() },
            _ => envelope with { ReadKind = (GrainReadKind)int.MaxValue }
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.DecodeRead(changed, id)).Code)
            .IsEqualTo(ErrorCode.TokenInvalidated);
    }

    /// <summary>AC-ROUTE-001: an envelope cannot authorize a different actor key or a foreign incarnation.</summary>
    [Test]
    public async Task WrongActorKeyAndForeignIncarnationAreDenied()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var id = Guid.NewGuid();
        var envelope = codec.CreateRead(id, Principal, GrainReadKind.Document, OrleansSerialization.Serialize(Payload));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.DecodeRead(envelope, Guid.NewGuid())).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        var foreign = envelope with { Incarnation = Guid.NewGuid() };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.DecodeRead(foreign, id)).Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    /// <summary>AC-ROUTE-001: undefined operation discriminator cannot be issued through public write actors.</summary>
    [Test]
    public async Task ReservedDiscriminatorAndForgedAuthenticationShapeAreDenied()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateCommand(Guid.NewGuid(), Principal,
            (OperationKind)8, Guid.NewGuid(), OrleansSerialization.Serialize(NoDtoMarker))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(Guid.NewGuid(), Principal,
            GrainReadKind.Authenticate, OrleansSerialization.Serialize(new PrincipalRecord(Principal, Principal, [], []) { ClusterAdministrator = true }))).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => GrainPayload.Read<string>(
            OrleansSerialization.Serialize(new PrincipalRecord(Principal, Principal, [], []) { ClusterAdministrator = true }))).Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>AC-ROUTE-001: malformed or trailing execution payload bytes are never accepted.</summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MalformedOrTrailingPayloadIsDenied(bool trailing)
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var id = Guid.NewGuid();
        var marker = OrleansSerialization.Serialize(NoDtoMarker);
        var request = codec.DecodeRead(codec.CreateRead(id, Principal, GrainReadKind.QueryCapabilities, marker), id).Envelope;
        var invalid = trailing ? marker.Append((byte)AdditionalByte).ToArray() : new byte[] { InvalidUtf8 };
        var changed = request with
        {
            Payload = invalid
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.DecodeRead(changed, id)).Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>AC-ROUTE-001: exact bytes must be complete execution payloads before an envelope can be issued.</summary>
    [Test]
    public async Task LegacyJsonAndMalformedAreDeniedBeforeIssuance()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation,
            fixture.Database.OperationLimitsOptions, TimeProvider.System, UnitRoutingOptions.Routing());
        var invalid = new byte[][] { Encoding.UTF8.GetBytes(InvalidJson), [InvalidUtf8] };
        foreach (var bytes in invalid)
        {
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(Guid.NewGuid(), null,
                GrainReadKind.Authenticate, bytes)).Code).IsEqualTo(ErrorCode.Validation);
        }
    }
}
