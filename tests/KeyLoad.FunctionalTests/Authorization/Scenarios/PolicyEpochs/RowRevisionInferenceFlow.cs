using KeyLoad.Server;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using P = KeyLoad.FunctionalTests.Authorization.RowRevisionInferenceProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class RowRevisionInferenceFlow
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
        db.Configure(P.Collection, ResourceKind.Collection);
        db.Commit(new PutDocument(P.Collection, P.Hidden, P.Original, Access: new(OwnerId: P.Foreign)),
            new PutDocument(P.Collection, P.Own, P.Original, Access: new(OwnerId: P.Reader)),
            new PutDocument(P.Collection, P.Tombstone, P.Original, Access: new(OwnerId: P.Reader)));
        db.Commit(new DeleteDocument(P.Collection, P.Tombstone, P.OriginalRevision));
        PrincipalRecord principal = new(P.Reader, P.Tenant,
            [new(P.Database, P.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)], [])
        {
            OwnerId = P.Reader,
            RestrictRows = true,
            PolicyEpoch = P.PolicyEpoch
        };
        var configured = db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        await Assert.That(JsonDefaults.Serialize(configured).AsSpan().SequenceEqual(JsonDefaults.Serialize(principal))).IsTrue();
        var failures = new List<(Command<DatabaseCommand> Operation, Result<object> Result, byte[] Outcome)>();
        foreach (var revision in new[] { P.WrongRevision, P.OriginalRevision, P.RepairRevision })
        {
            var request = RowRevisionInferenceRequests.Create(db.Partition, kind, P.Hidden, revision);
            failures.Add(await RowRevisionInferenceAssertions.RefuseAsync(db.Database, db.Store, request,
                ErrorCode.PermissionDenied, P.RowDenied, token));
            await RowRevisionInferenceAssertions.DocumentAsync(db, db.Database, db.Partition, P.Hidden,
                P.OriginalRevision, P.Original, token);
            await Assert.That(db.Database.GetDocument(P.Reader, new(db.Partition, P.Collection, P.Hidden), cancellationToken: token)).IsNull();
        }
        await RowRevisionInferenceAssertions.RefuseAsync(db.Database, db.Store,
            RowRevisionInferenceRequests.Create(db.Partition, kind, P.Own, P.WrongRevision),
            ErrorCode.RevisionConflict, P.RevisionDenied, token);
        if (kind != P.Put)
        {
            foreach (var id in new[] { P.Missing, P.Tombstone })
            {
                await RowRevisionInferenceAssertions.RefuseAsync(db.Database, db.Store,
                    RowRevisionInferenceRequests.Create(db.Partition, kind, id, P.OriginalRevision),
                    ErrorCode.NotFound, P.Unavailable, token);
            }
        }
        db.Commit(new PutDocument(P.Collection, P.Hidden, P.Original, P.OriginalRevision,
            new(OwnerId: P.Reader), ExplicitReplacement: true));
        await RowRevisionInferenceAssertions.DocumentAsync(db, db.Database, db.Partition, P.Hidden,
            P.RepairRevision, P.Original, token);
        failures.Add(await RowRevisionInferenceAssertions.HealthyAsync(db, kind, token));
        foreach (var item in failures)
        {
            await RowRevisionInferenceAssertions.ReplayAsync(db.Database, db.Store, item.Operation,
                item.Result, item.Outcome, db.Partition, token);
        }
        await RowRevisionInferenceCold.RunAsync(db, failures, lifetime, token);
    }
}
