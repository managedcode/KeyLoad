using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1OutcomeInspectionOperation
{
    internal static C1OutcomeInspectionReceipt Run(C1OutcomeInspectionRequest request)
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
            store = ZoneTreeExistingStore.Open(new(request.Directory)
            { Incarnation = request.Incarnation }, request.ExpectedNodeId, CrashExecutionOptions.StorageExecution());
            nodeId = store.Identity.NodeId;
            incarnation = store.Identity.Incarnation;
            formatVersion = store.Identity.FormatVersion;
            position = store.Position;
            outcomePresent = store.Read(view => C1OutcomeInspectionScopedOutcome.Exists(view, request.Partition,
                request.PrincipalId, request.CommandId));
        }, failures);
        ServerFailureObserver.Observe(() => store?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return new(C1OutcomeInspectionProtocol.Version, nodeId, incarnation, formatVersion, position, outcomePresent);
    }
}
