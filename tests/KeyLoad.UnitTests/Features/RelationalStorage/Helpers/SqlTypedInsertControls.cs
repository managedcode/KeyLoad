using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertControls
{
    internal static async Task RunAsync(TestDatabase fixture, SqlTypedInsertState state, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        foreach (var sql in Unsupported)
        { await RefusalAsync(() => SqlTypedInsertLiteral.Compile(fixture.Partition, Guid.NewGuid(), sql, token), ErrorCode.UnsupportedCapability); }
        await RefusalAsync(() => SqlTypedInsertLiteral.Compile(fixture.Partition, Guid.NewGuid(), DuplicateColumns, token), ErrorCode.Validation);
        var options = UnitExecutionOptions.DatabaseLimits();
        await RefusalAsync(() => SqlOperationCompiler.Compile(new(fixture.Partition, SqlTypedInsertLiteral.Repair), options,
            options.Value.MaxBatchBytes, UnitExecutionOptions.QueryExecution(), token), ErrorCode.Validation);
        await BoundAsync(fixture, new DatabaseLimits { MaxBatchMutations = SingleRow }, SqlTypedInsertLiteral.Sql, token);
        await BoundAsync(fixture, new DatabaseLimits { MaxDocumentBytes = ShortRowBytes }, SqlTypedInsertLiteral.Repair, token);
        await CancelAsync(fixture, token);
        var changed = SqlTypedInsertLiteral.Compile(fixture.Partition, state.Created.Command.CommandId, SqlTypedInsertLiteral.Repair, token);
        await Assert.That(SqlTypedInsertLiteral.Apply(fixture.Database, changed, state.Created.At).Error).IsEqualTo(ErrorCode.Conflict);
        await SqlTypedInsertAssertions.UnchangedAsync(fixture.Store, image, cut);
        await SqlTypedInsertHot.ReplayAsync(fixture, state.Created);
        await SqlTypedInsertAssertions.ImageAsync(fixture, fixture.Store, fixture.Database, repaired: false);
    }
    private static async Task BoundAsync(TestDatabase fixture, DatabaseLimits value, string sql, CancellationToken token)
    {
        var options = UnitExecutionOptions.DatabaseLimits(value);
        await RefusalAsync(() => SqlOperationCompiler.Compile(new(fixture.Partition, sql, CommandId: Guid.NewGuid()), options,
            options.Value.MaxBatchBytes, UnitExecutionOptions.QueryExecution(), token), ErrorCode.ResourceExhausted);
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
    private const int SingleRow = 1;
    private const int ShortRowBytes = 16;
}
