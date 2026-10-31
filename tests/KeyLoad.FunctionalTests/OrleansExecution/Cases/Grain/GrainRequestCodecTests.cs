using KeyLoad.Clustering;

namespace KeyLoad.FunctionalTests;

/// <summary>AC-ROUTE-001: typed envelopes preserve exact payloads and bind independently keyed requests.</summary>
internal sealed class GrainRequestCodecTests
{
    private const string Principal = "root";
    private const string WrongPurpose = "another-purpose";
    private const string Payload = "{\"text\":\"Україна \\u2603 \\\"quoted\\\"\"}";
    private const int NoDtoMarker = 0;

    /// <summary>AC-ROUTE-001: Unicode and quotes survive one envelope without recursive JSON escaping.</summary>
    [Test]
    public async Task ExactUnicodePayloadAndDistinctReadActorKeysArePreserved()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System, UnitRoutingOptions.Routing());
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = codec.DecodeRead(codec.CreateRead(firstId, Principal, GrainReadKind.Document, Payload), firstId);
        var second = codec.DecodeRead(codec.CreateRead(secondId, Principal, GrainReadKind.Document, Payload), secondId);
        await Assert.That(GrainPayload.Read<string>(first.Payload)).IsEqualTo(Payload);
        await Assert.That(first.Envelope.RequestId).IsNotEqualTo(second.Envelope.RequestId);
        await Assert.That(first.Envelope.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
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
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System, UnitRoutingOptions.Routing());
        var id = Guid.NewGuid();
        var envelope = codec.DecodeRead(codec.CreateRead(id, Principal, GrainReadKind.QueryCapabilities,
            NoDtoMarker), id).Envelope;
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
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System, UnitRoutingOptions.Routing());
        var id = Guid.NewGuid();
        var envelope = codec.CreateRead(id, Principal, GrainReadKind.Document, Payload);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.DecodeRead(envelope, Guid.NewGuid())).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        var foreign = envelope with { Incarnation = Guid.NewGuid() };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.DecodeRead(foreign, id)).Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    /// <summary>AC-ROUTE-001: undefined operation discriminator cannot be issued through public write actors.</summary>
    [Test]
    public async Task ReservedDiscriminatorAndForgedAuthenticationShapeAreDenied()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System, UnitRoutingOptions.Routing());
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateCommand(Guid.NewGuid(), Principal,
            (OperationKind)8, Guid.NewGuid(), NoDtoMarker)).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(Guid.NewGuid(), Principal,
            GrainReadKind.Authenticate, new PrincipalRecord(Principal, Principal, [], []) { ClusterAdministrator = true })).Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => GrainPayload.Read<string>(
            new PrincipalRecord(Principal, Principal, [], []) { ClusterAdministrator = true })).Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>AC-ROUTE-001: a payload that violates its contract is denied at issuance and at every receiving hop.</summary>
    [Test]
    public async Task ContractInvalidPayloadIsDeniedAtIssuanceAndDecode()
    {
        using var fixture = new TestDatabase();
        var codec = new GrainRequestCodec(fixture.Database.Store.Identity.Incarnation, TimeProvider.System, UnitRoutingOptions.Routing());
        var id = Guid.NewGuid();
        var invalid = new GetDocumentRequest(null!);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.CreateRead(id, Principal,
            GrainReadKind.Document, invalid)).Code).IsEqualTo(ErrorCode.Validation);
        var request = codec.DecodeRead(codec.CreateRead(id, Principal, GrainReadKind.QueryCapabilities, NoDtoMarker), id).Envelope;
        var changed = request with
        {
            Payload = invalid
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => codec.DecodeRead(changed, id)).Code).IsEqualTo(ErrorCode.Validation);
    }
}
