using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferColdTrial
{
    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ClusterFixture();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await fixture.InitializeAsync();
                using var deadline = McpCallerDeadline.Create();
                await ExecuteAsync(fixture, deadline.Token);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(ClusterFixture fixture, CancellationToken token)
    {
        var seed = await RemoteTransferColdSetup.CreateAsync(fixture, token);
        (CommitReceipt Receipt, QueueTransferInspection Intent)? original = null;
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        { original = await RemoteTransferColdStages.CreateAsync(sdk, mcp, seed, token); }, token);
        var created = original ?? throw new InvalidOperationException(RemoteTransferColdProtocol.Missing);
        await QueueProducerRf3Cold.RestartAsync(fixture, token);
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        { await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, seed.Create, created.Receipt, token); }, token);
        await RemoteTransferColdRefusals.ExecuteAsync(fixture, seed, created.Intent, token);
        var accept = seed.Accept(created.Intent);
        (CommitReceipt Receipt, QueueTransferReceiptInspection Proof)? accepted = null;
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            await RemoteTransferColdRefusals.OriginalEpochRefusedAsync(sdk, mcp, seed, token);
            accepted = await RemoteTransferColdStages.AcceptAsync(sdk, mcp, seed, created.Intent, accept, RemoteTransferColdProtocol.OriginalReadySequence, token);
        }, token);
        var target = accepted ?? throw new InvalidOperationException(RemoteTransferColdProtocol.Missing);
        await QueueProducerRf3Cold.RestartAsync(fixture, token);
        var complete = seed.Complete(target.Proof);
        CommitReceipt? completed = null;
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, accept, target.Receipt, token);
            await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, created.Intent, token);
            await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, target.Proof, token);
            await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed,
                RemoteTransferColdAssertions.Ready(seed, RemoteTransferColdProtocol.OriginalReadySequence), token);
            completed = await RemoteTransferColdStages.CompleteAsync(sdk, mcp, seed, created.Intent,
                target.Proof, complete, token);
        }, token);
        ArgumentNullException.ThrowIfNull(completed);
        await QueueProducerRf3Cold.RestartAsync(fixture, token);
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            await RemoteTransferColdRefusals.OriginalEpochRefusedAsync(sdk, mcp, seed, token);
            await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, accept, target.Receipt, token);
            await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, complete, completed, token);
            await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, created.Intent with
            { State = QueueTransferState.Delivered, ReceiptToken = target.Proof.ReceiptToken }, token);
            await RemoteTransferColdFinish.ExecuteAsync(sdk, mcp, seed, created.Intent, target.Proof, token);
        }, token);
    }
}
