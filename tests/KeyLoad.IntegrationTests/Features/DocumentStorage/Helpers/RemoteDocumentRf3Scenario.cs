using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal sealed class RemoteDocumentRf3Scenario : IAsyncDisposable
{
    private readonly List<HttpClient> connections = [];
    private readonly List<McpOfficialClient> sessions = [];
    private readonly TwoRf3MembershipWave wave;
    internal RemoteDocumentRf3Scenario(TwoRf3MembershipWave wave) => this.wave = wave;

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
        var seed = await RemoteDocumentRf3Seed.CreateAsync(wave, source, destination, probe: false, token).ConfigureAwait(false);
        await RemoteDocumentRf3Assertions.ReceiptAsync(seed).ConfigureAwait(false);
        var reader = Sdk(TwoRf3MembershipProtocol.Node2, seed.Secret);
        var official = await OfficialAsync(TwoRf3MembershipProtocol.Node3, seed.Secret, token).ConfigureAwait(false);
        await DenialAsync(source, destination, reader, official, token).ConfigureAwait(false);
        await seed.GrantDestinationAsync(destination, token).ConfigureAwait(false);
        await HealthyAsync(source, destination, reader, official, seed.Receipt.Token, token).ConfigureAwait(false);
        var destinationOfficial = await OfficialAsync(TwoRf3MembershipProtocol.Node5, wave.Profile.AdminKey, token).ConfigureAwait(false);
        await RemoteDocumentRf3Assertions.ReplayAsync(destination, destinationOfficial, seed, token).ConfigureAwait(false);
        await RestartOwnerAsync(source, destination, reader, official, seed, token).ConfigureAwait(false);
    }

    private static async Task DenialAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, CancellationToken token)
    {
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationBefore = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await RemoteDocumentRf3Assertions.DeniedAsync(reader, token).ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(RemoteDocumentRf3Protocol.DocumentsGet,
            new GetDocumentRequest(RemoteDocumentRf3Protocol.Reference), token), ErrorCode.PermissionDenied, dispatched: true);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationAfter = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsEqualTo(sourceBefore.Applied);
        await Assert.That(destinationAfter.Applied).IsEqualTo(destinationBefore.Applied);
        await RemoteDocumentRf3Assertions.DocumentAsync(await McpCallerAssertions.SdkSuccessAsync(
            await destination.GetAsync(RemoteDocumentRf3Protocol.Reference, token)), projected: false);
    }

    private static async Task HealthyAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, CommitToken minimum, CancellationToken token)
    {
        var sourceBefore = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationBefore = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await RemoteDocumentRf3Assertions.DocumentAsync(await McpCallerAssertions.SdkSuccessAsync(
            await reader.GetAsync(RemoteDocumentRf3Protocol.Reference, minimum, token)), projected: true);
        var request = new GetDocumentRequest(RemoteDocumentRf3Protocol.Reference, minimum);
        var document = (await McpCallerAssertions.SuccessAsync<DocumentResult?>(await official.CallAsync(
            RemoteDocumentRf3Protocol.DocumentsGet, request, token))).Value;
        await RemoteDocumentRf3Assertions.DocumentAsync(document, projected: true);
        await RemoteDocumentRf3Assertions.DocumentAsync(await SqlRf3Protocol.SdkAsync<DocumentResult?>(reader,
            SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition, RemoteDocumentRf3Protocol.DocumentsGet, request), token), projected: true);
        await RemoteDocumentRf3Assertions.DocumentAsync(await SqlRf3Protocol.McpAsync<DocumentResult?>(official,
            SqlRf3Protocol.Call(RemoteDocumentRf3Protocol.Partition, RemoteDocumentRf3Protocol.DocumentsGet, request), token), projected: true);
        var sourceAfter = await McpCallerAssertions.SdkSuccessAsync(await source.StatusAsync(token));
        var destinationAfter = await McpCallerAssertions.SdkSuccessAsync(await destination.StatusAsync(token));
        await Assert.That(sourceAfter.Applied).IsEqualTo(sourceBefore.Applied);
        await Assert.That(destinationAfter.Applied).IsEqualTo(destinationBefore.Applied);
    }

    private async Task RestartOwnerAsync(KeyLoadClient source, KeyLoadClient destination,
        KeyLoadClient reader, McpOfficialClient official, RemoteDocumentRf3Seed seed, CancellationToken token)
    {
        await wave.RemoteRuntime.KillAsync(TwoRf3MembershipProtocol.Node4,
            RemoteDocumentRf3Protocol.RestartScenario, token).ConfigureAwait(false);
        await wave.RemoteRuntime.RestartAsync(TwoRf3MembershipProtocol.Node4, token).ConfigureAwait(false);
        await HealthyAsync(source, destination, reader, official, seed.Receipt.Token, token).ConfigureAwait(false);
        var fresh = await OfficialAsync(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey, token).ConfigureAwait(false);
        await RemoteDocumentRf3Assertions.ReplayAsync(destination, fresh, seed, token).ConfigureAwait(false);
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
