using KeyLoad.Clustering;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests;

/// <summary>AC-IS-001: execution replies retain product DTOs, exact user JSON, byte budgets and cancellation.</summary>
internal sealed class GrainReplyTests
{
    private const string Entity = "execution-query-row";
    private const string UserJson = "{\"n\":1.2300e+02,\"large\":9007199254740993,\"text\":\"Україна\"}";
    private const string AccessPath = "point";
    private const string Detail = "The operation was rejected.";
    private const long Revision = 1;
    private const long CutPosition = 2;
    private const int OneByte = 1;

    [Test]
    public async Task ReplyUsesActualQueryDtoWithUnchangedUserJsonAndExactByteCount()
    {
        var value = new QueryPage([new QueryRow(Entity, Revision, UserJson)], null, CutPosition, AccessPath);
        var reply = GrainReplyFactory.Value(value, UnitRoutingOptions.Routing(), CancellationToken.None);
        var decoded = (QueryPage)OrleansSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value!;
        await Assert.That(decoded.Rows.Single().Json).IsEqualTo(UserJson);
        await Assert.That(decoded.Rows.Single().EntityId).IsEqualTo(Entity);
        await Assert.That(decoded.Rows.Single().Revision).IsEqualTo(Revision);
        await Assert.That(decoded.CutPosition).IsEqualTo(CutPosition);
        await Assert.That(decoded.AccessPath).IsEqualTo(AccessPath);
        await Assert.That(reply.Payload.Length).IsEqualTo(checked((int)OrleansSerialization.Measure(new GrainValue(value))));
        await Assert.That(reply.Error).IsNull();
    }

    [Test]
    public async Task ActualStreamAcceptsExactByteLimitAndRejectsOneByteLess()
    {
        var options = UnitRoutingOptions.Routing();
        var value = new GrainValue(new BackupReceipt(Entity, CutPosition));
        var expected = OrleansSerialization.Serialize(value);
        using var exact = new GrainBoundedPayloadStream(expected.Length, options, CancellationToken.None);
        OrleansSerialization.Serialize(value, exact);
        await Assert.That(exact.Complete().SequenceEqual(expected)).IsTrue();
        await Assert.That(exact.Length).IsEqualTo((long)expected.Length);
        using var shortBuffer = new GrainBoundedPayloadStream(expected.Length - OneByte, options, CancellationToken.None);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Serialize(value, shortBuffer));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(shortBuffer.Length <= expected.Length - OneByte).IsTrue();
    }

    [Test]
    public async Task OperationUsesValueAndDomainFailurePrecedesCallerCancellation()
    {
        var value = new BackupReceipt(Entity, CutPosition);
        var reply = GrainReplyFactory.Operation(Result<object>.Succeed(value), UnitRoutingOptions.Routing(), CancellationToken.None);
        await Assert.That(OrleansSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value).IsEqualTo(value);
        Assert.ThrowsExactly<ArgumentNullException>(() => Result<object>.Succeed((object)null!));
        var empty = GrainReplyFactory.Value(null, UnitRoutingOptions.Routing(), CancellationToken.None);
        await Assert.That(OrleansSerialization.Deserialize<GrainValue>(empty.Payload.Span).Value).IsNull();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = GrainReplyFactory.Operation(Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, Detail)), UnitRoutingOptions.Routing(), cancellation.Token);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.SafeDetail).IsEqualTo(Detail);
        await Assert.That(failure.Payload.Length).IsEqualTo(0);
        await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() =>
            GrainReplyFactory.Operation(Result<object>.Succeed(value), UnitRoutingOptions.Routing(), cancellation.Token))).IsNotNull();
    }

    [Test]
    public async Task CancelledStreamCannotWriteOrComplete()
    {
        var options = UnitRoutingOptions.Routing();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var stream = new GrainBoundedPayloadStream(options.Value.MaximumReplyBytes, options, cancellation.Token);
        await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() =>
            OrleansSerialization.Serialize(new GrainValue(new BackupReceipt(Entity, CutPosition)), stream))).IsNotNull();
        await Assert.That(stream.Length).IsEqualTo(0L);
        await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() => stream.Complete())).IsNotNull();
    }
}
