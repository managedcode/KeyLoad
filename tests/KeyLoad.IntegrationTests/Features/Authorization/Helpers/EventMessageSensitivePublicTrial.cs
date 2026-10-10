using KeyLoad.IntegrationTests.Features.ChangeFeeds;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitivePublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, bool subscription, bool dataAuthority, bool header)
    {
        using var deadline = McpCallerDeadline.Create();
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async root =>
            {
                var seeded = await EventMessageSensitivePublicSeed.RunAsync(root, subscription, dataAuthority, header, deadline.Token);
                await RunReadersAsync(fixture, root, seeded, false, deadline.Token);
                seeded.Before = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.StatusAsync(deadline.Token));
                return seeded;
            }, deadline.Token);
        await FeedLiveRf3Cold.RestartAsync(fixture, deadline.Token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey,
            async root => { await RunReadersAsync(fixture, root, state, true, deadline.Token); return true; }, deadline.Token);
    }

    private static Task<bool> RunReadersAsync(ClusterFixture fixture, RequestCqrsRf3Callers root,
        EventMessageSensitivePublicState state, bool cold, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Key,
            async worker => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.InspectorKey,
                async inspector =>
                {
                    if (cold)
                    { await ColdAsync(root, worker, state, token); }
                    else
                    { await LiveAsync(root, worker, inspector, state, token); }
                    return true;
                }, token), token);

    private static async Task LiveAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers worker,
        RequestCqrsRf3Callers inspector, EventMessageSensitivePublicState state, CancellationToken token)
    {
        var caller = state.DataAuthority ? root : worker;
        state.OriginalId = Guid.NewGuid();
        state.OriginalRequest = EventMessageSensitivePublicOperations.Receive(state, state.OriginalId, false);
        state.Original = await EventMessageSensitivePublicOperations.ReceiveAsync(caller, state, state.OriginalRequest, QueueLifecyclePublicProtocol.Sdk, token);
        await EventMessageSensitivePublicAssertions.FullAsync(state.Original, state);
        if (state.Subscription)
        { state.OriginalPage = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadEventSourceAsync(new(state.Source), token)); }
        else
        { state.OriginalInspection = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.InspectAsync(new(state.Lane, EventMessageSensitivePublicProtocol.Message), token)); }
        await EventMessageSensitivePublicOperations.PolicyAsync(root, state, false, token);
        await EventMessageSensitivePublicRefusals.ReceiveAsync(caller, state, state.OriginalRequest, ErrorCode.PermissionDenied, token);
        await EventMessageSensitivePublicReads.RedactedAsync(inspector, state, false, token);
        await EventMessageSensitivePublicOperations.PolicyAsync(root, state, true, token);
        var fresh = EventMessageSensitivePublicOperations.Receive(state, Guid.NewGuid(), true);
        await EventMessageSensitivePublicRefusals.ReceiveAsync(caller, state, fresh, ErrorCode.PermissionDenied, token);
        await EventMessageSensitivePublicContinuation.RepairAsync(root, caller, state, token);
        await EventMessageSensitivePublicRefusals.ReceiveAsync(caller, state, state.OriginalRequest, ErrorCode.PermissionDenied, token);
        await EventMessageSensitivePublicContinuation.HealthyAsync(root, worker, inspector, state, token);
        await EventMessageSensitivePublicAssertions.ReplayAsync(root, caller, state, token);
    }

    private static async Task ColdAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers worker,
        EventMessageSensitivePublicState state, CancellationToken token)
    {
        var after = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.StatusAsync(token));
        await Assert.That(after.NodeId).IsEqualTo(state.Before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(state.Before.Incarnation);
        await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(state.Before.ReadGeneration);
        await Assert.That(after.Applied).IsGreaterThanOrEqualTo(state.Before.Applied);
        var caller = state.DataAuthority ? root : worker;
        await EventMessageSensitivePublicRefusals.ReceiveAsync(caller, state, state.OriginalRequest, ErrorCode.PermissionDenied, token);
        await EventMessageSensitivePublicAssertions.ReplayAsync(root, caller, state, token);
        await EventMessageSensitivePublicContinuation.FinishAsync(root, caller, state, token);
    }
}
