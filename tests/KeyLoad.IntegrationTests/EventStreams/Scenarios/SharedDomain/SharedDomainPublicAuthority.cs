using KeyLoad.IntegrationTests.Features.Messaging;
using static KeyLoad.IntegrationTests.Features.EventStreams.SharedDomainPublicProtocol;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class SharedDomainPublicAuthority
{
    internal static async Task RunAsync(ClusterFixture fixture, SharedDomainPublicState state, CancellationToken token)
    {
        var original = state.Identity.Principal;
        var revoked = original with
        {
            Grants = [],
            PolicyEpoch = RevokedEpoch
        };
        await MessagingClusterIdentity.UpdateAsync(fixture, revoked, token);
        state.Identity = state.Identity with
        {
            Principal = revoked
        };
        state.Fenced = true;
        await SharedDomainPublicProducer.ReplayAllAsync(fixture, state, token);
        await SharedDomainPublicModels.AllAsync(fixture, state, token);
        var restored = original with
        {
            PolicyEpoch = RestoredEpoch
        };
        await MessagingClusterIdentity.UpdateAsync(fixture, restored, token);
        state.Identity = state.Identity with
        {
            Principal = restored
        };
        await SharedDomainPublicProducer.ReplayAllAsync(fixture, state, token);
        await SharedDomainPublicSession.RunAsync(fixture, state.Identity.Secret,
            (sdk, official) => SharedDomainPublicProcessing.ReplayAsync(sdk, official, state, token), token);
        await SharedDomainPublicModels.AllAsync(fixture, state, token);
        await SharedDomainPublicProducer.WriteAsync(fixture, state, true, token);
        await SharedDomainPublicModels.AllAsync(fixture, state, token);
    }
}
