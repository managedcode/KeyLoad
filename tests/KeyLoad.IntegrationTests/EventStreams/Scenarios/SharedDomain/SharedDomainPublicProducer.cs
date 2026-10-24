using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.RecoveryTests.EventStreams;
using static KeyLoad.RecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class SharedDomainPublicProducer
{
    internal static async Task WriteAsync(ClusterFixture fixture, SharedDomainPublicState state, bool healthy, CancellationToken token)
    {
        var id = healthy ? Guid.NewGuid() : CommandId;
        var item = healthy ? Healthy : Item;
        var history = healthy ? state.Healthy : state.Originals;
        await SharedDomainPublicSession.RunAsync(fixture, state.Identity.Secret, async (sdk, official) =>
        {
            foreach (var scope in state.Scopes)
            {
                var command = SharedDomainCorpus.Producer(scope, id, item);
                var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
                await SharedDomainPublicValues.ProducerAsync(command, receipt);
                history.Add((command, receipt));
                await ReplayAsync(sdk, official, command, receipt, token);
            }
        }, token);
        state.HealthyWritten = healthy;
    }

    internal static async Task ReplayAllAsync(ClusterFixture fixture, SharedDomainPublicState state, CancellationToken token)
    {
        await SharedDomainPublicSession.RunAsync(fixture, state.Identity.Secret, async (sdk, official) =>
        {
            foreach (var original in state.Originals)
            {
                if (state.Fenced)
                {
                    await SharedDomainPublicRefusals.DeniedAsync(sdk, official, original.Command, token);
                }
                else
                {
                    await ReplayAsync(sdk, official, original.Command, original.Receipt, token);
                }
            }
            foreach (var healthy in state.Healthy)
            {
                await ReplayAsync(sdk, official, healthy.Command, healthy.Receipt, token);
            }
        }, token);
    }

    private static async Task ReplayAsync(KeyLoadClient sdk, McpOfficialClient official, CommandRequest command,
        CommitReceipt original, CancellationToken token)
    {
        await SharedDomainPublicValues.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token)), original);
        await SharedDomainPublicValues.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await official.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value, original);
    }
}
