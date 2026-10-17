using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Replication;
using KeyLoad.UnitTests.Features.InternalSerialization;

namespace KeyLoad.UnitTests;

/// <summary>AC-IS-005: borrowed native command proof/type validation preserves actual authority and error markers.</summary>
internal sealed class ReplicaNativeAdmissionTests
{
    private const string Principal = "root";
    private const string InvalidJson = "{";
    private const string BooleanJson = "true";
    private const string WrongValue = "wrong-value-type";
    private const string WrongSender = "another-voter";
    private const int MaximumEntries = 2;
    private const int ControlBytes = 4_096;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualSignedSuccessAndDomainFailureMarkersRetainTheirAdmissionPool(bool failureMarker)
    {
        using var database = new TestDatabase();
        var kind = failureMarker ? OperationKind.Batch : OperationKind.SetDispatch;
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), kind, Principal, DateTimeOffset.UnixEpoch,
            failureMarker ? InvalidJson : BooleanJson));
        var inspected = ReplicaNativeInspection.Inspect<ReplicatedOperation>(ReplicaProtocolCodec.Serialize(operation), MaximumEntries);
        var before = database.Store.Position;
        var control = ReplicaNativeOperationAdmission.Validate(inspected, inspected.Value, database.Database, ControlBytes);
        await Assert.That(control).IsEqualTo(!failureMarker);
        await Assert.That(NativeAuthorityFixture.Read(operation).Error is not null).IsEqualTo(failureMarker);
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }

    [Test]
    public async Task MissingCanonicalAuthorityAndSubstitutedProofFailWithoutEffects()
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var inspected = ReplicaNativeInspection.Inspect<ReplicatedOperation>(ReplicaProtocolCodec.Serialize(operation), MaximumEntries);
        var before = database.Store.Position;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            ReplicaNativeOperationAdmission.Validate(inspected, inspected.Value, null, ControlBytes)).Code).IsEqualTo(ErrorCode.RecoveryRequired);
        var other = NativeAuthorityFixture.Create(database);
        var substituted = operation with
        {
            NativePayload = other.NativePayload
        };
        var changed = ReplicaNativeInspection.Inspect<ReplicatedOperation>(ReplicaProtocolCodec.Serialize(substituted), MaximumEntries);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            ReplicaNativeOperationAdmission.Validate(changed, changed.Value, database.Database, ControlBytes)).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }

    [Test]
    public async Task GenuineAuthorityCannotMakeWrongNativeValueTypeAdmissible()
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var payload = NativeAuthorityFixture.Read(operation);
        var wrong = NativeSerialization.Serialize(WrongValue);
        var wrapped = NativeAuthorityFixture.Wrap(operation, payload with
        {
            Value = wrong
        });
        var claims = NativeSerialization.Deserialize<NativeCommandAuthority>(payload.Authority.Span) with
        {
            ValueHash = SHA256.HashData(wrong)
        };
        var signed = NativeAuthorityFixture.Resign(database, wrapped, claims);
        var inspected = ReplicaNativeInspection.Inspect<ReplicatedOperation>(ReplicaProtocolCodec.Serialize(signed), MaximumEntries);
        var before = database.Store.Position;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            ReplicaNativeOperationAdmission.Validate(inspected, inspected.Value, database.Database, ControlBytes)).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }

    [Test]
    [Arguments(ReplicaMalformedCommandShape.MissingAuthority)]
    [Arguments(ReplicaMalformedCommandShape.WrongBodyReset)]
    [Arguments(ReplicaMalformedCommandShape.UnknownBody)]
    public async Task NativeWrapperPrimaryAndBodyGroupsRejectMalformedActualFrames(ReplicaMalformedCommandShape shape)
    {
        using var database = new TestDatabase();
        var operation = NativeAuthorityFixture.Create(database);
        var payload = NativeAuthorityFixture.Read(operation);
        var framed = ReplicaNativeFixtureWriter.Encode(payload, new ReplicaMalformedCommandCodec(shape));
        var malformed = operation with
        {
            NativePayload = framed.AsMemory(ReplicaProtocol.PayloadPrefixBytes)
        };
        var inspected = ReplicaNativeInspection.Inspect<ReplicatedOperation>(ReplicaProtocolCodec.Serialize(malformed), MaximumEntries);
        var before = database.Store.Position;
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            ReplicaNativeOperationAdmission.Validate(inspected, inspected.Value, database.Database, ControlBytes)).Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(database.Store.Position).IsEqualTo(before);
    }

    [Test]
    public async Task DeclaredSenderMismatchPrecedesLaterMalformedVoteFields()
    {
        var malformed = ReplicaNativeFixtureWriter.Vote(ReplicaMalformedVoteShape.Missing, new(Principal, 1, 0, 0));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() =>
            ReplicaNativeInspection.Inspect<VoteRequest>(malformed, MaximumEntries, expectedSender: WrongSender)).Code)
            .IsEqualTo(ErrorCode.PermissionDenied);
    }
}
