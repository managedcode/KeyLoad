using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.UnitTests.Authorization;
using static KeyLoad.UnitTests.ResourceExecution.ResourcePairBindingState;

namespace KeyLoad.UnitTests.ResourceExecution;

internal static class ResourcePairBindingSql
{
    internal static async Task RunAsync(ResourcePairBindingState state, CancellationToken token)
    {
        var source = state.Source;
        var id = Guid.NewGuid();
        var compiled = SqlOperationCompiler.Compile(new(source.Partition, SqlInsert, CommandId: id),
            UnitMcpOptions.CanonicalPayloadBytes, UnitExecutionOptions.QueryExecution(), token);
        await Assert.That(compiled.CommandKind).IsEqualTo(OperationKind.Batch);
        await Assert.That(compiled.CommandId).IsEqualTo(id);
        var request = OrleansSerialization.Deserialize<CommandRequest>(compiled.Payload.Span);
        await ResourceIncarnationAssertions.EqualAsync(new CommandRequest(id, source.Partition, [new InsertTypedRow(Table, TypedJson)]), request);
        var issued = source.Database.CreateCommand(OperationKind.Batch, id, Root,
            source.Database.EvaluationClock.GetUtcNow(), compiled.Payload);
        var expectedIndex = source.Database.LastApplied + One;
        var actual = await ResourcePairBindingFlow.ApplyAfterRepairAsync(state, issued, Table, token);
        var expected = ResourcePairBindingAssertions.Receipt(source.Store, source.Partition, id,
            expectedIndex, [new(PutKind, Table, TypedId, FirstRevision)]);
        await RequiredProcessingPolicyAssertions.ReceiptAsync(expected, actual.Result.Get<CommitReceipt>());
        state.Rows.Add((TypedId, TypedJson));
        await HealthyAsync(state, source.Database, token);
    }

    internal static async Task HealthyAsync(ResourcePairBindingState state, DatabaseEngine database, CancellationToken token)
    {
        var page = new QueryEngine(database, UnitExecutionOptions.QueryExecution()).Execute(Root,
            new(state.Source.Partition, SqlSelect, AllowFullScan: true), cancellationToken: token);
        await ResourceIncarnationAssertions.EqualAsync(new QueryPage([new(TypedId, FirstRevision, TypedJson, false, [])],
            null, database.Store.Position, SqlAccessPath), page);
        await ResourcePairBindingAssertions.ModelsAsync(state, database);
    }
}
