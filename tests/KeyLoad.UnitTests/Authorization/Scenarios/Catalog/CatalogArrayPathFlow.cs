using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class CatalogArrayPathFlow
{
    internal const string Collection = "catalog-arrays";
    internal const string Row = "row";
    internal const string Reader = "catalog-reader";
    internal const string Root = "root";
    internal const string SecretPath = "/people/0/secret~0~1";
    internal const string ReadGrant = "catalog.read";
    internal const string UseGrant = "catalog.use";
    internal const string Original = "{\"people\":[{\"secret~/\":\"CANARY\",\"visible\":\"first\"},{\"secret~/\":\"SECOND\",\"visible\":\"second\"}],\"numbers\":{\"00\":\"zeroes\",\"+0\":\"plus\",\"-0\":\"minus\",\" 0\":\"space\",\"0 \":\"trail\",\"2147483648\":\"overflow\",\"١\":\"arabic\"},\"status\":\"open\"}";
    internal const string Hidden = "{\"people\":[{\"visible\":\"first\"},{\"secret~/\":\"SECOND\",\"visible\":\"second\"}],\"numbers\":{\"00\":\"zeroes\",\"+0\":\"plus\",\"-0\":\"minus\",\" 0\":\"space\",\"0 \":\"trail\",\"2147483648\":\"overflow\",\"١\":\"arabic\"},\"status\":\"open\"}";
    internal static readonly string[] InvalidIndices = ["00", "+0", "-0", " 0", "0 ", "2147483648", "١"];
    internal static readonly string[] ObjectValues = ["zeroes", "plus", "minus", "space", "trail", "overflow", "arabic"];
    internal static ImmutableArray<string> Omitted => ["/people/0/secret~0~1", "/people/00/visible", "/people/+0/visible",
        "/people/-0/visible", "/people/ 0/visible", "/people/0 /visible", "/people/2147483648/visible", "/people/١/visible"];

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        TestDatabase? db = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            db = new();
            await ExecuteAsync(db, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() =>
        {
            db?.Dispose();
            return Task.CompletedTask;
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase db, CancellationToken token)
    {
        SensitiveFieldPolicy[] policies = [new(SecretPath, "secret", ReadGrant, UseGrant),
            .. InvalidIndices.Select(index => new SensitiveFieldPolicy("/people/" + index + "/visible", "literal-path", "ungranted.read", "ungranted.use"))];
        var resource = db.Configure(Collection, ResourceKind.Collection, fields: policies);
        var command = new CommandRequest(Guid.NewGuid(), db.Partition, [new PutDocument(Collection, Row, Original)]);
        var before = db.Store.Position;
        var receipt = db.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        await CatalogArrayPathAssertions.ReceiptAsync(db.Store, command, receipt, before, 1L, "putDocument");
        var outcome = CatalogArrayPathAssertions.Outcome(db.Store, command);
        var principal = new PrincipalRecord(Reader, "tenant", [new("database", Collection,
            Capability.DocumentsRead | Capability.Query | Capability.DocumentsWrite)], []);
        await ConfigureAsync(db, principal);
        await CatalogArrayPathAssertions.CheckAsync(db.Database, db.Store, db.Partition, false, false, 1L, token);
        await CatalogArrayPathAssertions.ReplayAsync(db.Database, db.Store, command, receipt, outcome, token);
        await ConfigureAsync(db, principal with
        {
            FieldGrants = [UseGrant],
            PolicyEpoch = 2L
        });
        await CatalogArrayPathAssertions.CheckAsync(db.Database, db.Store, db.Partition, true, false, 1L, token);
        await ConfigureAsync(db, principal with
        {
            FieldGrants = [UseGrant, ReadGrant],
            PolicyEpoch = 3L
        });
        await CatalogArrayPathAssertions.CheckAsync(db.Database, db.Store, db.Partition, true, true, 1L, token);
        var resourceKey = KeySpace.Resource(db.Partition.TenantId, db.Partition.DatabaseId, Collection);
        var catalog = db.Store.Read(view => view.ReadOwnedValue(resourceKey))!;
        var patch = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PatchDocument(Collection, Row, [new("/status", PatchKind.Set, "\"closed\"")], 1L)]);
        before = db.Store.Position;
        var patched = db.Submit(OperationKind.Batch, patch, id: patch.CommandId).Get<CommitReceipt>();
        await CatalogArrayPathAssertions.ReceiptAsync(db.Store, patch, patched, before, 2L, "patchDocument");
        await Assert.That(db.Store.Read(view => view.ReadOwnedValue(resourceKey))!.AsSpan().SequenceEqual(catalog)).IsTrue();
        await Assert.That(JsonDefaults.Serialize(db.Store.Read(view => view.GetRecord<ResourceDefinition>(resourceKey))).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(resource))).IsTrue();
        var patchOutcome = CatalogArrayPathAssertions.Outcome(db.Store, patch);
        await CatalogArrayPathAssertions.CheckAsync(db.Database, db.Store, db.Partition, true, true, 2L, token);
        await CatalogArrayPathAssertions.ReplayAsync(db.Database, db.Store, patch, patched, patchOutcome, token);
        await CatalogArrayPathAssertions.ReplayAsync(db.Database, db.Store, command, receipt, outcome, token);
        await CatalogArrayPathLifetime.ColdAsync(db, command, receipt, outcome, patch, patched, patchOutcome, token);
    }

    private static async Task ConfigureAsync(TestDatabase db, PrincipalRecord principal)
    {
        var actual = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
    }
}
