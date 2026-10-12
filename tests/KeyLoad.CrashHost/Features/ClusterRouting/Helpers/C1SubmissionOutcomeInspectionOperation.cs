using KeyLoad.Server;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1SubmissionOutcomeInspectionOperation
{
    private const int NoFailures = 0;
    private const int FirstFailureIndex = 0;
    internal static C1SubmissionOutcomeInspectionReceipt Run(C1SubmissionOutcomeInspectionRequest request,
        C1OutcomeInspectionFailureEvidence? evidence = null)
    {
        _ = C1SubmissionOutcomeInspectionValidation.Request(request);
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        C1SubmissionOutcomeInspectionReceipt? receipt = null;
        ServerFailureObserver.Observe(() =>
        {
            evidence?.SetPhase(C1OutcomeInspectionFailurePhase.OpenStore);
            var opened = ZoneTreeExistingStore.Open(new(request.Directory) { Incarnation = request.Incarnation },
                request.ExpectedNodeId, CrashExecutionOptions.StorageExecution());
            store = opened;
            evidence?.SetPhase(C1OutcomeInspectionFailurePhase.ReadOutcome);
            receipt = opened.Read(original =>
            {
                var view = new C1SubmissionOutcomeInspectionView(original);
                var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.Partition);
                var selected = CommandOutcomeKeyResolver.Select(view, request.PrincipalId, request.CommandId, scope);
                if (selected.Outcome is null)
                { throw C1SubmissionOutcomeInspectionValidation.Invalid(); }
                var raw = view.ReadOwnedValue(selected.Key) ?? throw C1SubmissionOutcomeInspectionValidation.Invalid();
                return new C1SubmissionOutcomeInspectionReceipt(C1SubmissionOutcomeInspectionProtocol.Version,
                    opened.Identity.NodeId, opened.Identity.Incarnation, opened.Identity.FormatVersion,
                    opened.Position, selected.Key, raw);
            });
        }, failures);
        if (failures.Count > NoFailures)
        { evidence?.Capture(failures[FirstFailureIndex]); }
        evidence?.SetPhase(C1OutcomeInspectionFailurePhase.DisposeStore);
        ServerFailureObserver.Observe(() => store?.Dispose(), failures);
        if (failures.Count > NoFailures)
        { evidence?.Capture(failures[FirstFailureIndex]); }
        ServerFailureObserver.ThrowIfAny(failures);
        return receipt ?? throw C1SubmissionOutcomeInspectionValidation.Invalid();
    }
}
