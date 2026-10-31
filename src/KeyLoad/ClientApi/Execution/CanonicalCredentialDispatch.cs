using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server;

internal static class CanonicalCredentialDispatch
{
    /// <summary>Authenticates through the credential's user grain, which reads storage only on its first request.</summary>
    internal static async Task<PrincipalRecord> AuthenticateAsync(OrleansNode node, string credential,
        ServerConnectionOperation connection, Guid requestId, Action? onStarted = null)
    {
        var grains = node.DatabaseReady ? node.Grains!
            : throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        var user = grains.GetGrain<IUserGrain>(UserGrainIdentity.For(
            AuthorizationIdentity.IssueCredentialWitness(credential).CredentialId));
        PrincipalRecord? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = node.OpenRequestContext(null, requestId, Guid.Empty, connection.Token, connection.Id);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                onStarted?.Invoke();
                result = await user.AuthenticateAsync(credential, connection.Token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, PostgresProtocol.Invalid);
    }
}
