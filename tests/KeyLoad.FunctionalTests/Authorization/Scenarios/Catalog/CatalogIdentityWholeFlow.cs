using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class CatalogIdentityWholeFlow
{
    internal static async Task RunAsync(bool credential, CancellationToken token)
    {
        TestDatabase? db = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            db = new();
            await ExecuteAsync(db, credential, token);
        }, failures);
        ServerFailureObserver.Observe(() => db?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase db, bool credential, CancellationToken token)
    {
        db.Configure(CatalogIdentityProtocol.Collection, ResourceKind.Collection,
            fields: [new("/secret", "private", CatalogIdentityProtocol.Read, CatalogIdentityProtocol.Use)]);
        var seed = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PutDocument(CatalogIdentityProtocol.Collection, CatalogIdentityProtocol.Row, CatalogIdentityProtocol.Original)]);
        var before = db.Store.Position;
        var expectedToken = db.NextCommitToken();
        var receipt = db.Submit(OperationKind.Batch, seed, id: seed.CommandId).Get<CommitReceipt>();
        await CatalogIdentityAssertions.ReceiptAsync(db.Store, seed, receipt, before, expectedToken, 1L, "putDocument");
        var seedOutcome = CatalogIdentityAssertions.Outcome(db.Store, seed, CatalogIdentityProtocol.Root);
        await ConfigureAsync(db, CatalogIdentityProtocol.Principal);
        await Assert.That(db.Submit(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(AuthorizationExecution.Credential(
            CatalogIdentityProtocol.Key, CatalogIdentityProtocol.Reader, CatalogIdentityProtocol.Secret))).Get<bool>()).IsTrue();
        await CatalogIdentityAssertions.HealthyAsync(db, db.Database, db.Store, db.Partition, false, false, 1L, token);
        await CatalogIdentityFault.RunAsync(db, credential, token);
        await CatalogIdentityAssertions.HealthyAsync(db, db.Database, db.Store, db.Partition, false, false, 1L, token);
        await ConfigureAsync(db, CatalogIdentityProtocol.Principal with
        {
            FieldGrants = [CatalogIdentityProtocol.Use],
            PolicyEpoch = 2L
        });
        await CatalogIdentityAssertions.HealthyAsync(db, db.Database, db.Store, db.Partition, true, false, 1L, token);
        await ConfigureAsync(db, CatalogIdentityProtocol.Principal with
        {
            Revoked = true,
            PolicyEpoch = 3L
        });
        await CatalogIdentityAssertions.RefusedAsync(db.Database, db.Store, db.Partition,
            ErrorCode.Unauthenticated, CatalogIdentityProtocol.Unavailable, true, token);
        await ConfigureAsync(db, CatalogIdentityProtocol.Principal with
        {
            FieldGrants = [CatalogIdentityProtocol.Use, CatalogIdentityProtocol.Read],
            PolicyEpoch = 4L
        });
        await CatalogIdentityAssertions.HealthyAsync(db, db.Database, db.Store, db.Partition, true, true, 1L, token);
        var resourceKey = KeySpace.Resource(db.Partition.TenantId, db.Partition.DatabaseId, CatalogIdentityProtocol.Collection);
        var catalog = db.Store.Read(view => view.ReadOwnedValue(resourceKey))!;
        var patch = new CommandRequest(Guid.NewGuid(), db.Partition,
            [new PatchDocument(CatalogIdentityProtocol.Collection, CatalogIdentityProtocol.Row,
                [new("/public", PatchKind.Set, "\"closed\"")], 1L)]);
        before = db.Store.Position;
        expectedToken = db.NextCommitToken();
        var verified = db.Database.Authenticate(CatalogIdentityProtocol.Secret, db.Database.EvaluationClock.GetUtcNow());
        await Assert.That(verified).IsEqualTo(CatalogIdentityProtocol.Reader);
        var current = db.Submit(OperationKind.Batch, patch, verified, patch.CommandId).Get<CommitReceipt>();
        await CatalogIdentityAssertions.ReceiptAsync(db.Store, patch, current, before, expectedToken, 2L, "patchDocument");
        var patchOutcome = CatalogIdentityAssertions.Outcome(db.Store, patch, CatalogIdentityProtocol.Reader);
        await Assert.That(db.Store.Read(view => view.ReadOwnedValue(resourceKey))!.AsSpan().SequenceEqual(catalog)).IsTrue();
        await CatalogIdentityAssertions.HealthyAsync(db, db.Database, db.Store, db.Partition, true, true, 2L, token);
        await CatalogIdentityAssertions.ReplayAsync(db.Database, db.Store, seed, receipt, seedOutcome, CatalogIdentityProtocol.Root, token);
        await CatalogIdentityAssertions.ReplayAsync(db.Database, db.Store, patch, current, patchOutcome, CatalogIdentityProtocol.Reader, token);
        await CatalogIdentityLifetime.ColdAsync(db, seed, receipt, seedOutcome, patch, current, patchOutcome, token);
    }

    private static async Task ConfigureAsync(TestDatabase db, PrincipalRecord expected)
    {
        var actual = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>();
        await CatalogIdentityAssertions.EqualAsync(actual, expected);
        var persisted = db.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(expected.Id)));
        await CatalogIdentityAssertions.EqualAsync(persisted, expected);
    }
}
