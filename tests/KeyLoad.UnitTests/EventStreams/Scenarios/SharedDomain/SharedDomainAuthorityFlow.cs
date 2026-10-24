using static KeyLoad.RecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.EventStreams;

internal static class SharedDomainAuthorityFlow
{
    internal static async Task RunAsync(DatabaseEngine database, SharedDomainState state, CancellationToken token)
    {
        state.PolicyEpoch += FirstRevision;
        state.Denied = true;
        SharedDomainOperations.ConfigurePrincipal(database, state, denied: true, token);
        await SharedDomainHistoryOperations.AllAsync(database, database.Store, state, token);
        await SharedDomainModelAssertions.AllAsync(database, state, token);
        state.PolicyEpoch += FirstRevision;
        state.Denied = false;
        SharedDomainOperations.ConfigurePrincipal(database, state, denied: false, token);
        await SharedDomainHistoryOperations.AllAsync(database, database.Store, state, token);
        await SharedDomainProducerFlow.RunAsync(database, state, healthy: true, token);
        await SharedDomainHistoryOperations.AllAsync(database, database.Store, state, token);
    }
}
