using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class C1OutcomeInspectionOperation
{
    internal static C1OutcomeInspectionReceipt Run(C1OutcomeInspectionRequest request)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        Guid nodeId = default;
        Guid incarnation = default;
        var formatVersion = 0;
        long position = 0;
        var outcomePresent = false;
        ServerFailureObserver.Observe(() =>
        {
            store = ZoneTreeExistingStore.Open(new(request.Directory)
            { Incarnation = request.Incarnation }, request.ExpectedNodeId);
            nodeId = store.Identity.NodeId;
            incarnation = store.Identity.Incarnation;
            formatVersion = store.Identity.FormatVersion;
            position = store.Position;
            outcomePresent = new DatabaseEngine(store, new AuthorizationPolicy())
                .Outcome(request.PrincipalId, request.CommandId) is not null;
        }, failures);
        ServerFailureObserver.Observe(() => store?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return new(C1OutcomeInspectionProtocol.Version, nodeId, incarnation, formatVersion, position, outcomePresent);
    }
}
