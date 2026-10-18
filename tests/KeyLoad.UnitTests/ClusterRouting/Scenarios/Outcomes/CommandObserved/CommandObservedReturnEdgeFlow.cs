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
        var executionEntered = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await scenario.InitializeAsync(runner);
            executionEntered = true;
            await RunDeadlineAsync(scenario, business, failures, runner);
        }, failures);
        if (!executionEntered)
        {
            await ServerFailureObserver.ObserveAsync(() => CloseAsync(scenario, null, false, failures), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunDeadlineAsync(CommandObservedPolicyScenario scenario, bool business,
        List<Exception> failures, CancellationToken runner)
    {
        var acquired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new CancellationTokenSource(scenario.Fixture.Timing.CompletionTimeout, TimeProvider.System);
            acquired = true;
            await RunOwnedAsync(scenario, business, failures, runner, deadline.Token);
        }, failures);
        if (!acquired)
        {
            await ServerFailureObserver.ObserveAsync(() => CloseAsync(scenario, null, false, failures), failures);
        }
    }

    private static async Task RunOwnedAsync(CommandObservedPolicyScenario scenario, bool business,
        List<Exception> failures, CancellationToken runner, CancellationToken deadline)
    {
        var acquired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(runner, deadline);
            acquired = true;
            await RunOperationAsync(scenario, business, failures, lifetime.Token);
        }, failures);
        if (!acquired)
        {
            await ServerFailureObserver.ObserveAsync(() => CloseAsync(scenario, null, false, failures), failures);
        }
    }

    private static async Task RunOperationAsync(CommandObservedPolicyScenario scenario, bool business,
        List<Exception> failures, CancellationToken lifetime)
    {
        Task<GrainOperationReply>? original = null;
        var observed = false;
        var acquired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            acquired = true;
            try
            {
                (original, observed) = await ExecuteAsync(scenario, business, operation, failures, lifetime);
            }
            finally
            {
                await ServerFailureObserver.ObserveAsync(() => CloseAsync(scenario, original, observed, failures), failures);
            }
        }, failures);
        if (!acquired)
        {
            await ServerFailureObserver.ObserveAsync(() => CloseAsync(scenario, null, false, failures), failures);
        }
    }

    private static async Task<(Task<GrainOperationReply>? Original, bool Observed)> ExecuteAsync(
        CommandObservedPolicyScenario scenario, bool business, CancellationTokenSource operation,
        List<Exception> failures, CancellationToken token)
    {
        Task<GrainOperationReply>? original = null;
        var observed = false;
        try
        {
            var before = await CommandObservedReturnEdgeSnapshot.CaptureAsync(scenario, operation.Token);
            var request = scenario.Receive();
            var requestId = Guid.NewGuid();
            var phase = business ? GrainRequestPhase.OriginalBusinessReplyReturned : GrainRequestPhase.OriginalClaimReturned;
            var gate = scenario.Observation.Hold(requestId, phase, fail: true);
            original = CommandObservedPolicyCalls.ReceiveAsync(scenario, requestId, request, operation.Token);
            await gate.Arrived.Task.WaitAsync(operation.Token);
            var refused = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
            {
                _ = await original;
            });
            observed = true;
            await Assert.That((refused ?? throw new InvalidOperationException(nameof(refused))).Code).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await Assert.That(refused.Message).IsEqualTo(business
                ? CommandObservedReturnEdgeProtocol.Unknown : CommandObservedReturnEdgeProtocol.ClaimUnavailable);
            await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(operation.Token);
            await operation.CancelAsync();
            await VerifyAndRetryAsync(scenario, request, business, before, token);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        return (original, observed);
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
            var cut = scenario.Fixture.Database.ReadCanonicalCut();
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
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                _ = await original;
            }, failures);
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
