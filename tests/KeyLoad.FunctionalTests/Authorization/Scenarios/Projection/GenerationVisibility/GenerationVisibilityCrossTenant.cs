using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class GenerationVisibilityCrossTenant
{
    private const string Tenant = "other-tenant";
    private const string Reader = "other-reader";
    private const string DatabaseScope = "database";
    private const string ForeignJson = "{\"value\":99}";
    private const long InitialRevision = 1;
    private const long InitialEpoch = 1;
    private const string Scan = "bounded-full-scan";

    internal static async Task RunAsync(TestDatabase db, QueryEngine queries, CancellationToken token)
    {
        var partition = db.Partition with
        {
            TenantId = Tenant
        };
        var resource = new ResourceDefinition(TeamRowPolicyFlow.Collection, ResourceKind.Collection, partition.TransactionDomainId);
        var configured = db.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Tenant, DatabaseScope, resource)).Get<ResourceDefinition>();
        await Assert.That(JsonDefaults.Serialize(configured).AsSpan().SequenceEqual(JsonDefaults.Serialize(resource))).IsTrue();
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.Row, ForeignJson, Access: new(OwnerId: Reader))]);
        var receipt = db.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        MutationReceipt[] expectedReceipt = [new("putDocument", TeamRowPolicyFlow.Collection, TeamRowPolicyFlow.Row, InitialRevision)];
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations).AsSpan().SequenceEqual(JsonDefaults.Serialize(expectedReceipt))).IsTrue();
        await GenerationVisibilityFlow.ConfigureAsync(db, new(Reader, Tenant,
            [new(DatabaseScope, TeamRowPolicyFlow.Collection, Capability.DocumentsRead | Capability.Query)], [])
        {
            OwnerId = Reader,
            RestrictRows = true,
            PolicyEpoch = InitialEpoch
        });
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var cut = db.Store.Position;
        QueryRow[] expected = [new(TeamRowPolicyFlow.Row, InitialRevision, ForeignJson, false, [])];
        var page = queries.Execute(Reader, new(partition, GenerationVisibilityAssertions.Sql, AllowFullScan: true), cancellationToken: token);
        await Assert.That(JsonDefaults.Serialize(page.Rows).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await Assert.That(page.CutPosition).IsEqualTo(cut);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(Scan);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => queries.Execute(TeamRowPolicyFlow.Reader,
            new(partition, GenerationVisibilityAssertions.Sql, AllowFullScan: true), cancellationToken: token));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await TeamRowPolicyAssertions.UnchangedAsync(db.Store, image, cut);
        await TeamRowPolicyAssertions.VisibleAsync(db, db.Database, queries, db.Store, InitialRevision, TeamRowPolicyFlow.OriginalJson, token);
    }
}
