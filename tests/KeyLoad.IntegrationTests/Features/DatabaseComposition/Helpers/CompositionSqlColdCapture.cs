using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class CompositionSqlColdCapture
{
    internal static async Task<CompositionSqlColdState> CaptureAsync(KeyLoadClient sdk,
        RelationalSqlRf3Scenario scenario, QueueGraphLink link, CommandRequest forward, CommitReceipt forwardReceipt,
        CommandRequest reverse, CommitReceipt reverseReceipt, CancellationToken token)
    {
        var first = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.First, token));
        var second = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.Second, token));
        var graph = await McpCallerAssertions.SdkSuccessAsync(await sdk.TraverseAsync(new(scenario.Partition,
            RelationalSqlRf3Tokens.Graph, scenario.First), token));
        var source = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(scenario.Inspect(CompositionSqlColdProtocol.Message), token));
        var derived = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(scenario.Inspect(
            CompositionSqlColdProtocol.MessagePrefix + CompositionSqlColdProtocol.EdgePrefix + CompositionSqlColdProtocol.Message), token));
        return new(scenario, link, forward, forwardReceipt, reverse, reverseReceipt, first!, second!, graph, source!, derived!);
    }
}
