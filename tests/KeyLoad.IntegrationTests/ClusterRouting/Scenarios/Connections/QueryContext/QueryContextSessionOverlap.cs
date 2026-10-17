using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class QueryContextSessionOverlap
{
    private const int CSharpRoute = 2;

    private const string MissingSelected = "The original SQL query context was not captured.";
    private const string MissingSibling = "The original AST query context was not captured.";
    private const string MissingSiblingPage = "The original successful sibling page is unavailable.";

    internal static async Task<ConnectionQueryContextRead> RunAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state,
        ConnectionRf3Caller caller, CancellationToken token)
    {
        var failures = new List<Exception>();
        ConnectionQueryContextRead? completed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var operations = new QueryContextSessionOperations(scenario, caller, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await operations.StartAsync(state);
                var first = operations.First ?? throw new InvalidOperationException(MissingSelected);
                var second = operations.Second ?? throw new InvalidOperationException(MissingSibling);
                await ConnectionQueryContextPages.SdkAsync(scenario, state, caller.Sdk, state.First, first.Witness, token);
                await Assert.That(first.Original.IsCompleted).IsFalse();
                await Assert.That(second.Original.IsCompleted).IsFalse();
                await QueryContextSessionRefusals.RunAsync(scenario, state, caller.WithCredential(state.Second.Identity.Secret), first.Witness, token);
                await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, operations.CancelSelectedAsync, token);
                await Assert.That(second.Original.IsCompleted).IsFalse();
                await ConnectionQueryContextPages.SdkAsync(scenario, state, caller.Sdk, state.First, second.Witness, token);
                await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
                {
                    completed = await operations.ReleaseSiblingAsync();
                    await ConnectionQueryContextCurrent.PageAsync(completed.Page, state.First, true, null);
                }, token);
                await FollowingAsync(scenario, state, caller, completed!, token);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return completed ?? throw new InvalidOperationException(MissingSiblingPage);
    }
    private static async Task FollowingAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state, ConnectionRf3Caller caller,
        ConnectionQueryContextRead original, CancellationToken token)
        => await ConnectionQueryContextCurrent.ObserveAsync(scenario, state, async () =>
        {
            var following = await ConnectionQueryContextCalls.SuccessAsync(scenario, caller.Sdk, state.First.Identity,
                CSharpRoute, original.Page.Cursor, token);
            await ConnectionQueryContextCurrent.PageAsync(following.Page, state.First, false, null, original.Page.CutPosition);
            await ConnectionRf3WitnessReader.SameOwnerAsync(original.Witness, following.Witness);
        }, token);
}
