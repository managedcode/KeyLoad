using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

internal static class OrleansNodeRuntimeAccess
{
    internal static KeyLoad.Query.QueryEngine BorrowPartitionQueries(this OrleansNode node) => node.RuntimeServices.GetRequiredService<KeyLoad.Query.QueryEngine>();

    internal static GrainRequestCodec CatalogRequestCodec(this OrleansNode node) => node.RuntimeServices.GetRequiredService<GrainRequestCodec>();

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
