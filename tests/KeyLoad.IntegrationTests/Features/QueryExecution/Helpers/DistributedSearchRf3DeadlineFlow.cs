using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class DistributedSearchRf3DeadlineFlow
{
    private const int FirstVoter = 0;
    private const int NoFailures = 0;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, KeyLoadClient reader,
        McpOfficialClient official, DistributedSearchRf3Seed seed, bool useMcp, bool viaSql, CancellationToken token)
    {
        var failures = new List<Exception>();
        var discovery = new ReplicaSiloDiscovery[TwoRf3MembershipProtocol.MembersPerGroup];
        var call = new DistributedSearchRf3DeadlineCall();
        DistributedSearchRf3DeadlineProbe? probe = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            for (var index = FirstVoter; index < discovery.Length; index++)
            {
                discovery[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application,
                    RequestCqrsRf3Protocol.NodeName(index), wave.Profile, token).ConfigureAwait(false);
            }
            probe = new(wave.QueryControls, discovery);
            probe.Arm(seed.Original.Destination.Principal.Id);
            call.Start(reader, official, useMcp, viaSql, token);
            await probe.ObserveNaturalDeadlineAsync(token).ConfigureAwait(false);
            await call.RequireOriginalAsync(probe.ParentRequest).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        using var cleanup = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
        if (failures.Count != NoFailures && discovery.All(value => value is not null))
        {
            await ServerFailureObserver.ObserveAsync(() => wave.QueryControls.ReleaseOpenArmsAsync(discovery,
                cleanup.Token), failures).ConfigureAwait(false);
        }
        await call.JoinAsync(failures).ConfigureAwait(false);
        if (probe is { ParentRequest: var id } owned && id != Guid.Empty)
        { await ServerFailureObserver.ObserveAsync(() => owned.ParentDisposedAsync(cleanup.Token), failures).ConfigureAwait(false); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
