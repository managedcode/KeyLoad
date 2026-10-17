using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class DistributedSearchRf3Scenario(TwoRf3MembershipWave wave, bool useMcp) : IAsyncDisposable
{
    private readonly DistributedSearchRf3Callers callers = new(wave);

    internal async Task RunAsync(CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token).ConfigureAwait(false);
        var source = callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
        var destination = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
        var seed = await DistributedSearchRf3Seed.CreateAsync(wave, source, destination, token).ConfigureAwait(false);
        var reader = callers.Sdk(TwoRf3MembershipProtocol.Node2, seed.Original.Destination.Secret);
        var official = await callers.OfficialAsync(TwoRf3MembershipProtocol.Node3, seed.Original.Destination.Secret, token).ConfigureAwait(false);
        await DistributedSearchRf3PublicFlow.DeniedAsync(source, destination, reader, official, token).ConfigureAwait(false);
        await DistributedSearchRf3Documents.RequireAsync(source, destination, token).ConfigureAwait(false);
        await DistributedSearchRf3Seed.ConfigureAsync(destination, seed.Original.Destination.Principal,
            DistributedSearchRf3Seed.InitialEpoch, token).ConfigureAwait(false);
        await DistributedSearchRf3PublicFlow.HealthyAsync(source, destination, reader, official,
            seed, DistributedSearchRf3Seed.InitialEpoch, token).ConfigureAwait(false);
        await DistributedSearchRf3PublicFlow.EmptyAsync(source, destination, reader, official,
            seed, DistributedSearchRf3Seed.InitialEpoch, token).ConfigureAwait(false);
        await DistributedSearchRf3Refusal.RunAsync(source, destination, reader, official, token).ConfigureAwait(false);
        await DistributedSearchRf3PublicFlow.HealthyAsync(source, destination, reader, official,
            seed, DistributedSearchRf3Seed.InitialEpoch, token).ConfigureAwait(false);
        await CancelledAsync(source, destination, reader, official, seed, token).ConfigureAwait(false);
        await DistributedSearchRf3PublicFlow.HealthyAsync(source, destination, reader, official,
            seed, DistributedSearchRf3Seed.InitialEpoch, token).ConfigureAwait(false);
        await new DistributedSearchRf3DeadlineScenario(wave, callers, seed, useMcp).RunAsync(token).ConfigureAwait(false);
        source = callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
        destination = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
        reader = callers.Sdk(TwoRf3MembershipProtocol.Node2, seed.Original.Destination.Secret);
        official = await callers.OfficialAsync(TwoRf3MembershipProtocol.Node3, seed.Original.Destination.Secret, token).ConfigureAwait(false);
        await DistributedSearchRf3EpochTrial.RunAsync(wave, source, destination, reader, official, seed, useMcp, token).ConfigureAwait(false);
        await ReplayAsync(source, destination, seed, token).ConfigureAwait(false);
        await callers.DisposeAsync().ConfigureAwait(false);
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await ColdAsync(seed, token).ConfigureAwait(false);
    }

    private async Task CancelledAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, DistributedSearchRf3Seed seed, CancellationToken token)
    {
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationBefore = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await DistributedSearchRf3Cancellation.RunAsync(wave, reader, official, seed, useMcp, token).ConfigureAwait(false);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationAfter = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsEqualTo(sourceBefore.Applied);
        await Assert.That(destinationAfter.Applied).IsEqualTo(destinationBefore.Applied);
        await DistributedSearchRf3Documents.RequireAsync(source, destination, token).ConfigureAwait(false);
    }

    private async Task ReplayAsync(KeyLoadClient source, KeyLoadClient destination,
        DistributedSearchRf3Seed seed, CancellationToken token)
    {
        var sourceOfficial = await callers.OfficialAsync(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey, token).ConfigureAwait(false);
        var destinationOfficial = await callers.OfficialAsync(TwoRf3MembershipProtocol.Node5, wave.Profile.AdminKey, token).ConfigureAwait(false);
        await DistributedSearchRf3Replay.AllAsync(source, destination, sourceOfficial, destinationOfficial, seed, token).ConfigureAwait(false);
        await DistributedSearchRf3Documents.RequireAsync(source, destination, token).ConfigureAwait(false);
    }

    private async Task ColdAsync(DistributedSearchRf3Seed seed, CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token).ConfigureAwait(false);
        var source = callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
        var destination = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
        var reader = callers.Sdk(TwoRf3MembershipProtocol.Node2, seed.Original.Destination.Secret);
        var official = await callers.OfficialAsync(TwoRf3MembershipProtocol.Node3, seed.Original.Destination.Secret, token).ConfigureAwait(false);
        await DistributedSearchRf3PublicFlow.HealthyAsync(source, destination, reader, official,
            seed, DistributedSearchRf3EpochTrial.HealthyEpoch, token).ConfigureAwait(false);
        await ReplayAsync(source, destination, seed, token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await callers.DisposeAsync().ConfigureAwait(false);
}
