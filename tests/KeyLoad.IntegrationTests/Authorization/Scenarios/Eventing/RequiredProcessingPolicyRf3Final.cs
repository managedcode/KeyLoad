using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RequiredProcessingPolicyRf3Final
{
    internal static Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader, RequiredProcessingPolicyRf3State state, CancellationToken token)
        => RequiredProcessingPolicyRf3Values.ObserveAsync(root, state, 3, async () =>
        {
            await RequiredProcessingPolicyRf3Refusals.AllAsync(reader, state, state.Original, RequiredProcessingPolicyRf3State.EpochChanged, token);
            await RequiredProcessingPolicyRf3Refusals.AllAsync(reader, state, state.Refused, RequiredProcessingPolicyRf3State.EpochChanged, token);
            await ReplayProcessingAsync(root, reader, state, state.Healthy, state.HealthyReceipt, 3, token);
            foreach (var producer in state.Producers)
            {
                for (var route = 0; route < 4; route++)
                {
                    await SqlRf3Protocol.EqualAsync(producer.Receipt, await QueueLifecyclePublicRoutes.CallAsync(root, route, state.Partition,
                        McpCallerTools.DocumentsCommit, producer.Request, () => root.Sdk.CommitAsync(producer.Request, token), token));
                }
            }
        }, token);
    internal static async Task ReplayProcessingAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        RequiredProcessingPolicyRf3State state, ProcessingRequest request, CommitReceipt receipt, int phase, CancellationToken token)
    {
        for (var route = 0; route < 4; route++)
        {
            await RequiredProcessingPolicyRf3Values.ObserveAsync(root, state, phase, async () =>
                await SqlRf3Protocol.EqualAsync(receipt, await RequiredProcessingPolicyRf3Routes.ProcessAsync(reader, state, request, route, token)), token);
        }
    }
}
