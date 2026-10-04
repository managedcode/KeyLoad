using System.Text;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeOperationMarkerTests
{
    [Test]
    public async Task PublicDecodeFailureIsSignedAndStillEvaluatedAtItsOriginalDomainCallSite()
    {
        using var database = new TestDatabase();
        var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.Batch, NativeAuthorityFixture.Root,
            database.Database.EvaluationClock.GetUtcNow(), NativeAuthorityFixture.InvalidJson);
        var normalized = database.Database.NormalizeOperation(operation);
        var payload = NativeAuthorityFixture.Read(normalized);
        await Assert.That(payload.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(payload.SafeDetail).IsEqualTo(NativeCommandContract.InvalidJson);
        await Assert.That(payload.Value.IsEmpty).IsTrue();
        var verified = database.Database.VerifyOperationAuthority(normalized);
        var result = database.Database.Apply(verified);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(result.SafeDetail).IsEqualTo(NativeCommandContract.InvalidJson);
        await Assert.That(database.Database.Outcome(operation.PrincipalId, operation.Id)!.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(database.Database.ResolveOutcome(verified).Error).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task SignedDecodeMarkerDoesNotPreemptTheCommittedBusinessClock()
    {
        using var database = new TestDatabase();
        var prior = database.Submit(OperationKind.SetDispatch, true);
        await Assert.That(prior.Error).IsNull();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.SetDispatch, NativeAuthorityFixture.Root,
            DateTimeOffset.UnixEpoch, NativeAuthorityFixture.InvalidJson));
        var result = database.Database.Apply(operation);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.ClockUncertain);
        await Assert.That(database.Database.Outcome(operation.PrincipalId, operation.Id)!.Error).IsEqualTo(ErrorCode.ClockUncertain);
    }

    [Test]
    public async Task SignedDecodeMarkerDoesNotPreemptCurrentPrincipalAuthentication()
    {
        using var database = new TestDatabase();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.Batch,
            NativeAuthorityFixture.OtherPrincipal, database.Database.EvaluationClock.GetUtcNow(), NativeAuthorityFixture.InvalidJson));
        var result = database.Database.Apply(operation);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(database.Database.Outcome(operation.PrincipalId, operation.Id)!.Error).IsEqualTo(ErrorCode.Unauthenticated);
    }

    [Test]
    public async Task NullPublicPayloadRetainsItsExistingCorruptionFailureMarker()
    {
        using var database = new TestDatabase();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.Batch, NativeAuthorityFixture.Root,
            database.Database.EvaluationClock.GetUtcNow(), NativeMarkerFixtures.NullJson));
        var verified = database.Database.VerifyOperationAuthority(operation);
        var payload = NativeAuthorityFixture.Read(verified);
        await Assert.That(payload.Error).IsEqualTo(ErrorCode.Corruption);
        var before = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.Apply(verified));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Database.ResolveOutcome(verified).Error).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
        await Assert.That(database.Database.Outcome(operation.PrincipalId, operation.Id)).IsNull();
    }

    [Test]
    public async Task UnknownPublicKindRetainsItsSignedUnsupportedCapabilityMarker()
    {
        using var database = new TestDatabase();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), (OperationKind)NativeMarkerFixtures.UnknownKind,
            NativeAuthorityFixture.Root, database.Database.EvaluationClock.GetUtcNow(), NativeAuthorityFixture.InvalidJson));
        var verified = database.Database.VerifyOperationAuthority(operation);
        var payload = NativeAuthorityFixture.Read(verified);
        await Assert.That(payload.Error).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(payload.SafeDetail).IsEqualTo(NativeCommandContract.Unsupported);
        await Assert.That(payload.Value.IsEmpty).IsTrue();
    }

    [Test]
    public async Task BorrowedProjectionAuthenticatesWithoutDecodingThePublicJsonPayload()
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var payload = NativeAuthorityFixture.Read(operation);
        Verify(database, operation, payload, Encoding.UTF8.GetBytes(operation.PayloadJson));
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => Verify(database, operation, payload,
            Encoding.UTF8.GetBytes(bool.FalseString)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Database.Outcome(operation.PrincipalId, operation.Id)).IsNull();
    }

    private static void Verify(TestDatabase database, ReplicatedOperation operation, NativeCommandPayload payload, byte[] json)
        => database.Database.VerifyNativeAuthority(operation.Id, operation.Kind, operation.PrincipalId, json,
            payload.Value, payload.Error, payload.SafeDetail, payload.Authority, payload.Signature);
}

internal static class NativeMarkerFixtures
{
    internal const string NullJson = "null";
    internal const int UnknownKind = 999;
}
