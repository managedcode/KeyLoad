using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;
namespace KeyLoad.UnitTests.Features.RelationalStorage;

internal static class SqlTypedInsertParameterParity
{
    internal static async Task RunAsync(TestDatabase fixture, SqlTypedInsertState state, CancellationToken token)
    {
        await global::KeyLoad.UnitTests.Features.QueryExecution.ScriptDomOrdinalWriteAdmission.RunAsync(fixture, state, token);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var cut = fixture.Store.Position;
        var limits = UnitExecutionOptions.DatabaseLimits();
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [CanonicalSlot] = JsonSerializer.SerializeToElement(true)
        };
        var request = new SqlOperationRequest(fixture.Partition, WireSql, values,
            CommandId: state.Created.Command.CommandId);
        var wire = SqlOperationCompiler.CompileSession(request, limits, limits.Value.MaxBatchBytes,
            UnitExecutionOptions.QueryExecution(), SlotCount, token);
        await SqlTypedInsertAssertions.SameAsync(state.Created.Command.Payload, wire.Payload);
        await SqlTypedInsertAssertions.SameAsync(state.Created.Result,
            SqlTypedInsertLiteral.Apply(fixture.Database, wire, state.Created.At));
        await SqlTypedInsertAssertions.UnchangedAsync(fixture.Store, image, cut);
        await SqlTypedInsertAssertions.SameAsync(state.Created.Bytes,
            SqlTypedInsertAssertions.OutcomeBytes(fixture.Store, fixture.Partition, wire.CommandId));
        await RefuseAsync(() => SqlOperationCompiler.Compile(request, limits, limits.Value.MaxBatchBytes,
            UnitExecutionOptions.QueryExecution(), token), ErrorCode.UnsupportedCapability);
        await RefuseAsync(() => SqlOperationCompiler.CompileSession(request with
        {
            Sql = InvalidSlot
        },
            limits, limits.Value.MaxBatchBytes, UnitExecutionOptions.QueryExecution(), SlotCount, token), ErrorCode.Validation);
        await RefuseAsync(() => SqlOperationCompiler.CompileSession(request with
        {
            Sql = SqlTypedInsertLiteral.Sql
        },
            limits, limits.Value.MaxBatchBytes, UnitExecutionOptions.QueryExecution(), SlotCount, token), ErrorCode.Validation);
        await SqlTypedInsertAssertions.UnchangedAsync(fixture.Store, image, cut);
        await SqlTypedInsertHot.ReplayAsync(fixture, state.Created);
        await SqlTypedInsertAssertions.ImageAsync(fixture, fixture.Store, fixture.Database, repaired: false);
    }
    private static async Task RefuseAsync(Action action, ErrorCode expected)
    {
        var error = await Assert.ThrowsAsync<KeyLoadException>(() =>
        {
            action();
            return Task.CompletedTask;
        })
            ?? throw new InvalidOperationException(MissingFailure);
        await Assert.That(error.Code).IsEqualTo(expected);
    }
    private const int SlotCount = 1;
    private const string CanonicalSlot = "p1";
    private const string MissingFailure = "The original execution INSERT parameter refusal is missing.";
    private const string WireSql = "INSERT INTO [sql-values] (id,title,amount,active,price,at) VALUES (N'one',N'alpha',7,$01,12.5,N'2026-10-14T00:00:00Z'),(N'two',N'beta',NULL,NULL,NULL,NULL)";
    private const string InvalidSlot = "INSERT INTO [sql-values] (id,title,active) VALUES (N'four',N'delta',$0)";
}
