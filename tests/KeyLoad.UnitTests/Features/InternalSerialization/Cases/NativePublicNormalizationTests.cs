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
        // Ordinary internal persistence remains strict, while the signed command retains its public elements.
        await Assert.That(NativePublicNormalizationFixture.EncodingFailure(operation).Code).IsEqualTo(ErrorCode.Corruption);
        var normalized = database.Database.NormalizeOperation(operation);
        await Assert.That(normalized.PayloadJson).IsEqualTo(operation.PayloadJson);
        var verified = database.Database.VerifyOperationAuthority(normalized);
        var payload = NativeAuthorityFixture.Read(verified);
        await Assert.That(payload.Value.IsEmpty).IsFalse();
        await Assert.That(payload.Error).IsNull();
        await Assert.That(payload.SafeDetail).IsNull();
        await Assert.That(NativePublicNormalizationChecks.HasNullElement(verified, shape)).IsTrue();
        var authority = NativeSerialization.Deserialize<NativeCommandAuthority>(payload.Authority.Span);
        await Assert.That(authority.Fingerprint).IsEqualTo(NativePublicNormalizationFixture.Fingerprint(operation));

        var result = database.Database.Apply(verified);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.SafeDetail).IsEqualTo(NativePublicNormalizationFixture.Detail(shape));
        var stored = NativePublicNormalizationFixture.Stored(database, operation);
        await Assert.That(stored.Fingerprint).IsEqualTo(NativePublicNormalizationFixture.Fingerprint(operation));
        await Assert.That(stored.Result).IsEqualTo(result);
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(verified)).IsEqualTo(result);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation)).IsEqualTo(stored);
        await Assert.That(database.Database.Outcome(operation.PrincipalId, operation.Id)).IsEqualTo(result);
    }

    [Test]
    [Arguments(NativePublicShape.NullMutation)]
    [Arguments(NativePublicShape.NullPatch)]
    [Arguments(NativePublicShape.NullGrant)]
    public async Task PublicNullElementsDoNotPreemptCurrentPrincipalAuthentication(NativePublicShape shape)
    {
        using var database = new TestDatabase();
        var operation = NativePublicNormalizationFixture.Create(database, shape, NativeAuthorityFixture.OtherPrincipal);
        var normalized = database.Database.NormalizeOperation(operation);
        var result = database.Database.Apply(normalized);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation).Result).IsEqualTo(result);
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
    }

    [Test]
    public async Task ConfiguredNullGrantDoesNotPreemptCurrentClusterAdministration()
    {
        using var database = new TestDatabase();
        var configured = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(NativePublicNormalizationFixture.OrdinaryPrincipal(database)));
        await Assert.That(configured.Error).IsNull();
        var operation = NativePublicNormalizationFixture.Create(database, NativePublicShape.NullGrant,
            NativePublicNormalizationFixture.NonAdministrator);
        var normalized = database.Database.NormalizeOperation(operation);
        var result = database.Database.Apply(normalized);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation).Result).IsEqualTo(result);
        await Assert.That(database.Database.Apply(operation)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
    }

    [Test]
    public async Task ConfiguredNullGrantRetainsCommittedClockCachedOutcomeAndExactFingerprintConflict()
    {
        using var database = new TestDatabase();
        await Assert.That(database.Submit(OperationKind.SetDispatch, true).Error).IsNull();
        var operation = NativePublicNormalizationFixture.Create(database, NativePublicShape.NullGrant,
            time: DateTimeOffset.UnixEpoch);
        var normalized = database.Database.NormalizeOperation(operation);
        var result = database.Database.Apply(normalized);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.ClockUncertain);
        var stored = NativePublicNormalizationFixture.Stored(database, operation);
        await Assert.That(stored.Result).IsEqualTo(result);
        var retry = operation with { EvaluatedAt = database.Database.EvaluationClock.GetUtcNow() };
        await Assert.That(database.Database.Apply(retry)).IsEqualTo(result);
        await Assert.That(database.Database.ResolveOutcome(normalized)).IsEqualTo(result);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation)).IsEqualTo(stored);
        var changed = retry with { PayloadJson = retry.PayloadJson + "\n" };
        await Assert.That(database.Database.Apply(changed).Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(NativePublicNormalizationFixture.Stored(database, operation)).IsEqualTo(stored);
    }

    [Test]
    public async Task NullMutationRetainsStaleOwnershipBeforeElementValidation()
    {
        using var database = new TestDatabase();
        var operation = NativePublicNormalizationFixture.Create(database, NativePublicShape.NullMutation, ownershipEpoch: 0);
        await NativePublicNormalizationChecks.DurableFailureAsync(database, operation, ErrorCode.OwnershipLost,
            "The partition ownership epoch is stale.");
    }

    [Test]
    public async Task NullGrantRetainsProtectedTargetBeforeGrantValidation()
    {
        using var database = new TestDatabase();
        var operation = NativePublicNormalizationFixture.Create(database, NativePublicShape.NullGrant,
            targetPrincipal: KeyLoad.Core.ClusterPrincipalPolicy.InternalPrincipalId);
        await NativePublicNormalizationChecks.DurableFailureAsync(database, operation, ErrorCode.PermissionDenied,
            "The internal cluster principal is protected from public configuration.");
    }

    [Test]
    [Arguments(NativePublicShape.NullMutation)]
    [Arguments(NativePublicShape.NullPatch)]
    [Arguments(NativePublicShape.NullGrant)]
    public async Task NativeFactoryAndSemanticComparerRetainPublicNullElements(NativePublicShape shape)
    {
        using var database = new TestDatabase();
        var operation = NativePublicNormalizationFixture.Create(database, shape);
        var normalized = database.Database.NormalizeOperation(operation);
        var value = NativeAuthorityFixture.Read(normalized).Value;
        var created = database.Database.CreateNativeOperation(operation.Kind, operation.Id, operation.PrincipalId,
            operation.EvaluatedAt, value);
        await Assert.That(NativePublicNormalizationChecks.HasNullElement(created, shape)).IsTrue();
        await Assert.That(database.Database.NativeOperationsEqual(normalized,
            normalized with { NativePayload = normalized.NativePayload.ToArray() })).IsTrue();
        var result = database.Database.Apply(created);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.SafeDetail).IsEqualTo(NativePublicNormalizationFixture.Detail(shape));
        await Assert.That(NativePublicNormalizationFixture.Stored(database, created).Fingerprint)
            .IsEqualTo(NativePublicNormalizationFixture.Fingerprint(created));
    }

    [Test]
    public async Task MalformedAlreadyNativeOperationStillPropagatesCorruptionWithoutAnOutcome()
    {
        using var database = new TestDatabase();
        var operation = NativePublicNormalizationFixture.Create(database, NativePublicShape.NullGrant)
            with
        { NativePayload = new byte[] { 0xff } };
        var before = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.NormalizeOperation(operation));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await Assert.That(database.Database.Outcome(operation.PrincipalId, operation.Id)).IsNull();
    }
}
