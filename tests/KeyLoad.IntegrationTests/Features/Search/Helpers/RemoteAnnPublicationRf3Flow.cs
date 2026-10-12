using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class RemoteAnnPublicationRf3Flow
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        var administrator = await PartitionMovementPublicParentRf3Administrator.PersistAsync(wave, token);
        RemoteAnnPublicationRf3State original;
        using (var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1))
        {
            var sdk = new KeyLoadClient(http, administrator, IntegrationClientOptions.Execution());
            await using var official = await McpOfficialClient.ConnectAsync(wave.Application,
                TwoRf3MembershipProtocol.Node1, administrator, token);
            original = await RemoteAnnPublicationRf3Build.SeedAsync(sdk, official, token);
        }
        var reader = await RemoteAnnPublicationRf3Reader.CreateAsync(wave, original.Scenario, administrator, token);
        await RemoteAnnPublicationRf3Round.RunAsync(wave, administrator, reader, TwoRf3MembershipProtocol.Node1,
            TwoRf3MembershipProtocol.Node2, original, RemoteAnnPublicationRf3Protocol.FirstEpoch, true, token);
        // Existing fixture owner stops/joins the actual AppHost and all eighteen locks before reopening this same root.
        await wave.RestartJoinedAsync(token);
        await RemoteAnnPublicationRf3Round.RestoreAsync(wave, administrator, TwoRf3MembershipProtocol.Node1, original, token);
        await RemoteAnnPublicationRf3Round.RunAsync(wave, administrator, reader, TwoRf3MembershipProtocol.Node1,
            TwoRf3MembershipProtocol.Node2, original, RemoteAnnPublicationRf3Protocol.FieldRepairedEpoch, false, token);
        var moved = await RemoteAnnPublicationRf3Movement.TransferAsync(wave, administrator, original, token);
        RemoteAnnPublicationRf3State destination;
        using (var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node4))
        {
            var sdk = new KeyLoadClient(http, administrator, IntegrationClientOptions.Execution());
            await using var official = await McpOfficialClient.ConnectAsync(wave.Application,
                TwoRf3MembershipProtocol.Node4, administrator, token);
            destination = await RemoteAnnPublicationRf3Build.RebuildMovedAsync(sdk, official, original, token);
        }
        await RemoteAnnPublicationRf3Round.RunAsync(wave, administrator, reader, TwoRf3MembershipProtocol.Node4,
            TwoRf3MembershipProtocol.Node5, destination, RemoteAnnPublicationRf3Protocol.FirstEpoch, true, token);
        var stopped = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, moved.Request, token);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(stopped);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await wave.WaitForSixHealthyAsync(token);
        await RemoteAnnPublicationRf3Round.RestoreAsync(wave, administrator, TwoRf3MembershipProtocol.Node4, destination, token);
        await RemoteAnnPublicationRf3Round.RunAsync(wave, administrator, reader, TwoRf3MembershipProtocol.Node4,
            TwoRf3MembershipProtocol.Node5, destination, RemoteAnnPublicationRf3Protocol.FieldRepairedEpoch, false, token);
        await RemoteAnnPublicationRf3Movement.ReplayAsync(wave, administrator, moved.Request, moved.Result, token);
    }
}
