using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class CapabilityOutputOperations
{
    internal static async Task RunAsync(ZoneTreeStore store, PartitionRef partition,
        QueryEngine engine, int mode, CancellationToken token)
    {
        var image = SqlInnerJoinRetainedIdentityFlow.Image(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ExecutePagesAsync(engine, partition, mode, position, token), failures);
        await ServerFailureObserver.ObserveAsync(() => ConserveAsync(store, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecutePagesAsync(QueryEngine engine, PartitionRef partition, int mode,
        long position, CancellationToken token)
    {
        var manifest = engine.Capabilities;
        foreach (var version in new[] { 1, 2 })
        {
            await RejectAsync(() => engine.Execute(CapabilityOutputFixture.Reader,
                CapabilityOutputFixture.Sql(partition, false, version), cancellationToken: token), ErrorCode.BudgetExceeded);
            await RejectAsync(() => engine.ExecuteAst(CapabilityOutputFixture.Reader,
                Q2CapabilityFixtures.JsonRoundTrip(CapabilityOutputFixture.Ast(partition, false, version)), cancellationToken: token), ErrorCode.BudgetExceeded);
            var expected = new QueryPage([new("a", 1L, "{\"key\":\"a\"}", RedactedFields: []),
                new("b", 1L, "{\"key\":\"b\"}", RedactedFields: [])], null, position, "bounded-full-scan");
            var sql = engine.Execute(CapabilityOutputFixture.Reader, CapabilityOutputFixture.Sql(partition, true, version),
                cancellationToken: token);
            var request = CapabilityOutputFixture.Ast(partition, true, version);
            var ast = engine.ExecuteAst(CapabilityOutputFixture.Reader, Q2CapabilityFixtures.JsonRoundTrip(request),
                cancellationToken: token);
            var native = engine.ExecuteAst(CapabilityOutputFixture.Reader, Q2CapabilityFixtures.NativeRoundTrip(request),
                cancellationToken: token);
            await EqualAsync(expected, sql);
            await EqualAsync(expected, ast);
            await EqualAsync(expected, native);
            await Assert.That(JsonDefaults.Serialize(sql).Length).IsLessThanOrEqualTo(manifest.MaxBytes);
        }
        await RejectAsync(() => engine.ExecuteAst(CapabilityOutputFixture.Reader,
            CapabilityOutputFixture.Ast(partition, true, 3), cancellationToken: token), ErrorCode.UnsupportedCapability);
        await RejectAsync(() => engine.Execute(CapabilityOutputFixture.Reader,
            CapabilityOutputFixture.Sql(partition, true, 3), cancellationToken: token), ErrorCode.UnsupportedCapability);
        await Assert.That(manifest.MaxBytes).IsEqualTo(CapabilityOutputFixture.ExpectedBytes(mode));
        await Assert.That(manifest.MaxBytes).IsNotEqualTo(CapabilityOutputFixture.RequestBytes);
        await Assert.That(manifest.SqlDialect).IsEqualTo("TSQL.ScriptDOM180.v1");
        await Assert.That(manifest.AstVersion).IsEqualTo(1);
        await Assert.That(manifest.SupportedAstVersions).IsEquivalentTo(new[] { 1, 2 }, CollectionOrdering.Matching);
        await Assert.That(manifest.SupportedQueryDialectVersions).IsEquivalentTo(new[] { 1, 2 }, CollectionOrdering.Matching);
    }

    internal static async Task ConserveAsync(ZoneTreeStore store, string[] image, long position, byte[] identity)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(store.Position).IsEqualTo(position);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(SqlInnerJoinRetainedIdentityFlow.Image(store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(NativeSerialization.Serialize(store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task RejectAsync(Func<QueryPage> execute, ErrorCode code)
    {
        QueryPage? page = null;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => page = execute());
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(page).IsNull();
    }
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    internal static async Task ReplayAsync(DatabaseEngine database,
        (CommandRequest Command, CommitReceipt Receipt)[] seeds)
    {
        foreach (var seed in seeds)
        {
            var operation = database.CreateCommand(OperationKind.Batch, seed.Command.CommandId, "root", default, System.Text.Json.JsonSerializer.Serialize(seed.Command, JsonDefaults.Options));
            await EqualAsync(seed.Receipt, database.ApplyEmbedded(operation, cancellationToken: default).Get<CommitReceipt>());
        }
    }
}
