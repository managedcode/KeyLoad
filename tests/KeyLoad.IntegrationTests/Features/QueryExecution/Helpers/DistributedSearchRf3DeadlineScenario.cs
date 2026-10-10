using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class DistributedSearchRf3DeadlineScenario(TwoRf3MembershipWave wave,
    DistributedSearchRf3Callers callers, DistributedSearchRf3Seed seed, bool useMcp)
{
    private KeyLoadClient? source;
    private KeyLoadClient? destination;
    private KeyLoadClient? reader;
    private McpOfficialClient? official;

    internal async Task RunAsync(CancellationToken token)
    {
        await BarrierAsync(token).ConfigureAwait(false);
        await callers.DisposeAsync().ConfigureAwait(false);
        var before = await DistributedSearchRf3RawCut.StopAndReadAsync(wave, token).ConfigureAwait(false);
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await OpenAsync(token).ConfigureAwait(false);
        await OneAsync(viaSql: false, token).ConfigureAwait(false);
        await OneAsync(viaSql: true, token).ConfigureAwait(false);
        await BarrierAsync(token).ConfigureAwait(false);
        await callers.DisposeAsync().ConfigureAwait(false);
        var after = await DistributedSearchRf3RawCut.StopAndReadAsync(wave, token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(before, after);
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await OpenAsync(token).ConfigureAwait(false);
        await HealthyAsync(token).ConfigureAwait(false);
    }

    private async Task OpenAsync(CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token).ConfigureAwait(false);
        source = callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
        destination = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
        reader = callers.Sdk(TwoRf3MembershipProtocol.Node2, seed.Original.Destination.Secret);
        official = await callers.OfficialAsync(TwoRf3MembershipProtocol.Node3, seed.Original.Destination.Secret, token).ConfigureAwait(false);
    }

    private async Task OneAsync(bool viaSql, CancellationToken token)
    {
        await DistributedSearchRf3DeadlineFlow.RunAsync(wave, reader!, official!, seed, useMcp, viaSql, token).ConfigureAwait(false);
        await HealthyAsync(token).ConfigureAwait(false);
    }

    private async Task HealthyAsync(CancellationToken token)
    {
        await DistributedSearchRf3Documents.RequireAsync(source!, destination!, token).ConfigureAwait(false);
        await DistributedSearchRf3PublicFlow.HealthyAsync(source!, destination!, reader!, official!,
            seed, DistributedSearchRf3Seed.InitialEpoch, token).ConfigureAwait(false);
    }

    private async Task BarrierAsync(CancellationToken token)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var client = callers.Sdk(node, wave.Profile.AdminKey);
            _ = await McpCallerAssertions.SdkSuccessAsync(await client.StatusAsync(token)).ConfigureAwait(false);
        }
    }
}
