using KeyLoad.Core.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferAttemptClusterTrial
{
    internal static async Task RunAsync(int ceiling)
    {
        var subject = RemoteTransferCoordinatorClusterProtocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new RemoteTransferCoordinatorFixtureSelection(subject, ceiling));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                await ExecuteAsync(fixture, subject, ceiling, deadline.Token);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ClusterFixture fixture, string subject, int ceiling, CancellationToken token)
    {
        var seed = await RemoteTransferCoordinatorClusterSetup.CreateAsync(fixture, subject, fullTarget: true, token);
        CommitReceipt? created = null;
        QueueTransferInspection? intent = null;
        CommandRequest? failed = null;
        RemoteTransferCoordinationHint? hint = null;
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            created = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seed.Create, token));
            await QueueProducerClusterAssertions.ReplayAsync(sdk, mcp, seed.Create, created, token);
            intent = await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, null, token);
            hint = RemoteTransferCoordinatorClusterAssertions.Hint(seed, created, intent) with
            {
                AcceptAttemptCeiling = ceiling
            };
            failed = await RemoteTransferCoordinatorClusterAssertions.FailedAcceptAsync(sdk, mcp, seed, hint, intent, token);
        }, token);
        ArgumentNullException.ThrowIfNull(created);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(failed);
        ArgumentNullException.ThrowIfNull(hint);
        await QueueProducerClusterCold.RestartAsync(fixture, token);
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, intent, token);
            await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, null, token);
            await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed, null, token);
            await QueueProducerClusterAssertions.DeniedAsync(await sdk.CommitAsync(failed, token), ErrorCode.ResourceExhausted);
            await RemoteTransferAttemptClusterRepair.ExecuteAsync(sdk, mcp, seed, intent, ceiling, token);
            await RemoteTransferAttemptClusterHealthy.ProveAsync(sdk, mcp, seed, hint, intent, failed, ceiling, token);
        }, token);
        await QueueProducerClusterCold.RestartAsync(fixture, token);
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            await QueueProducerClusterAssertions.ReplayAsync(sdk, mcp, seed.Create, created, token);
            await RemoteTransferAttemptClusterHealthy.ProveAsync(sdk, mcp, seed, hint, intent, failed, ceiling, token);
        }, token);
    }
}
