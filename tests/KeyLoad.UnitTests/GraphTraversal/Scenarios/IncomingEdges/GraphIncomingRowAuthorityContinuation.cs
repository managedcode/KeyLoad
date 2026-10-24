using System.Collections.Immutable;

namespace KeyLoad.UnitTests.GraphTraversal;

internal static class GraphIncomingRowAuthorityContinuation
{
    private const long OriginalRevision = 1;
    private const string Root = "root";
    private const string Database = "database";
    private const string Collection = "vertices";
    private const string EmptyJson = "{}";
    private const string MissingOutcome = "The original row repair outcome is absent.";
    private const long ReducedEpoch = 2;
    private const long RevokedEpoch = 3;
    private const long RepairedEpoch = 4;

    internal static async Task RunAsync(TestDatabase database, EntityRef source, EntityRef target,
        PrincipalRecord original, bool repairTarget)
    {
        List<(CommandRequest Request, CommitReceipt Receipt, byte[] Outcome)> repairs =
            [Repair(database, source, original.Id)];
        if (repairTarget)
        {
            repairs.Add(Repair(database, target, original.Id));
        }
        await GraphIncomingRowAuthorityAssertions.HealthyAsync(database.Database, database.Store,
            source, target, original.Id, repairTarget);
        await ConfigureAsync(database, original with
        {
            PolicyEpoch = ReducedEpoch,
            Grants = ImmutableArray.Create(new ScopeGrant(Database, Collection, Capability.DocumentsRead))
        });
        await GraphIncomingRowAuthorityAssertions.RefusedAsync(database.Database, database.Store,
            target, original.Id, ErrorCode.PermissionDenied);
        await ConfigureAsync(database, original with
        {
            PolicyEpoch = RevokedEpoch,
            Revoked = true
        });
        await GraphIncomingRowAuthorityAssertions.RefusedAsync(database.Database, database.Store,
            target, original.Id, ErrorCode.Unauthenticated);
        await ConfigureAsync(database, original with
        {
            PolicyEpoch = RepairedEpoch
        });
        await GraphIncomingRowAuthorityAssertions.HealthyAsync(database.Database, database.Store,
            source, target, original.Id, repairTarget);
        await GraphIncomingRowAuthorityCold.RunAsync(database, source, target, original.Id, repairTarget, repairs);
    }

    private static (CommandRequest Request, CommitReceipt Receipt, byte[] Outcome) Repair(
        TestDatabase database, EntityRef vertex, string owner)
    {
        var id = Guid.NewGuid();
        var request = new CommandRequest(id, vertex.Partition,
            [new PutDocument(vertex.Collection, vertex.Id, EmptyJson, ExpectedRevision: OriginalRevision,
                Access: new RowAccess(owner))]);
        var receipt = database.Submit(OperationKind.Batch, request, id: id).Get<CommitReceipt>();
        var outcome = database.Store.Read(view => view.ReadOwnedValue(
            KeySpace.PartitionOutcome(vertex.Partition, Root, id)))
            ?? throw new InvalidOperationException(MissingOutcome);
        return (request, receipt, outcome);
    }

    private static async Task ConfigureAsync(TestDatabase database, PrincipalRecord principal)
    {
        var actual = database.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(principal, JsonDefaults.Options));
    }
}
