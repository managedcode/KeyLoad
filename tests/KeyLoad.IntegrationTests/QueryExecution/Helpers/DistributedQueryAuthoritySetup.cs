using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedQueryAuthoritySetup
{
    internal static async Task<DistributedQueryAuthorityContext> ContextAsync(TwoRf3MembershipWave wave,
        DistributedSearchRf3Callers callers, DistributedQueryAuthorityState state, CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token);
        return new(callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey),
            callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey),
            callers.Sdk(TwoRf3MembershipProtocol.Node2, state.Seed.Destination.Secret),
            await callers.OfficialAsync(TwoRf3MembershipProtocol.Node3, state.Seed.Destination.Secret, token),
            await callers.OfficialAsync(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey, token),
            await callers.OfficialAsync(TwoRf3MembershipProtocol.Node5, wave.Profile.AdminKey, token));
    }
}
