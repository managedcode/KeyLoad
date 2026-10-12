using KeyLoad.Core;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphParentRouting
{
    internal static PrincipalRecord Principal(PartitionHost partition, TimeProvider clock,
        GrainRequestEnvelope trusted, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(trusted);
        work.Check();
        if (trusted.Purpose != RemoteGraphRequestScope.ParentPurpose || string.IsNullOrEmpty(trusted.PrincipalId))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof); }
        return partition.Database.Store.Read(view => partition.Database.Principal(work.CreateView(view),
            trusted.PrincipalId, clock.GetUtcNow()));
    }

    internal static RemoteGraphParentRouteV1 Resolve(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, PartitionRef source, EntityRef? destination, string graph, ReadExecutionBudget work)
        => partition.Database.ReadRemoteGraphParentRoute(principal.Id, source, destination, graph,
            Control(node, partition), work);

    internal static RemoteGraphIncomingRouteV1 Incoming(OrleansNode node, PartitionHost partition,
        PrincipalRecord principal, EntityRef target, string graph, ReadExecutionBudget work)
        => partition.Database.ReadRemoteGraphIncomingRoute(principal.Id, target, graph, Control(node, partition), work);

    private static PhysicalShardRecord Control(OrleansNode node, PartitionHost partition)
    {
        var options = node.RuntimeServices.GetRequiredService<IOptions<NodeOptions>>().Value;
        if (options.PhysicalGroupTrust.Role != PhysicalGroupTrustRole.Control)
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphNativeProtocol.Unavailable); }
        return PhysicalOwnerConfiguredTuples.Control(options, partition).Owner;
    }
}
