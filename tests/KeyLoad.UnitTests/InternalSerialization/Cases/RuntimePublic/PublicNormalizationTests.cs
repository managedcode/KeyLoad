namespace KeyLoad.UnitTests.InternalSerialization;

internal sealed class PublicNormalizationTests
{
    [Test]
    [Arguments(PublicShape.NullMutation)]
    [Arguments(PublicShape.NullPatch)]
    [Arguments(PublicShape.NullGrant)]
    public async Task PublicNullElementsRetainTypedBodiesExactIdentityAndDurableDomainFailures(PublicShape shape)
    {
        using var database = new TestDatabase();
        var operation = PublicNormalizationFixture.Create(database, shape);
        // Strict DTO persistence rejects null elements; the public input retains its owning domain validation.
        await Assert.That(PublicNormalizationFixture.EncodingFailure(operation).Code).IsEqualTo(ErrorCode.Corruption);
        var normalized = database.Database.OwnCommand(operation);
        await Assert.That(normalized.Value!.Input.Span.SequenceEqual(operation.Value!.Input.Span)).IsTrue();
        var verified = database.Database.OwnCommand(normalized);
        await Assert.That(verified.Value!.Input.IsEmpty).IsFalse();
        await Assert.That(PublicNormalizationChecks.HasNullElement(verified, shape)).IsTrue();
        await Assert.That(verified.Value.Fingerprint).IsEqualTo(PublicNormalizationFixture.Fingerprint(operation));

        var result = database.Database.Apply(verified);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.Problem?.Detail).IsEqualTo(PublicNormalizationFixture.Detail(shape));
        var stored = PublicNormalizationFixture.Stored(database, operation);
        var originalOutcome = PublicNormalizationFixture.StoredBytes(database, operation);
        await Assert.That(stored.Fingerprint).IsEqualTo(PublicNormalizationFixture.Fingerprint(operation));
        await Assert.That(stored.Result).IsEqualTo(result);
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(verified)).IsEqualTo(result);
        await Assert.That(PublicNormalizationFixture.StoredBytes(database, operation).SequenceEqual(originalOutcome)).IsTrue();
        await Assert.That(OutcomeStoreOracle.Read(database.Store, operation)).IsEqualTo(result);
    }

    [Test]
    [Arguments(PublicShape.NullMutation)]
    [Arguments(PublicShape.NullPatch)]
    [Arguments(PublicShape.NullGrant)]
    public async Task PublicNullElementsDoNotPreemptCurrentPrincipalAuthentication(PublicShape shape)
    {
        using var database = new TestDatabase();
        var operation = PublicNormalizationFixture.Create(database, shape, AuthorityFixture.OtherPrincipal);
        var normalized = database.Database.OwnCommand(operation);
        var before = database.ReadCanonicalCut();
        var result = database.Database.Apply(normalized);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(OutcomeStoreOracle.ReadStored(database.Store, operation)).IsNull();
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
        await Assert.That(database.ReadCanonicalCut()).IsEqualTo(before);
        await Assert.That(database.Submit(OperationKind.SetDispatch, true).Get<bool>()).IsTrue();
    }

    [Test]
    public async Task ConfiguredNullGrantDoesNotPreemptCurrentClusterAdministration()
    {
        using var database = new TestDatabase();
        var configured = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(PublicNormalizationFixture.OrdinaryPrincipal(database)));
        await Assert.That(Errors.Code(configured.Problem)).IsNull();
        var operation = PublicNormalizationFixture.Create(database, PublicShape.NullGrant,
            PublicNormalizationFixture.NonAdministrator);
        var normalized = database.Database.OwnCommand(operation);
        var result = database.Database.Apply(normalized);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(PublicNormalizationFixture.Stored(database, operation).Result).IsEqualTo(result);
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
    }

    [Test]
    public async Task ConfiguredNullGrantRetainsCommittedClockCachedOutcomeAndExactFingerprintConflict()
    {
        using var database = new TestDatabase();
        await Assert.That(Errors.Code(database.Submit(OperationKind.SetDispatch, true).Problem)).IsNull();
        var operation = PublicNormalizationFixture.Create(database, PublicShape.NullGrant,
            time: DateTimeOffset.UnixEpoch);
        var normalized = database.Database.OwnCommand(operation);
        var result = database.Database.Apply(normalized);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.ClockUncertain);
        var stored = PublicNormalizationFixture.Stored(database, operation);
        var originalOutcome = PublicNormalizationFixture.StoredBytes(database, operation);
        await Assert.That(stored.Result).IsEqualTo(result);
        var retry = database.Database.OwnCommand(operation);
        retry.Timestamp = database.Database.EvaluationClock.GetUtcNow().UtcDateTime;
        await Assert.That(database.Database.Apply(retry)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
        await Assert.That(PublicNormalizationFixture.StoredBytes(database, operation).SequenceEqual(originalOutcome)).IsTrue();
        var changed = database.Database.CreateCommand(retry.Value!.Kind, retry.CommandId, retry.UserId!,
            new DateTimeOffset(retry.Timestamp), PublicNormalizationFixture.PublicJson(retry) + "\n");
        await Assert.That(Errors.Code(database.Database.Apply(changed).Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(PublicNormalizationFixture.StoredBytes(database, operation).SequenceEqual(originalOutcome)).IsTrue();
    }

    [Test]
    public async Task NullGrantRetainsProtectedTargetBeforeGrantValidation()
    {
        using var database = new TestDatabase();
        var operation = PublicNormalizationFixture.Create(database, PublicShape.NullGrant,
            targetPrincipal: KeyLoad.Core.RuntimeJournaling.Identity.RuntimeJournalIdentity.ProtectedPrincipalId);
        await PublicNormalizationChecks.DurableFailureAsync(database, operation, ErrorCode.PermissionDenied,
            "The runtime journal request is invalid.");
    }

    [Test]
    [Arguments(PublicShape.NullMutation)]
    [Arguments(PublicShape.NullPatch)]
    [Arguments(PublicShape.NullGrant)]
    public async Task FactoryAndOwnedSnapshotsRetainPublicNullElements(PublicShape shape)
    {
        using var database = new TestDatabase();
        var operation = PublicNormalizationFixture.Create(database, shape);
        var normalized = database.Database.OwnCommand(operation);
        var created = database.Database.CreateCommand(operation.Value!.Kind, operation.CommandId, operation.UserId!,
            new DateTimeOffset(operation.Timestamp), normalized.Value!.Input);
        await Assert.That(PublicNormalizationChecks.HasNullElement(created, shape)).IsTrue();
        var copied = database.Database.OwnCommand(normalized);
        await Assert.That(OrleansSerialization.Serialize(copied).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(normalized))).IsTrue();
        var result = database.Database.Apply(created);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.Problem?.Detail).IsEqualTo(PublicNormalizationFixture.Detail(shape));
        await Assert.That(PublicNormalizationFixture.Stored(database, created).Fingerprint)
            .IsEqualTo(created.Value!.Fingerprint);
    }

    [Test]
    public async Task MalformedAlreadyOperationStillPropagatesCorruptionWithoutAnOutcome()
    {
        using var database = new TestDatabase();
        var operation = PublicNormalizationFixture.Create(database, PublicShape.NullGrant);
        operation.Value = operation.Value! with { Input = new byte[] { 0xff } };
        var before = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.OwnCommand(operation));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await Assert.That(OutcomeStoreOracle.Read(database.Store, operation)).IsNull();
    }
}
