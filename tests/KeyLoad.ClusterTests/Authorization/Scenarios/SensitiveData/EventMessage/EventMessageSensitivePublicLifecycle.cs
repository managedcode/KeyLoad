using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.Messaging;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Core;

namespace KeyLoad.ClusterTests.Authorization;

internal static class EventMessageSensitivePublicLifecycle
{
    private const string AdministratorSuffix = "-redrive";
    private const string CredentialSuffix = ".keyload-sensitive-lifecycle-credential";
    private const string AllFields = "*";

    internal static async Task RedriveAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        EventMessageSensitivePublicState state, CommandRequest command, CancellationToken token)
    {
        var id = PrincipalId(state);
        var principal = new PrincipalRecord(id, state.Partition.TenantId,
            [new(state.Partition.DatabaseId, state.Resource.Name, Capability.DeadLettersRedrive)], [AllFields])
        {
            ClusterAdministrator = true
        };
        await SqlClusterProtocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            AuthorizationExecution.Credential(id, id, Credential(state)), token));
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, Credential(state), async caller =>
        {
            state.Redrive = (command, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token)));
            await Assert.That(state.Redrive.Value.Receipt.CommandId).IsEqualTo(command.CommandId);
            return true;
        }, token);
    }

    internal static async Task ReplayAsync(ClusterFixture fixture, EventMessageSensitivePublicState state,
        CancellationToken token)
    {
        if (state.Redrive is not { } original)
        {
            return;
        }
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, Credential(state), async caller =>
        {
            foreach (var route in QueueLifecyclePublicProtocol.Routes)
            {
                var actual = await QueueLifecyclePublicRoutes.CallAsync(caller, route, state.Partition,
                    McpCallerTools.DocumentsCommit, original.Command, () => caller.Sdk.CommitAsync(original.Command, token), token);
                await SqlClusterProtocol.EqualAsync(original.Receipt, actual);
            }
            return true;
        }, token);
    }

    private static string PrincipalId(EventMessageSensitivePublicState state) => state.Worker.Id + AdministratorSuffix;
    private static string Credential(EventMessageSensitivePublicState state) => PrincipalId(state) + CredentialSuffix;
}
