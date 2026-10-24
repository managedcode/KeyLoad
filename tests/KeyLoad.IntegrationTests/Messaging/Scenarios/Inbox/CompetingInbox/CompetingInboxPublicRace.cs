using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Server;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;
namespace KeyLoad.IntegrationTests.Messaging;

internal static class CompetingInboxPublicRace
{
    internal static async Task RunAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers sdk,
        RequestCqrsClusterCallers official, CancellationToken token)
    {
        var delivery = await CompetingInboxPublicSeed.ReceiveAsync(state, sdk, CompetingInboxPublicProtocol.First, token);
        state.Candidates = [state.Processing(delivery, CompetingInboxPublicProtocol.Effect, state.Json(CompetingInboxPublicProtocol.FirstCandidate)),
            state.Processing(delivery, CompetingInboxPublicProtocol.Effect, state.Json(CompetingInboxPublicProtocol.SecondCandidate))];
        var before = state.Clock.GetUtcNow();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = FirstAsync(state, sdk, release.Task, token);
        var second = SecondAsync(state, official, release.Task, token);
        var failures = new List<Exception>();
        release.TrySetResult();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var actual = await first;
            var other = await second;
            var officialSuccess = other.IsError is not true;
            if (state.Equivalent)
            {
                state.Winner = await McpCallerAssertions.SdkSuccessAsync(actual);
                await CompetingInboxPublicValues.EqualAsync(state.Winner,
                    (await McpCallerAssertions.SuccessAsync<CommitReceipt>(other)).Value);
            }
            else
            {
                await Assert.That(actual.IsSuccess != officialSuccess).IsTrue();
                state.Winner = actual.IsSuccess ? actual.Value!
                    : (await McpCallerAssertions.SuccessAsync<CommitReceipt>(other)).Value;
                if (actual.IsSuccess)
                {
                    await CompetingInboxPublicValues.McpFailureAsync(state, other, ErrorCode.Conflict);
                }
                else
                {
                    await CompetingInboxPublicValues.SdkFailureAsync(state, actual, ErrorCode.Conflict, CompetingInboxPublicProtocol.Conflict);
                }
            }
            state.WinnerIndex = Array.FindIndex(state.Candidates, candidate => candidate.CommandId == state.Winner.CommandId);
            await Assert.That(state.WinnerIndex).IsGreaterThanOrEqualTo(CompetingInboxPublicProtocol.FirstCandidate);
            await Assert.That(state.WinnerIndex).IsLessThan(CompetingInboxPublicProtocol.Competitors);
            await CompetingInboxPublicValues.ReceiptAsync(state, state.Winner, state.Winner.CommandId,
                CompetingInboxPublicValues.Effects(CompetingInboxPublicProtocol.First, CompetingInboxPublicProtocol.Effect));
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await first;
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await second;
        }, failures);
        state.EventWindows.Add(CompetingInboxPublicProtocol.Effect, (before, state.Clock.GetUtcNow()));
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task<Result<CommitReceipt>> FirstAsync(CompetingInboxPublicState state,
        RequestCqrsClusterCallers sdk, Task release, CancellationToken token)
    {
        await release.WaitAsync(token);
        return await sdk.Sdk.CommitProcessingAsync(state.Candidates[CompetingInboxPublicProtocol.FirstCandidate], token);
    }
    private static async Task<CallToolResult> SecondAsync(CompetingInboxPublicState state,
        RequestCqrsClusterCallers official, Task release, CancellationToken token)
    {
        await release.WaitAsync(token);
        return await official.Mcp.CallAsync(McpCallerTools.MessagesProcess,
            state.Candidates[CompetingInboxPublicProtocol.SecondCandidate], token);
    }
}
