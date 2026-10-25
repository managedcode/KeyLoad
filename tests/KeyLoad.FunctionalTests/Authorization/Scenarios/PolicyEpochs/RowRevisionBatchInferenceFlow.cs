using KeyLoad.Server;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using P = KeyLoad.FunctionalTests.Authorization.RowRevisionInferenceProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class RowRevisionBatchInferenceFlow
{
    internal static async Task RunAsync(string kind, CancellationToken token)
    {
        var failures = new List<Exception>();
        TestDatabase? database = null;
        ServerFailureObserver.Observe(() => database = new(), failures);
        if (database is not null)
        {
            var lifetime = new RowRevisionInferenceLifetime(database);
            await ServerFailureObserver.ObserveAsync(() => RunCoreAsync(database, kind, lifetime, token), failures);
            lifetime.Cleanup(failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunCoreAsync(TestDatabase db, string kind, RowRevisionInferenceLifetime lifetime, CancellationToken token)
    {
        Seed(db);
        var originals = new List<(Command<DatabaseCommand> Operation, Result<object> Result, byte[] Outcome)>();
        foreach (var revision in new[] { P.WrongRevision, P.OriginalRevision, P.RepairRevision })
        {
            var command = Command(db, kind, revision);
            originals.Add(await RowRevisionInferenceAssertions.RefuseAsync(db.Database, db.Store, command,
                ErrorCode.PermissionDenied, P.RowDenied, token));
            await RowRevisionInferenceAssertions.DocumentAsync(db, db.Database, db.Partition, P.Own,
                P.OriginalRevision, P.Original, token);
            await RowRevisionInferenceAssertions.DocumentAsync(db, db.Database, db.Partition, P.Hidden,
                P.OriginalRevision, P.Original, token);
        }
        db.Commit(new PutDocument(P.Collection, P.Hidden, P.Original, P.OriginalRevision,
            new(OwnerId: P.Reader), ExplicitReplacement: true));
        var healthy = Command(db, kind, P.RepairRevision);
        var operation = RowRevisionInferenceRequests.Operation(db.Database, healthy);
        var expectedToken = db.NextCommitToken();
        var previousStorePosition = db.Store.Position;
        var actual = db.Database.ApplyEmbedded(operation, token);
        await RowRevisionBatchInferenceAssertions.HealthyAsync(db, db.Database, db.Store, db.Partition,
            healthy, actual, expectedToken, previousStorePosition, kind, token);
        var outcome = db.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(db.Partition, P.Reader, healthy.CommandId)))!;
        originals.Add((operation, actual, outcome));
        foreach (var item in originals)
        {
            await RowRevisionInferenceAssertions.ReplayAsync(db.Database, db.Store, item.Operation,
                item.Result, item.Outcome, db.Partition, token);
        }
        await RowRevisionBatchInferenceCold.RunAsync(db, originals, kind, lifetime, token);
    }

    private static CommandRequest Command(TestDatabase db, string kind, long revision)
    {
        Mutation own = new PutDocument(P.Collection, P.Own, P.Changed, P.OriginalRevision,
            new(OwnerId: P.Reader), ExplicitReplacement: true);
        var hidden = RowRevisionInferenceRequests.Create(db.Partition, kind, P.Hidden, revision).Mutations;
        return new(Guid.NewGuid(), db.Partition, [own, .. hidden]);
    }

    private static void Seed(TestDatabase db)
    {
        db.Configure(P.Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(P.Collection, P.Hidden, P.Original, Access: new(OwnerId: P.Foreign)),
            new PutDocument(P.Collection, P.Own, P.Original, Access: new(OwnerId: P.Reader)));
        PrincipalRecord principal = new(P.Reader, P.Tenant,
            [new(P.Database, P.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [])
        {
            OwnerId = P.Reader,
            RestrictRows = true,
            PolicyEpoch = P.PolicyEpoch
        };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
    }
}
