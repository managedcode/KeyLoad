using KeyLoad.Clustering;
using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests;

/// <summary>AC-IS-001: execution replies retain product DTOs, exact user JSON and cancellation.</summary>
internal sealed class GrainReplyTests
{
    private const string Entity = "execution-query-row";
    private const string UserJson = "{\"n\":1.2300e+02,\"large\":9007199254740993,\"text\":\"Україна\"}";
    private const string AccessPath = "point";
    private const string Detail = "The operation was rejected.";
    private const long Revision = 1;
    private const long CutPosition = 2;

    [Test]
    public async Task ReplyUsesActualQueryDtoWithUnchangedUserJson()
    {
        var value = new QueryPage([new QueryRow(Entity, Revision, UserJson)], null, CutPosition, AccessPath);
        var reply = GrainReplyFactory.Value(value);
        var delivered = OrleansSerialization.Deserialize<GrainOperationReply>(OrleansSerialization.Serialize(reply));
        var decoded = (QueryPage)GrainReplyFactory.RequireValue(delivered).Value!;
        await Assert.That(decoded.Rows.Single().Json).IsEqualTo(UserJson);
        await Assert.That(decoded.Rows.Single().EntityId).IsEqualTo(Entity);
        await Assert.That(decoded.Rows.Single().Revision).IsEqualTo(Revision);
        await Assert.That(decoded.CutPosition).IsEqualTo(CutPosition);
        await Assert.That(decoded.AccessPath).IsEqualTo(AccessPath);
        await Assert.That(reply.Error).IsNull();
    }

    [Test]
    public async Task OperationUsesValueAndDomainFailurePrecedesCallerCancellation()
    {
        var value = new BackupReceipt(Entity, CutPosition);
        var reply = GrainReplyFactory.Operation(Result<object>.Succeed(value), CancellationToken.None);
        await Assert.That(GrainReplyFactory.RequireValue(reply).Value).IsEqualTo(value);
        Assert.ThrowsExactly<ArgumentNullException>(() => Result<object>.Succeed((object)null!));
        var empty = GrainReplyFactory.Value(null);
        await Assert.That(GrainReplyFactory.RequireValue(empty).Value).IsNull();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var failure = GrainReplyFactory.Operation(Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, Detail)), cancellation.Token);
        await Assert.That(failure.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.SafeDetail).IsEqualTo(Detail);
        await Assert.That(failure.Result).IsNull();
        await Assert.That(Assert.ThrowsExactly<OperationCanceledException>(() =>
            GrainReplyFactory.Operation(Result<object>.Succeed(value), cancellation.Token))).IsNotNull();
    }
}
