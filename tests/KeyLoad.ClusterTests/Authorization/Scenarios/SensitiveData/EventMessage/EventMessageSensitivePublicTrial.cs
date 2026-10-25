using KeyLoad.ClusterTests.ChangeFeeds;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.Messaging;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class EventMessageSensitivePublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, bool subscription, bool dataAuthority, bool header)
    {
        using var deadline = McpCallerDeadline.Create();
        var states = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async root =>
            {
                var seeded = await EventMessageSensitivePublicSeed.RunAsync(root, subscription, dataAuthority, header, deadline.Token);
                await RunReadersAsync(fixture, root, seeded, false, deadline.Token);
                seeded.Before = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.StatusAsync(deadline.Token));
                var safe = await EventMessageSensitiveSafeR52Seed.RunAsync(root, subscription, dataAuthority, header, deadline.Token);
                await EventMessageSensitiveSafeR52Flow.WarmAsync(fixture, root, safe, deadline.Token);
                return (Original: seeded, Safe: safe);
            }, deadline.Token);
        await FeedLiveClusterCold.RestartAsync(fixture, deadline.Token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async root =>
            {
                await RunReadersAsync(fixture, root, states.Original, true, deadline.Token);
                await EventMessageSensitiveSafeR52Flow.AfterFirstColdAsync(fixture, root, states.Safe, deadline.Token);
                return true;
            }, deadline.Token);
        await FeedLiveClusterCold.RestartAsync(fixture, deadline.Token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async root =>
            {
                await EventMessageSensitiveSafeR52Flow.AfterSecondColdAsync(fixture, root, states.Safe, deadline.Token);
                await EventMessageSensitivePublicLifecycle.ReplayAsync(fixture, states.Original, deadline.Token);
                return true;
            }, deadline.Token);
    }

    private static Task<bool> RunReadersAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        EventMessageSensitivePublicState state, bool cold, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Key,
            async worker => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.InspectorKey,
                async inspector =>
                {
                    if (cold)
                    {
                        await ColdAsync(fixture, root, worker, state, token);
                    }
                    else
                    {
                        await LiveAsync(fixture, root, worker, inspector, state, token);
                    }
                    return true;
                }, token), token);

    private static async Task LiveAsync(ClusterFixture fixture, RequestCqrsClusterCallers root, RequestCqrsClusterCallers worker,
        RequestCqrsClusterCallers inspector, EventMessageSensitivePublicState state, CancellationToken token)
    {
        var caller = state.DataAuthority ? root : worker;
        state.OriginalId = Guid.NewGuid();
        state.OriginalRequest = EventMessageSensitivePublicOperations.Receive(state, state.OriginalId, false);
        state.Original = await EventMessageSensitivePublicOperations.ReceiveAsync(caller, state, state.OriginalRequest, QueueLifecyclePublicProtocol.Sdk, token);
        await EventMessageSensitivePublicAssertions.FullAsync(state.Original, state);
        if (state.Subscription)
        {
            state.OriginalPage = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadEventSourceAsync(new(state.Source), token));
        }
        else
        {
            state.OriginalInspection = await PublicReadAssertions.MessageAsync(await root.Sdk.InspectAsync(new(state.Lane, EventMessageSensitivePublicProtocol.Message), token));
        }
        await EventMessageSensitivePublicOperations.PolicyAsync(root, state, false, token);
        await EventMessageSensitivePublicRefusals.ReceiveAsync(caller, state, state.OriginalRequest, ErrorCode.PermissionDenied, token);
        await EventMessageSensitivePublicReads.RedactedAsync(inspector, state, false, token);
        await EventMessageSensitivePublicOperations.PolicyAsync(root, state, true, token);
        var fresh = EventMessageSensitivePublicOperations.Receive(state, Guid.NewGuid(), true);
        await EventMessageSensitivePublicRefusals.ReceiveAsync(caller, state, fresh, ErrorCode.PermissionDenied, token);
        await EventMessageSensitivePublicContinuation.RepairAsync(root, caller, state, token);
        await EventMessageSensitivePublicRefusals.ReceiveAsync(caller, state, state.OriginalRequest, ErrorCode.PermissionDenied, token);
        await EventMessageSensitivePublicContinuation.HealthyAsync(fixture, root, worker, inspector, state, token);
        await EventMessageSensitivePublicAssertions.ReplayAsync(root, caller, state, token);
        await EventMessageSensitivePublicLifecycle.ReplayAsync(fixture, state, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, RequestCqrsClusterCallers root, RequestCqrsClusterCallers worker,
        EventMessageSensitivePublicState state, CancellationToken token)
    {
        var after = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.StatusAsync(token));
        await Assert.That(after.NodeId).IsEqualTo(state.Before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(state.Before.Incarnation);
        await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(state.Before.ReadGeneration);
        await Assert.That(after.Position).IsGreaterThanOrEqualTo(state.Before.Position);
        var caller = state.DataAuthority ? root : worker;
        await EventMessageSensitivePublicRefusals.ReceiveAsync(caller, state, state.OriginalRequest, ErrorCode.PermissionDenied, token);
        await EventMessageSensitivePublicAssertions.ReplayAsync(root, caller, state, token);
        await EventMessageSensitivePublicLifecycle.ReplayAsync(fixture, state, token);
        await EventMessageSensitivePublicContinuation.FinishAsync(root, caller, state, token);
    }
}
