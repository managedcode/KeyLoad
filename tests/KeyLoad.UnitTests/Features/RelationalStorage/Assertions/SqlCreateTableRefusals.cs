using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlCreateTableRefusals
{
    private const string UnsupportedType = "CREATE TABLE [never-created] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY, value FLOAT NOT NULL)";
    private const string UnsupportedDefault = "CREATE TABLE [never-created] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY, value BIGINT NOT NULL DEFAULT 1)";
    private const string UnsupportedAlter = "ALTER TABLE [never-created] ADD value BIGINT NOT NULL";
    private const string UnsupportedDrop = "DROP TABLE IF EXISTS [never-created]";
    private const string InvalidPrimary = "CREATE TABLE [never-created] (id NVARCHAR(MAX) NULL PRIMARY KEY)";
    private const string Malformed = "CREATE TABLE [never-created] (id NVARCHAR(MAX) NOT NULL PRIMARY KEY,";
    internal static async Task<Guid> RunAsync(TestDatabase fixture, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        var id = Guid.NewGuid();
        await RejectAsync(fixture, UnsupportedType, id, ErrorCode.UnsupportedCapability, token);
        await RejectAsync(fixture, UnsupportedDefault, id, ErrorCode.UnsupportedCapability, token);
        await RejectAsync(fixture, UnsupportedAlter, id, ErrorCode.UnsupportedCapability, token);
        await RejectAsync(fixture, UnsupportedDrop, id, ErrorCode.UnsupportedCapability, token);
        await RejectAsync(fixture, InvalidPrimary, id, ErrorCode.Validation, token);
        await RejectAsync(fixture, Malformed, id, ErrorCode.Validation, token);
        await RejectAsync(fixture, SqlCreateTableLiteral.Sql, Guid.Empty, ErrorCode.Validation, token);
        var limits = UnitExecutionOptions.DatabaseLimits();
        KeyLoadException? missing = null;
        try
        {
            SqlOperationCompiler.Compile(new(fixture.Partition, SqlCreateTableLiteral.Sql),
            limits, limits.Value.MaxBatchBytes, UnitExecutionOptions.QueryExecution(), token);
        }
        catch (KeyLoadException failure) { missing = failure; }
        await RequireAsync(missing, ErrorCode.Validation);
        await SqlCreateTableAssertions.UnchangedAsync(fixture.Store, image, cut);
        await CancelledAsync(fixture, id);
        await SqlCreateTableAssertions.UnchangedAsync(fixture.Store, image, cut);
        var repaired = SqlCreateTableLiteral.Compile(fixture.Partition, id, token);
        await Assert.That(repaired.CommandId).IsEqualTo(id);
        await SqlCreateTableAssertions.UnchangedAsync(fixture.Store, image, cut);
        return id;
    }
    private static async Task RejectAsync(TestDatabase fixture, string sql, Guid id, ErrorCode expected, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        KeyLoadException? refusal = null;
        try
        {
            SqlCreateTableLiteral.Compile(fixture.Partition, id, sql, token);
        }
        catch (KeyLoadException failure) { refusal = failure; }
        await RequireAsync(refusal, expected);
        await SqlCreateTableAssertions.UnchangedAsync(fixture.Store, image, cut);
    }
    private static async Task RequireAsync(KeyLoadException? refusal, ErrorCode expected)
    {
        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal!.GetType()).IsEqualTo(typeof(KeyLoadException));
        await Assert.That(refusal.Code).IsEqualTo(expected);
    }
    private static async Task CancelledAsync(TestDatabase fixture, Guid id)
    {
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var cancelled = new CancellationTokenSource();
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await cancelled.CancelAsync();
                OperationCanceledException? refusal = null;
                try
                {
                    SqlCreateTableLiteral.Compile(fixture.Partition, id, cancelled.Token);
                }
                catch (OperationCanceledException failure) { refusal = failure; }
                await Assert.That(refusal).IsNotNull();
                await Assert.That(refusal!.GetType()).IsEqualTo(typeof(OperationCanceledException));
                await Assert.That(refusal.CancellationToken).IsEqualTo(cancelled.Token);
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
