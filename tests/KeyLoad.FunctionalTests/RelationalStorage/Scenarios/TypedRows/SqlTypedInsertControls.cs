using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Server;
namespace KeyLoad.FunctionalTests.RelationalStorage;

internal static class SqlTypedInsertControls
{
    internal static async Task RunAsync(TestDatabase fixture, SqlTypedInsertState state, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        foreach (var sql in Unsupported)
        {
            await RefusalAsync(() => SqlTypedInsertLiteral.Compile(fixture.Partition, Guid.NewGuid(), sql, token), ErrorCode.UnsupportedCapability);
        }
        await RefusalAsync(() => SqlTypedInsertLiteral.Compile(fixture.Partition, Guid.NewGuid(), DuplicateColumns, token), ErrorCode.Validation);
        await RefusalAsync(() => SqlOperationCompiler.Compile(new(fixture.Partition, SqlTypedInsertLiteral.Repair), UnitMcpOptions.CanonicalPayloadBytes, UnitExecutionOptions.QueryExecution(), token), ErrorCode.Validation);
        await CancelAsync(fixture, token);
        var changed = SqlTypedInsertLiteral.Compile(fixture.Partition, state.Created.Command.CommandId, SqlTypedInsertLiteral.Repair, token);
        await Assert.That(Errors.Code(SqlTypedInsertLiteral.Apply(fixture.Database, changed, state.Created.At).Problem).Value).IsEqualTo(ErrorCode.Conflict);
        await SqlTypedInsertAssertions.UnchangedAsync(fixture.Store, image, cut);
        await SqlTypedInsertHot.ReplayAsync(fixture, state.Created);
        await SqlTypedInsertAssertions.ImageAsync(fixture, fixture.Store, fixture.Database, repaired: false);
    }
    private static async Task RefusalAsync(Func<object> operation, ErrorCode expected)
    {
        var error = await Assert.ThrowsAsync<KeyLoadException>(() => Task.FromResult(operation()))
            ?? throw new InvalidOperationException(MissingRefusal);
        await Assert.That(error.Code).IsEqualTo(expected);
    }
    private const string MissingRefusal = "The original SQL INSERT refusal was not retained.";

    private static async Task CancelAsync(TestDatabase fixture, CancellationToken token)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var original = CancellationTokenSource.CreateLinkedTokenSource(token);
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await original.CancelAsync();
                var error = await Assert.ThrowsAsync<OperationCanceledException>(() => Task.FromResult(
                    SqlTypedInsertLiteral.Compile(fixture.Partition, Guid.NewGuid(), SqlTypedInsertLiteral.Repair, original.Token)))
                    ?? throw new InvalidOperationException(MissingCancellation);
                await Assert.That(error.CancellationToken).IsEqualTo(original.Token);
                _ = SqlTypedInsertLiteral.Compile(fixture.Partition, Guid.NewGuid(), SqlTypedInsertLiteral.Repair, token);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
    private static readonly string[] Unsupported =
    ["INSERT INTO [sql-values] DEFAULT VALUES", "INSERT INTO [sql-values] (id,title) SELECT N'x',N'x'",
        "INSERT INTO [sql-values] (id,title) OUTPUT inserted.id VALUES (N'x',N'x')",
        "INSERT INTO dbo.[sql-values] (id,title) VALUES (N'x',N'x')"];
    private const string DuplicateColumns = "INSERT INTO [sql-values] (id,id) VALUES (N'x',N'x')";
    private const string MissingCancellation = "The original SQL INSERT token was not retained.";
}
