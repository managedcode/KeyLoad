using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Injects only a controlled real-delivery return-edge failure, never a fabricated transport reply.</summary>
internal static class CommandObservedReturnEdgeFlow
{
    internal static async Task RunAsync(bool business, CancellationToken runner)
    {
        var scenario = new CommandObservedPolicyScenario(header: false);
        var failures = new List<Exception>();
        Task<GrainOperationReply>? original = null;
        CancellationTokenSource? deadline = null;
        CancellationTokenSource? operation = null;
        CancellationTokenSource? lifetime = null;
        var observed = false;
        try
        {
            await scenario.InitializeAsync(runner);
            deadline = new(scenario.Fixture.Timing.CompletionTimeout, TimeProvider.System);
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(runner, deadline.Token);
            operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            var before = await CommandObservedReturnEdgeSnapshot.CaptureAsync(scenario, operation.Token);
            var request = scenario.Receive();
            var requestId = Guid.NewGuid();
            var phase = business ? GrainRequestPhase.OriginalBusinessReplyReturned : GrainRequestPhase.OriginalClaimReturned;
            var gate = scenario.Observation.Hold(requestId, phase, fail: true);
            original = CommandObservedPolicyCalls.ReceiveAsync(scenario, requestId, request, operation.Token);
            await gate.Arrived.Task.WaitAsync(operation.Token);
            var refused = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => { _ = await original; });
            observed = true;
            await Assert.That(refused.Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await Assert.That(refused.Message).IsEqualTo(business
                ? CommandObservedReturnEdgeProtocol.Unknown : CommandObservedReturnEdgeProtocol.ClaimUnavailable);
            await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(operation.Token);
            await operation.CancelAsync();
            await VerifyAndRetryAsync(scenario, request, business, before, lifetime.Token);
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        await CloseAsync(scenario, original, observed, failures);
        if (operation is not null)
        {
            ServerFailureObserver.Observe(operation.Dispose, failures);
        }
        if (lifetime is not null)
        {
            ServerFailureObserver.Observe(lifetime.Dispose, failures);
        }
        if (deadline is not null)
        {
            ServerFailureObserver.Observe(deadline.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyAndRetryAsync(CommandObservedPolicyScenario scenario, ReceiveRequest request,
        bool business, CommandObservedReturnEdgeSnapshot before, CancellationToken token)
    {
        ReceiveResult expected;
        if (business)
        {
            expected = await CommandObservedReturnEdgeAssertions.OriginalAsync(scenario, request, token);
            await Assert.That(expected.Token.Position).IsEqualTo(checked(before.Journal.LastIndex
                + CommandObservedPolicyProtocol.Step));
            var cut = scenario.Fixture.Database.ReadJournalCut();
            await Assert.That(cut.LastIndex).IsEqualTo(expected.Token.Position);
            await Assert.That(cut.CommittedIndex).IsEqualTo(expected.Token.Position);
            await Assert.That(scenario.Fixture.Database.Database.LastApplied).IsEqualTo(expected.Token.Position);
        }
        else
        {
            await CommandObservedReturnEdgeAssertions.NoOutcomeAsync(scenario, request, before, token);
            var retry = await CommandObservedPolicyCalls.ReceiveAsync(scenario, Guid.NewGuid(), request, token);
            expected = CommandObservedPolicyAssertions.Value<ReceiveResult>(retry);
            await scenario.Fixture.Database.Coordinator.ReadBarrierAsync(token);
            await CommandObservedPolicyAssertions.LiteralAsync(scenario, request, expected,
                CommandObservedPolicyProtocol.First);
        }
        await CommandObservedReturnEdgeAssertions.ReplayAndNextAsync(scenario, request, expected, token);
    }

    private static async Task CloseAsync(CommandObservedPolicyScenario scenario, Task<GrainOperationReply>? original,
        bool observed, List<Exception> failures)
    {
        scenario.Observation.ReleaseAll();
        if (original is not null && !observed)
        {
            await ServerFailureObserver.ObserveAsync(async () => { _ = await original; }, failures);
        }
        if (scenario.Fixture is not null)
        {
            var source = scenario.Fixture.Source;
            if (failures.Count != 0)
            {
                scenario.Fixture.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => scenario.Fixture.DisposeAsync().AsTask(), failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(source.IsJoined).IsTrue();
                await Assert.That(source.Silo.HasJoinedRequestWork).IsTrue();
            }, failures);
        }
    }
}
