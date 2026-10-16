using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.ResourceExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>Retains the actual independently seeded native controls through all unrelated table retirements.</summary>
internal static class DropTableLifecycleNativeControls
{
    internal static async Task VerifyAsync(TwoRf3MembershipWave wave,
        ResourcePartitionInventoryRf3Seed seed, RemoteTransferNativeMembership membership,
        RequestCqrsRf3Callers callers, CancellationToken token)
    {
        var inventory = new Dictionary<string, ResourceOwnerInventoryResultV1>(StringComparer.Ordinal);
        foreach (var creation in seed.Creations)
        {
            await SqlRf3Protocol.EqualAsync(creation.Request.Definition, await McpCallerAssertions.SdkSuccessAsync(
                await callers.Sdk.ConfigureResourceAsync(creation.CommandId, creation.Request, token)));
            var arguments = McpOfficialClient.Arguments(creation.Request);
            arguments.Add(McpCallerProtocol.CommandId, creation.CommandId);
            await SqlRf3Protocol.EqualAsync(creation.Request.Definition,
                (await McpCallerAssertions.SuccessAsync<ResourceDefinition>(await callers.Mcp.Client
                    .InvokeKeyLoadToolAsync(McpCallerTools.ResourcesConfigure, arguments, token))).Value);
            var actual = await DropTableLifecycleSeed.InventoryAsync(wave, seed, membership,
                creation.Request.Definition.Name, token);
            await SqlRf3Protocol.EqualAsync(creation.Request.Definition, actual.Control.Definition);
            inventory.Add(creation.Request.Definition.Name, actual);
        }
        foreach (var original in seed.Originals)
        {
            await NativeSubmissionColdAssertions.ReplayAsync(callers, original.Command, original.Receipt, token);
            var row = DropTableLifecycleSeedReferences.Bind(original.Document,
                inventory[original.Document.Reference.Collection]);
            await DropTableLifecycleSeedAssertions.RowsAsync(callers, [row], token);
        }
    }
}
