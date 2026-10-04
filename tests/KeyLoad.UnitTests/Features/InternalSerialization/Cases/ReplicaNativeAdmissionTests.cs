using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Replication;
using KeyLoad.UnitTests.Features.InternalSerialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

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
        var substituted = operation with { NativePayload = other.NativePayload };
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
        var wrapped = NativeAuthorityFixture.Wrap(operation, payload with { Value = wrong });
        var claims = NativeSerialization.Deserialize<NativeCommandAuthority>(payload.Authority.Span) with { ValueHash = SHA256.HashData(wrong) };
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
        var malformed = operation with { NativePayload = framed.AsMemory(ReplicaProtocol.PayloadPrefixBytes) };
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

internal enum ReplicaMalformedCommandShape { MissingAuthority, WrongBodyReset, UnknownBody }

// A real native writer profile emits invalid schema groups inside the shared authoritative envelope.
internal sealed class ReplicaMalformedCommandCodec(ReplicaMalformedCommandShape shape) : IFieldCodec<NativeCommandPayload>
{
    public NativeCommandPayload ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] NativeCommandPayload value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(NativeCommandPayload), WireType.TagDelimited);
        writer.WriteEndBase();
        writer.Session.CodecProvider.GetCodec<ReadOnlyMemory<byte>>().WriteField(ref writer, 0, typeof(ReadOnlyMemory<byte>), value.Value);
        writer.Session.CodecProvider.GetCodec<ErrorCode?>().WriteField(ref writer, 1, typeof(ErrorCode?), value.Error);
        StringCodec.WriteField(ref writer, 1, value.SafeDetail);
        if (shape == ReplicaMalformedCommandShape.WrongBodyReset)
        { writer.WriteEndBase(); }
        if (shape != ReplicaMalformedCommandShape.MissingAuthority)
        {
            var delta = shape == ReplicaMalformedCommandShape.WrongBodyReset ? NativeCommandContract.AuthorityId
                : NativeCommandContract.AuthorityId - NativeCommandContract.SafeDetailId;
            writer.Session.CodecProvider.GetCodec<ReadOnlyMemory<byte>>().WriteField(ref writer, delta, typeof(ReadOnlyMemory<byte>), value.Authority);
        }
        var signatureDelta = shape == ReplicaMalformedCommandShape.MissingAuthority
            ? NativeCommandContract.SignatureId - NativeCommandContract.SafeDetailId : 1U;
        writer.Session.CodecProvider.GetCodec<ReadOnlyMemory<byte>>().WriteField(ref writer, signatureDelta, typeof(ReadOnlyMemory<byte>), value.Signature);
        if (shape == ReplicaMalformedCommandShape.UnknownBody)
        {
            BoolCodec.WriteField(ref writer, 1, true);
        }
        writer.WriteEndObject();
    }
}
