using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlBetweenAuthorityContinuation
{
    internal const string Reader = "reader";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Use = "pii.use";
    private const string Read = "pii.read";
    private const long UseEpoch = 2;
    private const long ReadEpoch = 3;
    private const long RevokedEpoch = 4;
    private const long RepairEpoch = 5;
    internal const long ColdReducedEpoch = 6;
    internal const long ColdRepairEpoch = 7;

    internal static async Task RunAsync(TestDatabase db)
    {
        await ConfigureAsync(db.Database, UseEpoch, raw: false);
        await SqlBetweenAuthorityResults.PageAsync(db, db.Database, db.Store, raw: false);
        await ConfigureAsync(db.Database, ReadEpoch, raw: true);
        await SqlBetweenAuthorityResults.PageAsync(db, db.Database, db.Store, raw: true);
        await ConfigureAsync(db.Database, RevokedEpoch, raw: true, revoked: true);
        await SqlBetweenAuthorityResults.RevokedAsync(db, db.Database, db.Store);
        await ConfigureAsync(db.Database, RepairEpoch, raw: true);
        await SqlBetweenAuthorityResults.PageAsync(db, db.Database, db.Store, raw: true);
        await SqlBetweenAuthorityCold.RunAsync(db);
    }

    internal static async Task ConfigureAsync(DatabaseEngine database, long epoch, bool raw, bool revoked = false)
    {
        PrincipalRecord principal = new(Reader, Tenant,
            [new(Database, SqlBetweenTestData.Collection, Capability.DocumentsRead | Capability.Query)],
            raw ? [Use, Read] : [Use])
        { Revoked = revoked, PolicyEpoch = epoch };
        var request = new ConfigurePrincipalRequest(principal);
        var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.ConfigurePrincipal,
            SqlBetweenTestData.Root, database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options));
        var configured = database.ApplyEmbedded(operation, cancellationToken: default).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(configured).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
    }
}
