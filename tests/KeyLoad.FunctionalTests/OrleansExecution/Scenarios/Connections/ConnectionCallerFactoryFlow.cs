using KeyLoad.FunctionalTests.ClientApi;
using KeyLoad.Server;
using D = KeyLoad.FunctionalTests.ClientApi.DeclaredResponseEofProtocol;
using N = KeyLoad.FunctionalTests.OrleansExecution.ConnectionIdentityCollisionProtocol;
using P = KeyLoad.FunctionalTests.OrleansExecution.ConnectionCallerFactoryProtocol;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class ConnectionCallerFactoryFlow
{
    internal static Task RunAsync()
        => ConnectionScenario.RunAsync(ExecuteAsync);

    private static async Task ExecuteAsync(ConnectionScenario scenario)
    {
        using var deadline = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        var token = linked.Token;
        await scenario.InitializeAsync(token);
        var state = new ConnectionIdentityCollisionState(scenario, scenario.Principal(), scenario.Principal(),
            scenario.Command(D.Document, D.OriginalJson, D.AbsentRevision),
            scenario.Command(N.SecondDocument, N.SecondJson, D.AbsentRevision), Guid.NewGuid());
        await Assert.That(state.First.Id).IsNotEqualTo(state.Second.Id);
        await Assert.That(state.Original.CommandId).IsNotEqualTo(state.Following.CommandId);
        var gate = scenario.Observation.Hold(state.OriginalRequest);
        var failures = new List<Exception>();
        await using (var calls = new ConnectionCallerFactoryCalls(state, gate, failures, token))
        {
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(state, calls, gate, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(ConnectionIdentityCollisionState state,
        ConnectionCallerFactoryCalls calls, ConnectionOperationGate gate, CancellationToken token)
    {
        var scenario = state.Scenario;
        var placement = scenario.Fixture.Database.Database.ReadAtomicPartitionPlacement(D.Root,
            state.Original.Partition);
        var actual = calls.Start(state.First, state.OriginalRequest, state.Original, token);
        await gate.Arrived.Task.WaitAsync(token);
        await Assert.That(actual.IsCompleted).IsFalse();
        var observed = scenario.Observation.Read(state.OriginalRequest);
        var before = ConnectionIdentityCollisionAssertions.Capture(state);
        await ConnectionIdentityCollisionAssertions.HeldAsync(state, before, observed);
        var losingId = Guid.NewGuid();
        await Assert.That(losingId).IsNotEqualTo(state.OriginalRequest);
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            calls.Registry.Retain(state.OriginalRequest, () => calls.CreateDuplicate(losingId, token)));
        await Assert.That(failure.Message).IsEqualTo(P.Duplicate);
        await Assert.That(calls.DuplicateInvocations).IsEqualTo(P.NoInvocations);
        await Assert.That(calls.DuplicateCreated is null).IsTrue();
        var missing = Assert.ThrowsExactly<InvalidOperationException>(() => scenario.Observation.Read(losingId));
        await Assert.That(missing.Message).IsEqualTo(P.Missing);
        await Assert.That(actual.IsCompleted).IsFalse();
        await ConnectionIdentityCollisionAssertions.HeldAsync(state, before, observed);
        gate.Release.TrySetResult();
        var reply = await state.Tasks.CompleteAsync(actual);
        await observed.Disposed.Task.WaitAsync(token);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var first = await DeclaredResponseEofAssertions.FirstAsync(scenario, state.First, state.Original,
            ConnectionAssertions.Value<CommitReceipt>(reply), placement);
        var firstCut = DeclaredResponseEofAssertions.Capture(scenario, state.First, state.Original);
        await ConnectionCallerFactoryHealthy.RunAsync(state, calls, first, firstCut.Outcome, token);
    }
}
