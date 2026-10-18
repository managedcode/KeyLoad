using KeyLoad.Query;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class CursorExpiryBoundaryTests
{
    private const string Reader = "reader";
    private const string Tenant = "tenant";
    private const string DatabaseScope = "database";
    private const string Collection = "orders";
    private const string FirstId = "a";
    private const string SecondId = "b";
    private const string Json = "{}";
    private const long RevokedEpoch = 2;
    private static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactCursorExpiryRefusesThenFreshPolicyRepairReplayAndColdRemainHealthy(bool ast)
    {
        var clock = new ControlledReadClock(Epoch);
        using var db = new TestDatabase(timeProvider: clock);
        db.Configure(Collection, ResourceKind.Collection);
        var seed = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PutDocument(Collection, FirstId, Json), new PutDocument(Collection, SecondId, Json)]);
        var seedCut = db.Store.Position;
        var expectedToken = db.NextCommitToken();
        var seeded = db.Submit(OperationKind.Batch, seed, id: seed.CommandId);
        await CursorExpiryBoundaryAssertions.ReceiptAsync(db, seed, seeded.Get<CommitReceipt>(), seedCut, expectedToken);
        var principal = new PrincipalRecord(Reader, Tenant,
            [new(DatabaseScope, Collection, Capability.DocumentsRead | Capability.Query)], []);
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        var options = UnitExecutionOptions.QueryExecution();
        var queries = new QueryEngine(db.Database, options);
        var request = new QueryRequest(db.Partition, "SELECT TOP (1) * FROM orders ORDER BY id", AllowFullScan: true);
        var original = await CursorExpiryBoundaryAssertions.RunAsync(db, queries, request, ast, clock,
            options.Value.CursorLifetime, TestContext.Current!.Execution.CancellationToken);
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var cut = db.Store.Position;
        var replay = db.Submit(OperationKind.Batch, seed, id: seed.CommandId);
        await Assert.That(JsonDefaults.Serialize(replay).AsSpan().SequenceEqual(JsonDefaults.Serialize(seeded))).IsTrue();
        await TeamRowPolicyAssertions.UnchangedAsync(db.Store, image, cut);
        var revoked = principal with
        {
            Revoked = true,
            PolicyEpoch = RevokedEpoch
        };
        var persisted = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(persisted).AsSpan().SequenceEqual(JsonDefaults.Serialize(revoked))).IsTrue();
        await PolicyEpochCursorContinuation.RunAsync(db, request, original,
            TestContext.Current!.Execution.CancellationToken);
    }
}
