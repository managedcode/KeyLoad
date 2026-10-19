using KeyLoad.Clustering;

namespace KeyLoad.Server;

internal static class OrleansNodeRuntimeAccess
{

    internal static GrainRequestCodec CatalogRequestCodec(this OrleansNode node) => node.RuntimeServices.GetRequiredService<GrainRequestCodec>();

    /// <summary>Admits and scopes persisted identity with this running silo's execution serializers.</summary>
    /// <param name="node">Original running execution silo and borrowed service owner.</param>
    /// <param name="principal">The persisted subject, absent only for credential authentication.</param>
    /// <param name="requestId">The fresh request identity.</param>
    /// <param name="commandId">The stable command identity, empty for reads.</param>
    /// <param name="cancellationToken">Cancels validation and actual execution encoding.</param>
    /// <param name="connectionId">Actual accepted transport owner, or the registered service owner when absent.</param>
    internal static GrainRequestIdentityScope OpenRequestContext(this OrleansNode node, PrincipalRecord? principal, Guid requestId, Guid commandId,
        CancellationToken cancellationToken, Guid? connectionId = null)
        => new(node.RuntimeServices, principal, requestId, commandId, cancellationToken,
            connectionId: connectionId ?? ConnectionExecutionIdentity.Resolve(node.RuntimeServices));
}
