
namespace KeyLoad.ClusterTests.Messaging;

internal static class SubscriptionIndependentCheckpointR52Flow
{
    internal static async Task<SubscriptionIndependentCheckpointR52State> StartAsync(
        ClusterFixture fixture, SubscriptionFilterClusterSeed seed, CancellationToken token)
    {
        var state = new SubscriptionIndependentCheckpointR52State(seed);
        await SubscriptionIndependentCheckpointR52Callers.RunAsync(fixture, state, async (sdk, mcp, admin, official) =>
        {
            await SubscriptionIndependentCheckpointR52Current.CaptureAsync(state, admin, official, token);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            var claimed = await SubscriptionIndependentCheckpointR52Actions.ClaimAsync(state, seed.Independent,
                [1L, 2L, 3L], sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Refusals.WrongGroupAsync(state, claimed.Deliveries[0].Token,
                sdk, mcp, admin, official, token);
            state.OriginalRequest = state.Claims[0].Request;
            state.OriginalClaim = claimed;
            await SubscriptionIndependentCheckpointR52Actions.AckAsync(state, seed.Independent,
                claimed.Deliveries[2], 0, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Replay.RunAsync(state, sdk, mcp, admin, official, token);
        }, token);
        return state;
    }

    internal static async Task FirstColdAsync(ClusterFixture fixture,
        SubscriptionIndependentCheckpointR52State state, CancellationToken token)
    {
        state.PolicyEpoch = 3;
        state.PrimaryGeneration = 2;
        state.PrimaryCheckpoint = 4;
        state.PrimaryIssued = 4;
        state.PrimaryPaused = false;
        await SubscriptionIndependentCheckpointR52Callers.RunAsync(fixture, state, async (sdk, mcp, admin, official) =>
        {
            await SubscriptionIndependentCheckpointR52Current.CaptureAsync(state, admin, official, token);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Replay.RunAsync(state, sdk, mcp, admin, official, token);
            var old = state.OriginalClaim ?? throw new InvalidOperationException(SubscriptionIndependentCheckpointR52Protocol.MissingState);
            await SubscriptionIndependentCheckpointR52Refusals.OldTokenAsync(state, old.Deliveries[0].Token,
                ErrorCode.PermissionDenied, SubscriptionIndependentCheckpointR52Protocol.ChangedDeliveryDetail,
                sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Repair.RunAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Refusals.OldTokenAsync(state, old.Deliveries[0].Token,
                ErrorCode.TokenInvalidated, SubscriptionIndependentCheckpointR52Protocol.ScopeDetail,
                sdk, mcp, admin, official, token);
            var current = await SubscriptionIndependentCheckpointR52Actions.ClaimAsync(state, state.Seed.Independent,
                [1L, 2L, 3L], sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Actions.AckAsync(state, state.Seed.Independent,
                current.Deliveries[2], 0, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Replay.RunAsync(state, sdk, mcp, admin, official, token);
        }, token);
    }

    internal static async Task SecondColdAsync(ClusterFixture fixture,
        SubscriptionIndependentCheckpointR52State state, CancellationToken token)
    {
        state.PrimaryCheckpoint = 5;
        state.PrimaryIssued = 5;
        state.Tail = 5;
        await SubscriptionIndependentCheckpointR52Callers.RunAsync(fixture, state, async (sdk, mcp, admin, official) =>
        {
            await SubscriptionIndependentCheckpointR52Current.CaptureAsync(state, admin, official, token);
            await SubscriptionIndependentCheckpointR52Current.RunAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Replay.RunAsync(state, sdk, mcp, admin, official, token);
            var current = state.Claims.Last(x => x.Epoch == 3 && x.Request.Subscription == state.Seed.Independent).Result;
            await SubscriptionIndependentCheckpointR52Actions.AckAsync(state, state.Seed.Independent,
                current.Deliveries[0], 1, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Actions.AckAsync(state, state.Seed.Independent,
                current.Deliveries[1], 3, sdk, mcp, admin, official, token);
            var followers = await SubscriptionIndependentCheckpointR52Actions.ClaimAsync(state, state.Seed.Independent,
                [4L, 5L], sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Actions.AckAsync(state, state.Seed.Independent,
                followers.Deliveries[1], 3, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Actions.AckAsync(state, state.Seed.Independent,
                followers.Deliveries[0], 5, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Actions.SharedHealthyAsync(state, sdk, mcp, admin, official, token);
            await SubscriptionIndependentCheckpointR52Replay.RunAsync(state, sdk, mcp, admin, official, token);
        }, token);
    }
}
