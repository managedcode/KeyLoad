using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlPendingQueueAuthorityFlow
{
    private const string Reader = "sql-pending-reader";

    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store, QueueLifecycleTestState state, CancellationToken token)
    {
        var principal = new PrincipalRecord(Reader, state.Partition.TenantId,
            [new(state.Partition.DatabaseId, state.Lane.Queue, Capability.Query | Capability.QueueInspect)], ["*"]);
        Save(database, state, principal);
        var request = new QueryRequest(state.Partition,
            $"SELECT * FROM QUEUE_MESSAGES('{state.Lane.Queue}') ORDER BY id LIMIT {QueueLifecycleTestProtocol.Three}", AllowFullScan: true);
        var engine = new QueryEngine(database, UnitExecutionOptions.QueryExecution());
        await DeniedAsync(database, store, state, engine, request, token);
        principal = principal with
        {
            Grants = [new(state.Partition.DatabaseId, state.Lane.Queue,
            Capability.Query | Capability.QueueInspect | Capability.DeadLettersRead)],
            PolicyEpoch = principal.PolicyEpoch + QueueLifecycleTestProtocol.One
        };
        Save(database, state, principal);
        var image = QueueLifecycleImage.Capture(store, state.Lane);
        var position = store.Position;
        var page = engine.Execute(Reader, request, cancellationToken: token);
        var ast = engine.ExecuteAst(Reader, Ast(state), cancellationToken: token);
        await Assert.That(JsonDefaults.Serialize(page.Rows).SequenceEqual(JsonDefaults.Serialize(ast.Rows))).IsTrue();
        await Assert.That(page.Rows.Length).IsEqualTo(QueueLifecycleTestProtocol.Three);
        await Assert.That(page.Rows.Select(row => row.EntityId).SequenceEqual(new[]
            { QueueLifecycleTestProtocol.Parked, QueueLifecycleTestProtocol.Pending, QueueLifecycleTestProtocol.Held })).IsTrue();
        foreach (var row in page.Rows)
        { await SqlPendingQueueRowAssertions.RowAsync(database, state, row); }
        await Assert.That(store.Position).IsEqualTo(position);
        await QueueLifecycleImage.SameAsync(store, state.Lane, image);
        Save(database, state, principal with
        {
            Grants = [new(state.Partition.DatabaseId, state.Lane.Queue,
            Capability.Query | Capability.QueueInspect)],
            PolicyEpoch = principal.PolicyEpoch + QueueLifecycleTestProtocol.One
        });
        await DeniedAsync(database, store, state, engine, request, token);
    }

    private static AstQueryRequest Ast(QueueLifecycleTestState state)
        => new(state.Partition, new(state.Lane.Queue, null, [new("*", "*")], null,
            [new("/@id", false)], QueueLifecycleTestProtocol.Three, false,
            new(ModelQuerySourceKind.QueueMessages, state.Lane.Queue)), AllowFullScan: true);

    private static void Save(DatabaseEngine database, QueueLifecycleTestState state, PrincipalRecord principal)
        => state.Execute(database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal), Guid.NewGuid()).Get<PrincipalRecord>();

    private static async Task DeniedAsync(DatabaseEngine database, ZoneTreeStore store, QueueLifecycleTestState state,
        QueryEngine engine, QueryRequest request, CancellationToken token)
    {
        var image = QueueLifecycleImage.Capture(store, state.Lane);
        var position = store.Position;
        var sql = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Reader, request, cancellationToken: token));
        var ast = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(Reader, Ast(state), cancellationToken: token));
        var native = Assert.ThrowsExactly<KeyLoadException>(() => database.InspectMessage(Reader, state.Lane, QueueLifecycleTestProtocol.Pending));
        await Assert.That(sql.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(ast.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(native.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(store.Position).IsEqualTo(position);
        await QueueLifecycleImage.SameAsync(store, state.Lane, image);
    }
}
