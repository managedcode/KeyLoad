using KeyLoad.UnitTests.Features.Messaging;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertPrincipalPolicy
{
    internal static async Task<SqlTypedInsertState> RunAsync(TestDatabase fixture, SqlTypedInsertState state, CancellationToken token)
    {
        var denied = state.Principal with
        {
            PolicyEpoch = DeniedEpoch,
            Grants = [new(fixture.Partition.DatabaseId, SqlTypedInsertLiteral.Table, Capability.DocumentsRead | Capability.Query)]
        };
        await ConfigureAsync(fixture, denied);
        var refused = SqlTypedInsertHot.Execute(fixture, SqlTypedInsertLiteral.Repair, token);
        await Assert.That(Errors.Code(refused.Result.Problem)).IsEqualTo(ErrorCode.PermissionDenied);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        await FenceAsync(fixture, state, SqlTypedInsertLiteral.ScopeDenied);
        await SqlTypedInsertAssertions.UnchangedAsync(fixture.Store, image, cut);
        await SqlTypedInsertResourcePolicy.BytesAsync(fixture, state);
        var repaired = state.Principal with
        {
            PolicyEpoch = RepairedEpoch
        };
        await ConfigureAsync(fixture, repaired);
        image = QueueWholeFlowStorage.Bytes(fixture.Store);
        cut = fixture.Store.Position;
        await FenceAsync(fixture, state);
        await SqlTypedInsertAssertions.SameAsync(Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, SqlTypedInsertLiteral.EpochChanged)),
            SqlTypedInsertLiteral.Apply(fixture.Database, refused.Command, refused.At));
        await SqlTypedInsertAssertions.UnchangedAsync(fixture.Store, image, cut);
        var healthy = SqlTypedInsertHot.Execute(fixture, SqlTypedInsertLiteral.Repair, token);
        await SqlTypedInsertAssertions.ReceiptAsync(healthy, new MutationReceipt(PutKind, SqlTypedInsertLiteral.Table, SqlTypedInsertLiteral.Three, InitialRevision));
        await SqlTypedInsertAssertions.ImageAsync(fixture, fixture.Store, fixture.Database, repaired: true);
        await SqlTypedInsertHot.ReplayAsync(fixture, healthy);
        await SqlTypedInsertResourcePolicy.BytesAsync(fixture, state);
        return state with
        {
            Principal = repaired,
            Repaired = healthy,
            EpochFenced = true
        };
    }
    internal static async Task FenceAsync(TestDatabase fixture, SqlTypedInsertState state,
        string safeDetail = SqlTypedInsertLiteral.EpochChanged)
    {
        var expected = Result<object>.Fail(Errors.Problem(ErrorCode.PermissionDenied, safeDetail));
        await SqlTypedInsertAssertions.SameAsync(expected, SqlTypedInsertLiteral.Apply(fixture.Database, state.Created.Command, state.Created.At));
        await SqlTypedInsertAssertions.SameAsync(expected, SqlTypedInsertLiteral.Apply(fixture.Database, state.Failed.Command, state.Failed.At));
    }
    private static async Task ConfigureAsync(TestDatabase fixture, PrincipalRecord expected)
        => await SqlTypedInsertAssertions.SameAsync(expected, fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>());
    private const long DeniedEpoch = 2;
    private const long RepairedEpoch = 3;
    private const string PutKind = "putDocument";
    private const long InitialRevision = 1;
}
