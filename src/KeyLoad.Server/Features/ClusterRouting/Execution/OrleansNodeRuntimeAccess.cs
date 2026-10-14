using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

internal static class OrleansNodeRuntimeAccess
{
    /// <summary>Checks all configured voters within the caller's deadline before routing or readiness.</summary>
    /// <param name="node">Original running native silo and borrowed service owner.</param>
    /// <param name="cancellationToken">Bounds the actual discovery attempts and their cleanup.</param>
    internal static Task EnsureCompatibleCohortAsync(this OrleansNode node, CancellationToken cancellationToken)
        => node.RuntimeServices.GetRequiredService<ReplicaSiloDiscoveryClient>().EnsureCompatibleCohortAsync(cancellationToken);

    internal static KeyLoad.Query.QueryEngine BorrowPartitionQueries(this OrleansNode node) => node.RuntimeServices.GetRequiredService<KeyLoad.Query.QueryEngine>();

    internal static GrainRequestCodec CatalogRequestCodec(this OrleansNode node) => node.RuntimeServices.GetRequiredService<GrainRequestCodec>();

    internal static global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeClient BorrowRemoteModelClient(this OrleansNode node)
        => node.RuntimeServices.GetRequiredService<global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeClient>();

    internal static global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeRuntime BorrowRemoteModelRuntime(this OrleansNode node)
        => node.RuntimeServices.GetRequiredService<global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeRuntime>();

    internal static PartitionMovementGrainServiceClient BorrowPartitionMovementTransport(this OrleansNode node)
        => node.RuntimeServices.GetRequiredService<PartitionMovementGrainServiceClient>();

    internal static PhysicalOwnerProbeNativeClient BorrowPhysicalOwnerProbeTransport(this OrleansNode node)
        => node.RuntimeServices.GetRequiredService<PhysicalOwnerProbeNativeClient>();

    internal static System.Collections.Immutable.ImmutableArray<ReplicaNativeMembershipEntry> ReadActiveNativeMembership(this OrleansNode node)
        => node.RuntimeServices.GetRequiredService<ReplicaNativeDiscoveryRuntime>().ReadActiveMembership();

    /// <summary>Admits and scopes persisted identity with this running silo's native serializers.</summary>
    /// <param name="node">Original running native silo and borrowed service owner.</param>
    /// <param name="principal">The persisted subject, absent only for credential authentication.</param>
    /// <param name="requestId">The fresh request identity.</param>
    /// <param name="commandId">The stable command identity, empty for reads.</param>
    /// <param name="cancellationToken">Cancels validation and actual native encoding.</param>
    /// <param name="connectionId">Actual accepted transport owner, or the registered service owner when absent.</param>
    internal static GrainRequestIdentityScope OpenRequestContext(this OrleansNode node, PrincipalRecord? principal, Guid requestId, Guid commandId,
        CancellationToken cancellationToken, Guid? connectionId = null)
        => new(node.RuntimeServices, principal, requestId, commandId, cancellationToken,
            connectionId: connectionId ?? NativeConnectionExecutionIdentity.Resolve(node.RuntimeServices));
}
