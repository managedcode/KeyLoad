using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedQueryAuthorityRun
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, bool useMcp, CancellationToken token)
    {
        var failures = new List<Exception>();
        DistributedQueryAuthorityState? state = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var callers = new DistributedSearchRf3Callers(wave);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token);
                var source = callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
                var destination = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
                state = new(await RemotePartitionQueryRf3Seed.CreateAsync(wave, source, destination, true, token));
                await state.Seed.GrantAsync(destination, token);
                var context = await DistributedQueryAuthoritySetup.ContextAsync(wave, callers, state, token);
                await DistributedQueryAuthorityFlows.WarmAsync(wave, context, state, useMcp, token);
            }, failures);
        }, failures);
        if (state is not null)
        {
            await DistributedQueryAuthorityCold.RunAsync(wave, state, failures, token);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
