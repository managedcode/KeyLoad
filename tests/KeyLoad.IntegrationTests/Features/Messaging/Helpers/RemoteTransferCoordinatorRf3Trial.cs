using KeyLoad.Client;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferCoordinatorRf3Trial
{
    internal static async Task RunAsync(bool fullTarget)
    {
        var subject = RemoteTransferCoordinatorRf3Protocol.SubjectPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture(new RemoteTransferCoordinatorFixtureSelection(subject));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                await ExecuteAsync(fixture, subject, fullTarget, deadline.Token);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ClusterFixture fixture, string subject, bool fullTarget, CancellationToken token)
    {
        var seed = await RemoteTransferCoordinatorRf3Setup.CreateAsync(fixture, subject, fullTarget, token);
        CommitReceipt? created = null;
        QueueTransferInspection? intent = null;
        CommandRequest? failed = null;
        RemoteTransferCoordinationHint? hint = null;
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            created = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seed.Create, token));
            await RemoteTransferColdAssertions.ReceiptLiteralAsync(created, seed.Create, seed.Scenario.SourceQueue,
                seed.TransferId, RemoteTransferColdProtocol.CreateKind, RemoteTransferColdProtocol.CreateRevision);
            intent = await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, null, token);
            hint = RemoteTransferCoordinatorRf3Assertions.Hint(seed, created, intent);
            if (fullTarget)
            {
                await Assert.That(intent.State).IsEqualTo(QueueTransferState.OutputPending);
                failed = await RemoteTransferCoordinatorRf3Assertions.FailedAcceptAsync(sdk, mcp, seed, hint, intent, token);
            }
        }, token);
        ArgumentNullException.ThrowIfNull(created);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(hint);
        await QueueProducerRf3Cold.RestartAsync(fixture, token);
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, seed.Create, created, token);
            if (fullTarget)
            { await RepairAsync(sdk, mcp, seed, intent, failed!, token); }
            var delivered = await RemoteTransferCoordinatorRf3Wait.DeliveredAsync(sdk, seed, token);
            _ = await RemoteTransferCoordinatorRf3Assertions.CompletedAsync(sdk, mcp, seed, hint, delivered,
                fullTarget ? RemoteTransferCoordinatorRf3Protocol.RepairedReady : RemoteTransferCoordinatorRf3Protocol.OriginalReady, token);
        }, token);
        await QueueProducerRf3Cold.RestartAsync(fixture, token);
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            var delivered = await RemoteTransferCoordinatorRf3Wait.DeliveredAsync(sdk, seed, token);
            _ = await RemoteTransferCoordinatorRf3Assertions.CompletedAsync(sdk, mcp, seed, hint, delivered,
                fullTarget ? RemoteTransferCoordinatorRf3Protocol.RepairedReady : RemoteTransferCoordinatorRf3Protocol.OriginalReady, token);
            await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, seed.Create, created, token);
            if (failed is not null)
            { await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(failed, token), ErrorCode.ResourceExhausted); }
        }, token);
    }

    private static async Task RepairAsync(KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, CommandRequest failed, CancellationToken token)
    {
        await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, intent, token);
        await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, null, token);
        await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed, null, token);
        await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(failed, token), ErrorCode.ResourceExhausted);
        await RemoteTransferCoordinatorRf3Repair.AcceptAsync(sdk, mcp, seed, intent, token);
        await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(failed, token), ErrorCode.ResourceExhausted);
    }
}
