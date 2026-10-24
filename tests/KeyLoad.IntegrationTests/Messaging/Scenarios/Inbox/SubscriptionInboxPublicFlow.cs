using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class SubscriptionInboxPublicFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, SubscriptionFilterClusterSeed seed, CancellationToken token)
    {
        SubscriptionInboxPublicState? state = null;
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed, async (sdk, mcp) =>
        {
            state = await StartAsync(sdk, mcp, seed, token);
        }, token);
        var original = state ?? throw new InvalidOperationException("The original subscription flow did not finish.");
        await SubscriptionFilterClusterCold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => RepairAsync(sdk, mcp, original, token), token);
        await SubscriptionFilterClusterCold.RestartAsync(fixture, token);
        await SubscriptionPhasePublicTrial.WithCallersAsync(fixture, seed,
            (sdk, mcp) => FinishAsync(sdk, mcp, original, token), token);
    }

    private static async Task<SubscriptionInboxPublicState> StartAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionFilterClusterSeed seed, CancellationToken token)
    {
        var source = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(seed.Group.Source), token));
        var first = SubscriptionPhasePublicAssertions.Receive(seed.Group);
        var initial = await SubscriptionPhasePublicAssertions.ClaimAsync(sdk, mcp, first, source, [1L, 2L, 3L], token);
        var process = new SubscriptionProcessingRequest(Guid.NewGuid(), seed.Group, initial.Deliveries[0].Token,
            SubscriptionFilterClusterProtocol.Handler, 1L, SubscriptionInboxPublicAssertions.Effects);
        var failedProcessing = await SubscriptionProcessingRollbackPublicAssertions.StartAsync(
            sdk, mcp, process, first, initial, source, token);
        var processed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitSubscriptionProcessingAsync(process, token));
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionProcessingResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsProcess, process, token))).Value, processed);
        await Assert.That(processed.AlreadyProcessed).IsFalse();
        await Assert.That(processed.Receipt.CommandId).IsEqualTo(process.CommandId);
        await Assert.That(processed.Receipt.Mutations).IsEquivalentTo(new MutationReceipt[] {
            new("putDocument", SubscriptionFilterClusterProtocol.Collection, "inbox-first", 1L),
            new("putDocument", SubscriptionFilterClusterProtocol.Collection, "inbox-second", 1L),
            new("Ack", seed.Group.Source.Resource, "1", 1L) }, CollectionOrdering.Matching);
        var seek = new SeekSubscriptionRequest(Guid.NewGuid(), seed.Group, 1L, SubscriptionStart.FromBeginning);
        var sought = await McpCallerAssertions.SdkSuccessAsync(await sdk.SeekSubscriptionAsync(seek, token));
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsSeek, seek, token))).Value, sought);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.SetSubscriptionPausedAsync(new(Guid.NewGuid(), seed.Group, 2L, false), token));
        var current = SubscriptionPhasePublicAssertions.Receive(seed.Group);
        var claimed = await SubscriptionPhasePublicAssertions.ClaimAsync(sdk, mcp, current, source, [1L, 2L, 3L], token);
        for (var i = 0; i < claimed.Deliveries.Length; i++)
        {
            await Assert.That(claimed.Deliveries[i].Token).IsNotEqualTo(initial.Deliveries[i].Token);
        }
        var conflict = process with
        {
            CommandId = Guid.NewGuid(),
            Token = claimed.Deliveries[0].Token,
            Effects = SubscriptionInboxPublicAssertions.ChangedEffects
        };
        var problem = await SubscriptionInboxPublicAssertions.ConflictAsync(sdk, mcp, conflict, token);
        await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, seed.Group, 2L, 0L, false, token);
        await SubscriptionFilterClusterAssertions.HistoryAsync(sdk, mcp, seed.Group.Source, source, token);
        return new(first, initial, process, processed, current, claimed, conflict, problem, source)
        {
            FailedProcessing = failedProcessing
        };
    }

    private static async Task RepairAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionInboxPublicState state, CancellationToken token)
    {
        var group = state.Current.Subscription;
        await SubscriptionProcessingRollbackPublicAssertions.ReplayAsync(sdk, mcp, state, token);
        await SubscriptionInboxPublicAssertions.OriginalFencedAsync(sdk, mcp, state, token);
        await Assert.That(await SubscriptionInboxPublicAssertions.ConflictAsync(sdk, mcp, state.Conflict, token))
            .IsEqualTo(state.ConflictProblem);
        var request = state.Processing with
        {
            CommandId = Guid.NewGuid(),
            Token = state.CurrentResult.Deliveries[0].Token
        };
        var repaired = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitSubscriptionProcessingAsync(request, token));
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionProcessingResult>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsProcess, request, token))).Value, repaired);
        await Assert.That(repaired.AlreadyProcessed).IsTrue();
        await Assert.That(repaired.OriginalEffectsToken).IsEqualTo(state.ProcessingResult.OriginalEffectsToken);
        await SubscriptionPhasePublicAssertions.ReceiptAsync(repaired.Receipt,
            new(request.CommandId, group, request.Token, DeliveryAction.Ack), 1L);
        state.Repair = request;
        state.Repaired = repaired;
        foreach (var index in new[] { 2, 1 })
        {
            var ack = new SubscriptionDeliveryCommand(Guid.NewGuid(), group, state.CurrentResult.Deliveries[index].Token, DeliveryAction.Ack);
            var receipt = await SubscriptionPhasePublicAssertions.CompleteAsync(sdk, mcp, ack, index + 1L, token);
            state.Acknowledgements.Add((ack, receipt));
            await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, group, 2L, index == 2 ? 1L : 3L, false, token);
        }
        await SubscriptionInboxPublicAssertions.DocumentsAsync(sdk, group, token);
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, state.Current, ErrorCode.StaleLease, token);
    }

    private static async Task FinishAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionInboxPublicState state, CancellationToken token)
    {
        var group = state.Current.Subscription;
        await SubscriptionProcessingRollbackPublicAssertions.ReplayAsync(sdk, mcp, state, token);
        await SubscriptionInboxPublicAssertions.OriginalFencedAsync(sdk, mcp, state, token);
        await Assert.That(await SubscriptionInboxPublicAssertions.ConflictAsync(sdk, mcp, state.Conflict, token))
            .IsEqualTo(state.ConflictProblem);
        var repair = state.Repair ?? throw new InvalidOperationException("The matching original inbox repair is absent.");
        var repaired = state.Repaired ?? throw new InvalidOperationException("The original matching inbox result is absent.");
        await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CommitSubscriptionProcessingAsync(repair, token)), repaired);
        foreach (var ack in state.Acknowledgements)
        {
            await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.CompleteSubscriptionAsync(ack.Request, token)), ack.Receipt);
            await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await mcp.CallAsync(McpCallerTools.SubscriptionsComplete, ack.Request, token))).Value, ack.Receipt);
        }
        await SubscriptionPhasePublicAssertions.ReceiveFailureAsync(sdk, mcp, state.Current, ErrorCode.StaleLease, token);
        await SubscriptionFilterClusterAssertions.HistoryAsync(sdk, mcp, group.Source, state.Source, token);
        await SubscriptionPhasePublicAssertions.HealthyAsync(sdk, mcp, group, state.Source, 4L, token);
        await SubscriptionInboxPublicAssertions.DocumentsAsync(sdk, group, token);
        await SubscriptionFilterClusterAssertions.StatusAsync(sdk, mcp, group, 2L, 4L, false, token);
    }
}
