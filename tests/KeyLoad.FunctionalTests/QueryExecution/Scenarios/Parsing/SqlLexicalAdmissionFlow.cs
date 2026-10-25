namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlLexicalAdmissionFlow
{
    private const string Invalid = "The SQL syntax or query arguments are invalid.";
    private const string Unsupported = "The SQL statement or execution clause is unsupported.";

    internal static async Task RunAsync(global::KeyLoad.Core.DatabaseEngine owner, global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store,
        global::KeyLoad.PartitionRef partition, global::KeyLoad.CommandRequest command, global::KeyLoad.CommitReceipt receipt)
    {
        var engine = new global::KeyLoad.Query.QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var before = global::KeyLoad.FunctionalTests.Messaging.QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = global::KeyLoad.JsonDefaults.Serialize(store.Identity);
        var healthy = SqlJoinSourceBindingFixture.Request(partition, SqlLexicalAdmissionFixture.Kind);
        var ast = SqlJoinSourceBindingFixture.Ast(partition, SqlLexicalAdmissionFixture.Kind);
        foreach (var fault in SqlLexicalAdmissionFixture.Faults(healthy))
        {
            var execution = store.GetReadDiagnostics();
            global::KeyLoad.QueryPage? page = null;
            var error = Assert.ThrowsExactly<global::KeyLoad.KeyLoadException>(() =>
                page = engine.Execute(SqlJoinSourceBindingFixture.Root, healthy with
                {
                    Sql = fault.Sql
                }));
            await Assert.That(page).IsNull();
            await Assert.That(error.Code).IsEqualTo(fault.Code);
            await Assert.That(error.Message).IsEqualTo(fault.Code == global::KeyLoad.ErrorCode.Validation ? Invalid : Unsupported);
            await Assert.That(store.GetReadDiagnostics()).IsEqualTo(execution);
            await SqlJoinSourceBindingFlow.UnchangedAsync(store, before, position, identity);
            await SqlJoinSourceBindingAssertions.PageAsync(engine.Execute(SqlJoinSourceBindingFixture.Root, healthy), SqlLexicalAdmissionFixture.Kind, position);
            await SqlJoinSourceBindingAssertions.PageAsync(engine.ExecuteAst(SqlJoinSourceBindingFixture.Root, ast), SqlLexicalAdmissionFixture.Kind, position);
            await SqlJoinSourceBindingAssertions.PageAsync(engine.Execute(SqlJoinSourceBindingFixture.Root,
                SqlLexicalAdmissionFixture.LiteralSql(healthy)), SqlLexicalAdmissionFixture.Kind, position);
            await SqlJoinSourceBindingAssertions.PageAsync(engine.ExecuteAst(SqlJoinSourceBindingFixture.Root,
                SqlLexicalAdmissionFixture.LiteralAst(ast)), SqlLexicalAdmissionFixture.Kind, position);
            var replay = owner.ApplyEmbedded(owner.CreateCommand(global::KeyLoad.OperationKind.Batch, command.CommandId, SqlJoinSourceBindingFixture.Root, default, global::System.Text.Json.JsonSerializer.Serialize(command, global::KeyLoad.JsonDefaults.Options)),
                cancellationToken: default).Get<global::KeyLoad.CommitReceipt>();
            await Assert.That(global::KeyLoad.JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(global::KeyLoad.JsonDefaults.Serialize(receipt))).IsTrue();
            await SqlJoinSourceBindingFlow.UnchangedAsync(store, before, position, identity);
        }
    }
}
