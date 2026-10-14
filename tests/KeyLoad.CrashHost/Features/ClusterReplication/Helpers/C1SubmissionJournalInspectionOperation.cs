using System.Security.Cryptography;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterReplication;

internal static class C1SubmissionJournalInspectionOperation
{
    private const int NoFailures = 0;
    private const int FirstFailureIndex = 0;
    internal static C1SubmissionJournalInspectionReceipt Run(C1SubmissionJournalInspectionRequest request,
        C1OutcomeInspectionFailureEvidence? evidence = null, CancellationToken cancellationToken = default)
    {
        _ = C1SubmissionJournalInspectionValidation.Request(request);
        cancellationToken.ThrowIfCancellationRequested();
        var budget = new C1SubmissionJournalInspectionReadBudget(cancellationToken);
        var failures = new List<Exception>();
        ZoneTreeStore? canonical = null;
        ZoneTreeStore? replica = null;
        C1SubmissionJournalInspectionReceipt? receipt = null;
        ServerFailureObserver.Observe(() =>
        {
            evidence?.SetPhase(C1OutcomeInspectionFailurePhase.OpenStore);
            C1SubmissionJournalInspectionPaths.RequireOwnedScope(request);
            var openedCanonical = ZoneTreeExistingStore.Open(
                new(C1SubmissionJournalInspectionPaths.Canonical(request))
                {
                    Incarnation = request.Incarnation
                },
                request.ExpectedCanonicalNodeId, CrashExecutionOptions.StorageExecution());
            canonical = openedCanonical;
            var replicaOptions = new ZoneTreeStoreOptions(C1SubmissionJournalInspectionPaths.Replica(request))
            {
                Incarnation = request.Incarnation
            };
            var policy = CrashExecutionOptions.StorageExecution();
            var resolved = replicaOptions.ResolveExecutionOptions(policy);
            var identity = ZoneTreeIdentityFile.Read(C1SubmissionJournalInspectionPaths.Identity(resolved.Directory),
                resolved.MaximumIdentityFileBytes, resolved.StreamBufferBytes);
            var openedReplica = ZoneTreeExistingStore.Open(resolved, identity.NodeId, policy);
            replica = openedReplica;
            RequireIdentities(openedCanonical, openedReplica, request);
            cancellationToken.ThrowIfCancellationRequested();
            evidence?.SetPhase(C1OutcomeInspectionFailurePhase.ReadOutcome);
            using var inspection = C1SubmissionJournalInspectionDatabase.Open(openedCanonical, budget, cancellationToken);
            var database = inspection.Database;
            var configuration = CrashExecutionOptions.Configuration(request.LocalVoter, request.Voters,
                resolved.Directory, request.Incarnation).Value;
            var cut = NativeSubmissionJournalRead.Run(openedReplica, database, configuration,
                request.EntryIndex, request.EntryTerm, cancellationToken, budget.Charge);
            receipt = new(C1SubmissionJournalInspectionProtocol.Version, openedCanonical.Identity.NodeId,
                cut.NodeId, cut.Incarnation, openedCanonical.Position, cut.Position,
                cut.NativeHardState, cut.NativeEntry);
        }, failures);
        if (failures.Count > NoFailures)
        {
            evidence?.Capture(failures[FirstFailureIndex]);
        }
        evidence?.SetPhase(C1OutcomeInspectionFailurePhase.DisposeStore);
        ServerFailureObserver.Observe(() => replica?.Dispose(), failures);
        ServerFailureObserver.Observe(() => canonical?.Dispose(), failures);
        if (failures.Count > NoFailures)
        {
            evidence?.Capture(failures[FirstFailureIndex]);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return receipt ?? throw C1SubmissionJournalInspectionValidation.Invalid();
    }
    private static void RequireIdentities(ZoneTreeStore canonical, ZoneTreeStore replica,
        C1SubmissionJournalInspectionRequest request)
    {
        if (canonical.Identity.Incarnation != request.Incarnation || replica.Identity.Incarnation != request.Incarnation
            || canonical.Identity.NodeId == replica.Identity.NodeId
            || !CryptographicOperations.FixedTimeEquals(canonical.Identity.SigningKey.Span, replica.Identity.SigningKey.Span))
        {
            throw C1SubmissionJournalInspectionValidation.Invalid();
        }
    }
}
