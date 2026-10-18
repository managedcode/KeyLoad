using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativePublicNormalizationTests
{
    [Test]
    [Arguments(NativePublicShape.NullMutation)]
    [Arguments(NativePublicShape.NullPatch)]
    [Arguments(NativePublicShape.NullGrant)]
    public async Task PublicNullElementsRetainTypedBodiesExactIdentityAndDurableDomainFailures(NativePublicShape shape)
    {
        using var database = new TestDatabase();
        var operation = NativePublicNormalizationFixture.Create(database, shape);
        // Strict DTO persistence rejects null elements; the public input retains its owning domain validation.
        await Assert.That(NativePublicNormalizationFixture.EncodingFailure(operation).Code).IsEqualTo(ErrorCode.Corruption);
        var normalized = database.Database.OwnCommand(operation);
        await Assert.That(normalized.Value!.Input.Span.SequenceEqual(operation.Value!.Input.Span)).IsTrue();
        var verified = database.Database.OwnCommand(normalized);
        await Assert.That(verified.Value!.Input.IsEmpty).IsFalse();
        await Assert.That(NativePublicNormalizationChecks.HasNullElement(verified, shape)).IsTrue();
        await Assert.That(verified.Value.Fingerprint).IsEqualTo(NativePublicNormalizationFixture.Fingerprint(operation));

        var result = database.Database.Apply(verified);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.Problem?.Detail).IsEqualTo(NativePublicNormalizationFixture.Detail(shape));
        var stored = NativePublicNormalizationFixture.Stored(database, operation);
        await Assert.That(stored.Fingerprint).IsEqualTo(NativePublicNormalizationFixture.Fingerprint(operation));
        await Assert.That(stored.Result).IsEqualTo(result);
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(verified)).IsEqualTo(result);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation)).IsEqualTo(stored);
        await Assert.That(OutcomeStoreOracle.Read(database.Store, operation)).IsEqualTo(result);
    }

    [Test]
    [Arguments(NativePublicShape.NullMutation)]
    [Arguments(NativePublicShape.NullPatch)]
    [Arguments(NativePublicShape.NullGrant)]
    public async Task PublicNullElementsDoNotPreemptCurrentPrincipalAuthentication(NativePublicShape shape)
    {
        using var database = new TestDatabase();
        var operation = NativePublicNormalizationFixture.Create(database, shape, NativeAuthorityFixture.OtherPrincipal);
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
            new ConfigurePrincipalRequest(NativePublicNormalizationFixture.OrdinaryPrincipal(database)));
        await Assert.That(Errors.Code(configured.Problem)).IsNull();
        var operation = NativePublicNormalizationFixture.Create(database, NativePublicShape.NullGrant,
            NativePublicNormalizationFixture.NonAdministrator);
        var normalized = database.Database.OwnCommand(operation);
        var result = database.Database.Apply(normalized);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation).Result).IsEqualTo(result);
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
    }

    [Test]
    public async Task ConfiguredNullGrantRetainsCommittedClockCachedOutcomeAndExactFingerprintConflict()
    {
        using var database = new TestDatabase();
        await Assert.That(Errors.Code(database.Submit(OperationKind.SetDispatch, true).Problem)).IsNull();
        var operation = NativePublicNormalizationFixture.Create(database, NativePublicShape.NullGrant,
            time: DateTimeOffset.UnixEpoch);
        var normalized = database.Database.OwnCommand(operation);
        var result = database.Database.Apply(normalized);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.ClockUncertain);
        var stored = NativePublicNormalizationFixture.Stored(database, operation);
        await Assert.That(stored.Result).IsEqualTo(result);
        var retry = database.Database.OwnCommand(operation);
        retry.Timestamp = database.Database.EvaluationClock.GetUtcNow().UtcDateTime;
        await Assert.That(database.Database.Apply(retry)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation)).IsEqualTo(stored);
        var changed = database.Database.CreateCommand(retry.Value!.Kind, retry.CommandId, retry.UserId!,
            new DateTimeOffset(retry.Timestamp), NativePublicNormalizationFixture.PublicJson(retry) + "\n");
        await Assert.That(Errors.Code(database.Database.Apply(changed).Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation)).IsEqualTo(stored);
    }

    [Test]
    public async Task NullGrantRetainsProtectedTargetBeforeGrantValidation()
    {
        using var database = new TestDatabase();
        var operation = NativePublicNormalizationFixture.Create(database, NativePublicShape.NullGrant,
            targetPrincipal: KeyLoad.Core.Features.ClusterRouting.Identity.RuntimeJournalIdentity.ProtectedPrincipalId);
        await NativePublicNormalizationChecks.DurableFailureAsync(database, operation, ErrorCode.PermissionDenied,
            "The runtime journal request is invalid.");
    }

    [Test]
    [Arguments(NativePublicShape.NullMutation)]
    [Arguments(NativePublicShape.NullPatch)]
    [Arguments(NativePublicShape.NullGrant)]
    public async Task NativeFactoryAndOwnedSnapshotsRetainPublicNullElements(NativePublicShape shape)
    {
        using var database = new TestDatabase();
        var operation = NativePublicNormalizationFixture.Create(database, shape);
        var normalized = database.Database.OwnCommand(operation);
        var created = database.Database.CreateCommand(operation.Value!.Kind, operation.CommandId, operation.UserId!,
            new DateTimeOffset(operation.Timestamp), normalized.Value!.Input);
        await Assert.That(NativePublicNormalizationChecks.HasNullElement(created, shape)).IsTrue();
        var copied = database.Database.OwnCommand(normalized);
        await Assert.That(NativeSerialization.Serialize(copied).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(normalized))).IsTrue();
        var result = database.Database.Apply(created);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.Problem?.Detail).IsEqualTo(NativePublicNormalizationFixture.Detail(shape));
        await Assert.That(NativePublicNormalizationFixture.Stored(database, created).Fingerprint)
            .IsEqualTo(created.Value!.Fingerprint);
    }

    [Test]
    public async Task MalformedAlreadyNativeOperationStillPropagatesCorruptionWithoutAnOutcome()
    {
        using var database = new TestDatabase();
        var operation = NativePublicNormalizationFixture.Create(database, NativePublicShape.NullGrant);
        operation.Value = operation.Value! with { Input = new byte[] { 0xff } };
        var before = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.OwnCommand(operation));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await Assert.That(OutcomeStoreOracle.Read(database.Store, operation)).IsNull();
    }
}
