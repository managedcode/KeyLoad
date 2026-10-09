using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3EventingContinuation
{
    internal static async Task RunAsync(KeyLoadClient sdk, McpOfficialClient official,
        ClusterRestoreRf3EventingState state, PhysicalShardRecord owner, CancellationToken cancellationToken)
    {
        var seek = new SeekSubscriptionRequest(Guid.NewGuid(), state.Group,
            ClusterRestoreRf3EventingProtocol.InitialGeneration, SubscriptionStart.FromBeginning);
        var expected = new SubscriptionInfo(state.Group, new(PartitionMovementPublicParentRf3Administrator.PrincipalId),
            ClusterRestoreRf3EventingProtocol.ReconciledGeneration, ClusterRestoreRf3EventingProtocol.ReconciledOwnershipEpoch,
            ClusterRestoreRf3EventingProtocol.EmptyCheckpoint, ClusterRestoreRf3EventingProtocol.EmptyCheckpoint,
            ClusterRestoreRf3EventingProtocol.LastSourcePosition, true, null);
        await ClusterRestoreRf3EventingReadOracle.FourAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await sdk.SeekSubscriptionAsync(seek, cancellationToken).ConfigureAwait(false)), sdk, official, state.Partition,
            McpCallerTools.SubscriptionsSeek, seek, cancellationToken).ConfigureAwait(false);
        var unpause = new SetSubscriptionPausedRequest(Guid.NewGuid(), state.Group,
            ClusterRestoreRf3EventingProtocol.ReconciledGeneration, false);
        await ClusterRestoreRf3EventingReadOracle.FourAsync(expected with { Paused = false },
            await McpCallerAssertions.SdkSuccessAsync(await sdk.SetSubscriptionPausedAsync(unpause,
            cancellationToken).ConfigureAwait(false)), sdk, official, state.Partition,
            McpCallerTools.SubscriptionsPause, unpause, cancellationToken).ConfigureAwait(false);
        await ProcessRetainedAsync(sdk, official, state, owner, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3EventingHealthy.RunAsync(sdk, official, state, owner, cancellationToken).ConfigureAwait(false);
        state.Resumed = true;
        state.FinalOutbox = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(state.Partition,
            cancellationToken).ConfigureAwait(false));
        await ClusterRestoreRf3EventingReadOracle.RequireAsync(sdk, official, state, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ProcessRetainedAsync(KeyLoadClient sdk, McpOfficialClient official,
        ClusterRestoreRf3EventingState state, PhysicalShardRecord owner, CancellationToken cancellationToken)
    {
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), state.Group,
            MaxEvents: ClusterRestoreRf3EventingProtocol.SourceEvents);
        var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveSubscriptionAsync(request,
            cancellationToken).ConfigureAwait(false));
        await ClusterRestoreRf3EventingReceiveOracle.GroupAsync(request, state, owner, received, healthy: false);
        for (var index = ClusterRestoreRf3EventingProtocol.FirstIndex; index < received.Deliveries.Length; index++)
        { await SqlRf3Protocol.EqualAsync(state.Page.Events[index], received.Deliveries[index].Event); }
        await ClusterRestoreRf3EventingReadOracle.FourAsync(received, received, sdk, official, state.Partition,
            McpCallerTools.SubscriptionsReceive, request, cancellationToken).ConfigureAwait(false);
        var process = new SubscriptionProcessingRequest(Guid.NewGuid(), state.Group,
            received.Deliveries[ClusterRestoreRf3EventingProtocol.FirstIndex].Token, ClusterRestoreRf3EventingProtocol.Handler,
            ClusterRestoreRf3EventingProtocol.InitialGeneration, ClusterRestoreRf3EventingSeed.Effects());
        var processed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitSubscriptionProcessingAsync(process,
            cancellationToken).ConfigureAwait(false));
        await ClusterRestoreRf3EventingReceiptOracle.GroupAckAsync(process.CommandId, state, owner,
            ClusterRestoreRf3EventingProtocol.FirstPosition,
            received.Deliveries[ClusterRestoreRf3EventingProtocol.FirstIndex].LeaseVersion, processed.Receipt);
        await Assert.That(processed.AlreadyProcessed).IsTrue();
        await SqlRf3Protocol.EqualAsync(state.Processed.OriginalEffectsToken, processed.OriginalEffectsToken);
        await ClusterRestoreRf3EventingReadOracle.FourAsync(processed, processed, sdk, official, state.Partition,
            McpCallerTools.SubscriptionsProcess, process, cancellationToken).ConfigureAwait(false);
        await ClusterRestoreRf3EventingReadOracle.EffectsAsync(sdk, official, state, cancellationToken).ConfigureAwait(false);
        foreach (var delivery in received.Deliveries.Skip(ClusterRestoreRf3EventingProtocol.SingleDelivery))
        {
            var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Group, delivery.Token, DeliveryAction.Ack);
            var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteSubscriptionAsync(ack,
                cancellationToken).ConfigureAwait(false));
            await ClusterRestoreRf3EventingReceiptOracle.GroupAckAsync(ack.CommandId, state, owner,
                delivery.Event.Position, delivery.LeaseVersion, result);
            await ClusterRestoreRf3EventingReadOracle.FourAsync(result, result, sdk, official, state.Partition,
                McpCallerTools.SubscriptionsComplete, ack, cancellationToken).ConfigureAwait(false);
        }
    }
}
