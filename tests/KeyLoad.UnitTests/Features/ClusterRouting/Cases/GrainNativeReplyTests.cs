using KeyLoad.Orleans;

namespace KeyLoad.UnitTests;

/// <summary>AC-IS-001: native replies retain product DTOs, exact user JSON, byte budgets and cancellation.</summary>
internal sealed class GrainNativeReplyTests
{
    private const string Entity = "native-query-row";
    private const string UserJson = "{\"n\":1.2300e+02,\"large\":9007199254740993,\"text\":\"Україна\"}";
    private const string AccessPath = "point";
    private const string Detail = "The operation was rejected.";
    private const long Revision = 1;
    private const long CutPosition = 2;
    private const int OneByte = 1;

    [Test]
    public async Task ReplyUsesActualQueryDtoWithUnchangedUserJsonAndExactNativeByteCount()
    {
        var value = new QueryPage([new QueryRow(Entity, Revision, UserJson)], null, CutPosition, AccessPath);
        var reply = GrainReplyFactory.Value(value, UnitRoutingOptions.Routing(), CancellationToken.None);
        var decoded = (QueryPage)NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value!;
        await Assert.That(decoded.Rows.Single().Json).IsEqualTo(UserJson);
        await Assert.That(decoded.Rows.Single().EntityId).IsEqualTo(Entity);
        await Assert.That(decoded.Rows.Single().Revision).IsEqualTo(Revision);
        await Assert.That(decoded.CutPosition).IsEqualTo(CutPosition);
        await Assert.That(decoded.AccessPath).IsEqualTo(AccessPath);
        await Assert.That(reply.Payload.Length).IsEqualTo(checked((int)NativeSerialization.Measure(new GrainValue(value))));
        await Assert.That(reply.Error).IsNull();
    }

    [Test]
    public async Task ActualNativeStreamAcceptsExactByteLimitAndRejectsOneByteLess()
    {
        var value = new GrainValue(new BackupReceipt(Entity, CutPosition));
        var expected = NativeSerialization.Serialize(value);
        using var exact = new GrainBoundedPayloadStream(expected.Length, CancellationToken.None);
        NativeSerialization.Serialize(value, exact);
        await Assert.That(exact.Complete().SequenceEqual(expected)).IsTrue();
        await Assert.That(exact.Length).IsEqualTo((long)expected.Length);
        using var shortBuffer = new GrainBoundedPayloadStream(expected.Length - OneByte, CancellationToken.None);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Serialize(value, shortBuffer));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(shortBuffer.Length <= expected.Length - OneByte).IsTrue();
    }

    [Test]
    public async Task OperationUsesNativeValueAndDomainFailurePrecedesCallerCancellation()
    {
        var value = new BackupReceipt(Entity, CutPosition);
        var reply = GrainReplyFactory.Operation(new(UserJson) { NativeValue = value }, UnitRoutingOptions.Routing(), CancellationToken.None);
        await Assert.That(NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value).IsEqualTo(value);
        var empty = GrainReplyFactory.Operation(new(null), UnitRoutingOptions.Routing(), CancellationToken.None);
        await Assert.That(NativeSerialization.Deserialize<GrainValue>(empty.Payload.Span).Value).IsNull();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = GrainReplyFactory.Operation(new(null, ErrorCode.PermissionDenied, Detail), UnitRoutingOptions.Routing(), cancellation.Token);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.SafeDetail).IsEqualTo(Detail);
        await Assert.That(failure.Payload.Length).IsEqualTo(0);
        await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() =>
            GrainReplyFactory.Operation(new(null) { NativeValue = value }, UnitRoutingOptions.Routing(), cancellation.Token))).IsNotNull();
    }

    [Test]
    public async Task CancelledNativeStreamCannotWriteOrComplete()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var stream = new GrainBoundedPayloadStream(UnitRoutingOptions.Routing().Value.MaximumReplyBytes, cancellation.Token);
        await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() =>
            NativeSerialization.Serialize(new GrainValue(new BackupReceipt(Entity, CutPosition)), stream))).IsNotNull();
        await Assert.That(stream.Length).IsEqualTo(0L);
        await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() => stream.Complete())).IsNotNull();
    }
}
