using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class RemotePartitionQueryRf3Scenario : IAsyncDisposable
{
    private readonly List<HttpClient> connections = [];
    private readonly List<McpOfficialClient> sessions = [];
    private readonly TwoRf3MembershipWave wave;
    private readonly bool useMcp;
    internal RemotePartitionQueryRf3Scenario(TwoRf3MembershipWave wave, bool useMcp)
    { this.wave = wave; this.useMcp = useMcp; }

    private KeyLoadClient Sdk(string node, string secret)
    {
        var http = McpCallerHttp.Create(wave.Application, node);
        connections.Add(http);
        return new(http, secret, IntegrationClientOptions.Execution());
    }

    private async Task<McpOfficialClient> OfficialAsync(string node, string secret, CancellationToken token)
    {
        var owner = await McpOfficialClient.ConnectAsync(wave.Application, node, secret, token).ConfigureAwait(false);
        sessions.Add(owner);
        return owner;
    }

    internal async Task RunAsync(CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token).ConfigureAwait(false);
        var source = Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
        var destination = Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
        var seed = await RemotePartitionQueryRf3Seed.CreateAsync(wave, source, destination, probe: true, token).ConfigureAwait(false);
        await RemoteDocumentRf3Assertions.ReceiptAsync(seed.Destination).ConfigureAwait(false);
        var reader = Sdk(TwoRf3MembershipProtocol.Node2, seed.Destination.Secret);
        var official = await OfficialAsync(TwoRf3MembershipProtocol.Node3, seed.Destination.Secret, token).ConfigureAwait(false);
        await DenialAsync(source, destination, reader, official, token).ConfigureAwait(false);
        await seed.GrantAsync(destination, token).ConfigureAwait(false);
        var sourceOfficial = await OfficialAsync(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey, token).ConfigureAwait(false);
        await RemotePartitionQueryRf3Assertions.LocalReceiptAsync(source, sourceOfficial, seed, token).ConfigureAwait(false);
        await CancelledAsync(source, destination, reader, official, seed, token).ConfigureAwait(false);
        await HealthyAsync(source, destination, reader, official, token).ConfigureAwait(false);
        var destinationOfficial = await OfficialAsync(TwoRf3MembershipProtocol.Node5, wave.Profile.AdminKey, token).ConfigureAwait(false);
        await RemotePartitionQueryRf3Assertions.ReceiptReplayAsync(destination, destinationOfficial, seed, token).ConfigureAwait(false);
        await RestartOwnerAsync(source, destination, reader, official, seed, token).ConfigureAwait(false);
    }

    private static async Task DenialAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, CancellationToken token)
    {
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationBefore = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await RemotePartitionQueryRf3Assertions.DeniedAsync(reader, token).ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(RemotePartitionQueryRf3Seed.Tool,
            RemotePartitionQueryRf3Seed.Request(), token), ErrorCode.PermissionDenied, dispatched: true);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationAfter = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsEqualTo(sourceBefore.Applied);
        await Assert.That(destinationAfter.Applied).IsEqualTo(destinationBefore.Applied);
        await RemoteDocumentRf3Assertions.DocumentAsync(await McpCallerAssertions.SdkSuccessAsync(
            await destination.GetAsync(RemoteDocumentRf3Protocol.Reference, token)), projected: false);
    }

    private async Task CancelledAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, RemotePartitionQueryRf3Seed seed, CancellationToken token)
    {
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationBefore = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await RemotePartitionQueryRf3Cancellation.RunAsync(wave, reader, official, seed, useMcp, token).ConfigureAwait(false);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationAfter = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsEqualTo(sourceBefore.Applied);
        await Assert.That(destinationAfter.Applied).IsEqualTo(destinationBefore.Applied);
    }

    private static async Task HealthyAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, CancellationToken token)
    {
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationBefore = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        var request = RemotePartitionQueryRf3Seed.Request();
        await RemotePartitionQueryRf3Assertions.PageAsync(await McpCallerAssertions.SdkSuccessAsync(
            await reader.PartitionQueryAsync(request, token))).ConfigureAwait(false);
        await RemotePartitionQueryRf3Assertions.PageAsync((await McpCallerAssertions.SuccessAsync<PartitionQueryPageV1>(
            await official.CallAsync(RemotePartitionQueryRf3Seed.Tool, request, token))).Value).ConfigureAwait(false);
        await RemotePartitionQueryRf3Assertions.PageAsync(await SqlRf3Protocol.SdkAsync<PartitionQueryPageV1>(reader,
            SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition, RemotePartitionQueryRf3Seed.Tool, request), token)).ConfigureAwait(false);
        await RemotePartitionQueryRf3Assertions.PageAsync(await SqlRf3Protocol.McpAsync<PartitionQueryPageV1>(official,
            SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition, RemotePartitionQueryRf3Seed.Tool, request), token)).ConfigureAwait(false);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationAfter = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsEqualTo(sourceBefore.Applied);
        await Assert.That(destinationAfter.Applied).IsEqualTo(destinationBefore.Applied);
    }

    private async Task RestartOwnerAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, RemotePartitionQueryRf3Seed seed, CancellationToken token)
    {
        await wave.RemoteRuntime.KillAsync(TwoRf3MembershipProtocol.Node4,
            RemoteDocumentRf3Protocol.RestartScenario, token).ConfigureAwait(false);
        await wave.RemoteRuntime.RestartAsync(TwoRf3MembershipProtocol.Node4, token).ConfigureAwait(false);
        await HealthyAsync(source, destination, reader, official, token).ConfigureAwait(false);
        var fresh = await OfficialAsync(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey, token).ConfigureAwait(false);
        await RemotePartitionQueryRf3Assertions.ReceiptReplayAsync(destination, fresh, seed, token).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        foreach (var session in sessions)
        { await ServerFailureObserver.ObserveAsync(() => session.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        foreach (var http in connections)
        { ServerFailureObserver.Observe(http.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
