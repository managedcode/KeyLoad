using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Messaging;

internal static class CompetingInboxPublicHealthy
{
    internal static async Task RunAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers worker, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await ProduceAsync(state, root, worker, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicModels.RequireAsync(state, root, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ProduceAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers worker, CancellationToken token)
    {
        await CompetingInboxPublicSeed.EnqueueAsync(state, root, CompetingInboxPublicProtocol.Next, token);
        var delivery = await CompetingInboxPublicSeed.ReceiveAsync(state, worker, CompetingInboxPublicProtocol.Next, token);
        var request = state.Processing(delivery, CompetingInboxPublicProtocol.Healthy, CompetingInboxPublicProtocol.HealthyJson);
        var before = state.Clock.GetUtcNow();
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CommitProcessingAsync(request, token));
        state.EventWindows.Add(CompetingInboxPublicProtocol.Healthy, (before, state.Clock.GetUtcNow()));
        state.Current.Add((request, receipt));
        state.HasHealthy = true;
        await CompetingInboxPublicValues.ReceiptAsync(state, receipt, request.CommandId,
            CompetingInboxPublicValues.Effects(CompetingInboxPublicProtocol.Next, CompetingInboxPublicProtocol.Healthy));
        await CompetingInboxPublicModels.OutboxAsync(state, root, CompetingInboxPublicProtocol.FinalOutboxRecords, true, token);
        await CompetingInboxPublicModels.RequireAsync(state, root, token);
    }
}
