using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1OutcomeInspectionOperation
{
    private const int NoFailures = 0;
    private const int FirstFailureIndex = 0;

    internal static C1OutcomeInspectionReceipt Run(C1OutcomeInspectionRequest request,
        C1OutcomeInspectionFailureEvidence? evidence = null)
    {
        const int FormatVersionInitialValue = 0;
        const int PositionInitialValue = 0;

        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        Guid nodeId = default;
        Guid incarnation = default;
        var formatVersion = FormatVersionInitialValue;
        long position = PositionInitialValue;
        var outcomePresent = false;
        ServerFailureObserver.Observe(() =>
        {
            evidence?.SetPhase(C1OutcomeInspectionFailurePhase.OpenStore);
            store = ZoneTreeExistingStore.Open(new(request.Directory)
            { Incarnation = request.Incarnation }, request.ExpectedNodeId, CrashExecutionOptions.StorageExecution());
            evidence?.SetPhase(C1OutcomeInspectionFailurePhase.ReadOutcome);
            nodeId = store.Identity.NodeId;
            incarnation = store.Identity.Incarnation;
            formatVersion = store.Identity.FormatVersion;
            position = store.Position;
            outcomePresent = store.Read(view => C1OutcomeInspectionScopedOutcome.Exists(view, request.Partition,
                request.PrincipalId, request.CommandId));
        }, failures);
        CaptureFirstFailure(evidence, failures);
        evidence?.SetPhase(C1OutcomeInspectionFailurePhase.DisposeStore);
        ServerFailureObserver.Observe(() => store?.Dispose(), failures);
        CaptureFirstFailure(evidence, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return new(C1OutcomeInspectionProtocol.Version, nodeId, incarnation, formatVersion, position, outcomePresent);
    }
    private static void CaptureFirstFailure(C1OutcomeInspectionFailureEvidence? evidence, List<Exception> failures)
    {
        if (failures.Count > NoFailures)
        { evidence?.Capture(failures[FirstFailureIndex]); }
    }
}
