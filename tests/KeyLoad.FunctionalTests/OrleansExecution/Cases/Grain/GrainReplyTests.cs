using KeyLoad.Clustering;
using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests;

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
        var delivered = OrleansSerialization.Deserialize<GrainOperationReply>(OrleansSerialization.Serialize(reply));
        var decoded = (QueryPage)GrainReplyFactory.RequireValue(delivered).Value!;
        await Assert.That(decoded.Rows.Single().Json).IsEqualTo(UserJson);
        await Assert.That(decoded.Rows.Single().EntityId).IsEqualTo(Entity);
        await Assert.That(decoded.Rows.Single().Revision).IsEqualTo(Revision);
        await Assert.That(decoded.CutPosition).IsEqualTo(CutPosition);
        await Assert.That(decoded.AccessPath).IsEqualTo(AccessPath);
        await Assert.That(GrainReplyFactory.Measure(reply)).IsEqualTo((long)OrleansSerialization.Serialize(new GrainValue(value)).Length);
        await Assert.That(reply.Error).IsNull();
    }

    [Test]
    public async Task ActualCounterAcceptsExactByteLimitAndRejectsOneByteLess()
    {
        var options = UnitRoutingOptions.Routing();
        var value = new GrainValue(new BackupReceipt(Entity, CutPosition));
        var expected = OrleansSerialization.Serialize(value);
        using var exact = new GrainCountingWriter(expected.Length, options, CancellationToken.None);
        OrleansSerialization.SerializeToWriter(value, exact);
        await Assert.That(exact.Length).IsEqualTo((long)expected.Length);
        using var shortBuffer = new GrainCountingWriter(expected.Length - OneByte, options, CancellationToken.None);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.SerializeToWriter(value, shortBuffer));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(shortBuffer.Length <= expected.Length - OneByte).IsTrue();
        var exactOptions = UnitRoutingOptions.Routing(new() { MaximumReplyBytes = expected.Length });
        var delivered = GrainReplyFactory.Value(value.Value, exactOptions, CancellationToken.None);
        await Assert.That(GrainReplyFactory.Measure(delivered)).IsEqualTo((long)expected.Length);
        var limited = UnitRoutingOptions.Routing(new() { MaximumReplyBytes = expected.Length - OneByte });
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
            GrainReplyFactory.Value(value.Value, limited, CancellationToken.None));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(GrainReplyFactory.RequireValue(
            GrainReplyFactory.Value(value.Value, exactOptions, CancellationToken.None)).Value).IsEqualTo(value.Value);
    }

    [Test]
    public async Task OperationUsesValueAndDomainFailurePrecedesCallerCancellation()
    {
        var value = new BackupReceipt(Entity, CutPosition);
        var reply = GrainReplyFactory.Operation(Result<object>.Succeed(value), UnitRoutingOptions.Routing(), CancellationToken.None);
        await Assert.That(GrainReplyFactory.RequireValue(reply).Value).IsEqualTo(value);
        Assert.ThrowsExactly<ArgumentNullException>(() => Result<object>.Succeed((object)null!));
        var empty = GrainReplyFactory.Value(null, UnitRoutingOptions.Routing(), CancellationToken.None);
        await Assert.That(GrainReplyFactory.RequireValue(empty).Value).IsNull();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = GrainReplyFactory.Operation(Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, Detail)), UnitRoutingOptions.Routing(), cancellation.Token);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.SafeDetail).IsEqualTo(Detail);
        await Assert.That(failure.Result).IsNull();
        await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() =>
            GrainReplyFactory.Operation(Result<object>.Succeed(value), UnitRoutingOptions.Routing(), cancellation.Token))).IsNotNull();
    }

    [Test]
    public async Task CancelledCounterCannotWriteOrBorrowScratch()
    {
        var options = UnitRoutingOptions.Routing();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var stream = new GrainCountingWriter(options.Value.MaximumReplyBytes, options, cancellation.Token);
        await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() =>
            OrleansSerialization.SerializeToWriter(new GrainValue(new BackupReceipt(Entity, CutPosition)), stream))).IsNotNull();
        await Assert.That(stream.Length).IsEqualTo(0L);
        await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() => stream.GetMemory())).IsNotNull();
    }
}
