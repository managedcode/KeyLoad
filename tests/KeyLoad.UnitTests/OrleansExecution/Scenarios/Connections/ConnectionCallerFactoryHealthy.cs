using KeyLoad.UnitTests.Features.ClientApi;
using D = KeyLoad.UnitTests.Features.ClientApi.DeclaredResponseEofProtocol;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class ConnectionCallerFactoryHealthy
{
    internal static async Task RunAsync(ConnectionIdentityCollisionState state, ConnectionCallerFactoryCalls calls,
        CommitReceipt first, byte[] originalOutcome, CancellationToken token)
    {
        var scenario = state.Scenario;
        var freshRequest = Guid.NewGuid();
        await Assert.That(freshRequest).IsNotEqualTo(state.OriginalRequest);
        var placement = scenario.Fixture.Database.Database.ReadAtomicPartitionPlacement(D.Root,
            state.Following.Partition);
        var actual = calls.Start(state.Second, freshRequest, state.Following, token);
        var reply = await state.Tasks.CompleteAsync(actual);
        await scenario.Observation.Read(freshRequest).Disposed.Task.WaitAsync(token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var expected = await ConnectionIdentityCollisionSecond.RequireAsync(state, placement, first,
            ConnectionAssertions.Value<CommitReceipt>(reply));
        await ConnectionIdentityCollisionAssertions.ProbeAsync(state, freshRequest, state.Second, state.Following);
        await ConnectionAssertions.SameOwnerAsync(scenario, state.OriginalRequest, freshRequest, token);
        var current = DeclaredResponseEofAssertions.Capture(scenario, state.Second, state.Following);
        await CurrentAsync(state, current, originalOutcome);
        await ReplayAsync(state, calls, state.Second, state.Following, expected, current, originalOutcome, token);
        await ReplayAsync(state, calls, state.First, state.Original, first, current, originalOutcome, token);
    }

    private static async Task ReplayAsync(ConnectionIdentityCollisionState state, ConnectionCallerFactoryCalls calls,
        PrincipalRecord principal, CommandRequest command, CommitReceipt expected, DeclaredResponseEofCut current,
        byte[] originalOutcome, CancellationToken token)
    {
        var requestId = Guid.NewGuid();
        var actual = calls.Start(principal, requestId, command, token);
        var reply = await state.Tasks.CompleteAsync(actual);
        await state.Scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        await DeclaredResponseEofAssertions.EqualAsync(expected, ConnectionAssertions.Value<CommitReceipt>(reply));
        await ConnectionIdentityCollisionAssertions.ProbeAsync(state, requestId, principal, command);
        await ConnectionAssertions.SameOwnerAsync(state.Scenario, state.OriginalRequest, requestId, token);
        await CurrentAsync(state, current, originalOutcome);
    }

    private static async Task CurrentAsync(ConnectionIdentityCollisionState state, DeclaredResponseEofCut current,
        byte[] originalOutcome)
    {
        await ConnectionIdentityCollisionSecond.DocumentsAsync(state);
        await DeclaredResponseEofAssertions.CurrentAsync(state.Scenario, state.Second, state.Following, current);
        var original = DeclaredResponseEofAssertions.Capture(state.Scenario, state.First, state.Original);
        await Assert.That(original.Outcome.SequenceEqual(originalOutcome)).IsTrue();
    }
}
