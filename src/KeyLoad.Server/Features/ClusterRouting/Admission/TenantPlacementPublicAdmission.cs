using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class TenantPlacementPublicAdmission
{
    internal static void Require(HttpContext context, GrainReadKind? read, OperationKind? command)
    {
        if (read is not (GrainReadKind.PlacementPolicy or GrainReadKind.PlacementPlan or GrainReadKind.PlacementIntent)
            && command is not (OperationKind.ConfigurePlacementPolicy or OperationKind.PersistPlacementIntent
                or OperationKind.ApplyPlacementIntent))
        { return; }
        var options = context.RequestServices.GetRequiredService<IOptions<NodeOptions>>().Value;
        var execution = context.RequestServices.GetRequiredService<IOptions<TenantPlacementExecutionOptions>>().Value;
        if (!execution.Enabled || options.PhysicalGroupTrust.Role != PhysicalGroupTrustRole.Control
            || !options.PhysicalGroupTrust.RegisterPhysicalOwners || !options.PhysicalGroupTrust.RemoteDocumentReads)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability,
            KeyLoad.Core.Features.ClusterRouting.Contracts.TenantPlacementCapacityProtocol.Unavailable);
        }
    }
}
