using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server;

internal static class CanonicalCredentialDispatch
{
    /// <summary>Authenticates a subject and reads current authority for adapters that consume permission metadata.</summary>
    internal static async Task<PrincipalRecord> AuthenticateAsync(OrleansNode node, string credential,
        ServerConnectionOperation connection, Guid requestId, Action? onStarted = null)
        => await ReadCurrentPrincipalAsync(node,
            await AuthenticateSubjectAsync(node, credential, connection, requestId, onStarted), connection);

    /// <summary>Authenticates a subject; the database operation reloads and enforces current persisted permissions.</summary>
    internal static async Task<PrincipalRecord> AuthenticateSubjectAsync(OrleansNode node, string credential,
        ServerConnectionOperation connection, Guid requestId, Action? onStarted = null)
    {
        var grains = node.DatabaseReady ? node.Grains!
            : throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        var user = grains.GetGrain<IUserGrain>(UserGrainIdentity.For(AuthorizationIdentity.ReadCredentialId(credential)));
        var identity = node.OpenRequestContext(null, requestId, Guid.Empty, connection.Token, connection.Id);
        PrincipalRecord result;
        try
        {
            onStarted?.Invoke();
            try
            {
                var verified = await user.AuthenticateAsync(credential, connection.Token);
                result = new(verified.Id, verified.TenantId, [], []);
            }
            catch (Exception failure) when (failure is OrleansException or TimeoutException
                && CqrsBoundaryErrors.IsNonFatal(failure))
            {
                throw OrleansRpcFailure.Translate(failure, false, requestId,
                    node.RuntimeServices.GetRequiredService<ILogger<OrleansNode>>(),
                    OrleansRpcFailureStage.Consumption,
                    new(Guid.Empty, GrainReadKind.Authenticate, null), connection.Token);
            }
        }
        catch (Exception failure)
        {
            if (RequestWorkSettlement.Observe(identity.Dispose).Value is { } cleanup)
            {
                throw new AggregateException(failure, cleanup);
            }
            throw;
        }
        identity.Dispose();
        return result;
    }

    private static async Task<PrincipalRecord> ReadCurrentPrincipalAsync(OrleansNode node,
        PrincipalRecord verified, ServerConnectionOperation connection)
    {
        var requestId = Guid.NewGuid();
        var envelope = node.CatalogRequestCodec().CreateRead(requestId, verified.Id,
            GrainReadKind.CurrentPrincipal, GrainContracts.NoDtoMarker);
        using var identity = node.OpenRequestContext(verified, requestId, Guid.Empty, connection.Token, connection.Id);
        var reply = await node.ExecuteAsync(requestId, envelope, false, connection.Token);
        return ((GrainReplyFactory.RequireValue(reply).Value /* KLD0041: Read the current generated GrainValue principal reply slot before checking its concrete type and subject identity. */) is PrincipalRecord current /* KLD0042: Require the concrete PrincipalRecord and match the verified subject before returning the current principal. */) && current.Id == verified.Id
            ? current : throw Errors.Fail(ErrorCode.Corruption, McpAuthentication.InvalidReply);
    }
}
