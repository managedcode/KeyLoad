using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Authorization;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingSql
{
    internal static async Task RunAsync(ResourcePairBindingState state, CancellationToken token)
    {
        var source = state.Source;
        var id = Guid.NewGuid();
        var compiled = SqlOperationCompiler.Compile(new(source.Partition, SqlInsert, CommandId: id),
            source.Database.OperationLimitsOptions, source.Database.Limits.MaxBatchBytes, UnitExecutionOptions.QueryExecution(), token);
        await Assert.That(compiled.CommandKind).IsEqualTo(OperationKind.Batch);
        await Assert.That(compiled.CommandId).IsEqualTo(id);
        var request = NativeSerialization.Deserialize<CommandRequest>(compiled.Payload.Span);
        await ResourcePairBindingAssertions.EqualAsync(new CommandRequest(id, source.Partition, [new InsertTypedRow(Table, TypedJson)]), request);
        var issued = source.Database.CreateNativeOperation(OperationKind.Batch, id, Root,
            source.Database.EvaluationClock.GetUtcNow(), compiled.Payload);
        var expectedIndex = source.ReadJournalCut().LastIndex + One;
        var actual = await ResourcePairBindingRecovery.ApplyAsync(state, issued, Table, token);
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
        await ResourcePairBindingAssertions.EqualAsync(new QueryPage([new(TypedId, FirstRevision, TypedJson, false, [])],
            null, database.Store.Position, SqlAccessPath), page);
        await ResourcePairBindingAssertions.ModelsAsync(state, database);
    }
}
