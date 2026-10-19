using KeyLoad.Clustering;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.OrleansExecution.CommandObservedPolicyProtocol;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class CommandObservedPolicyFlow
{
    internal static async Task RunAsync(bool header, CancellationToken runner)
    {
        var scenario = new CommandObservedPolicyScenario(header);
        var failures = new List<Exception>();
        var initialized = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await scenario.InitializeAsync(runner);
            initialized = true;
        }, failures);
        if (initialized)
        {
            await RunTimeoutAsync(scenario, failures, runner);
        }
        else
        {
            await ServerFailureObserver.ObserveAsync(() => CleanupAsync(scenario, null, false, failures), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunTimeoutAsync(CommandObservedPolicyScenario scenario,
        List<Exception> failures, CancellationToken runner)
    {
        var acquired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var timeout = new CancellationTokenSource(scenario.Fixture.Timing.CompletionTimeout, TimeProvider.System);
            acquired = true;
            await RunOperationAsync(scenario, failures, timeout.Token, runner);
        }, failures);
        if (!acquired)
        {
            await ServerFailureObserver.ObserveAsync(() => CleanupAsync(scenario, null, false, failures), failures);
        }
    }

    private static async Task RunOperationAsync(CommandObservedPolicyScenario scenario,
        List<Exception> failures, CancellationToken timeout, CancellationToken runner)
    {
        var acquired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(timeout, runner);
            acquired = true;
            Task<GrainOperationReply>? original = null;
            var observed = false;
            try
            {
                (original, observed) = await ExecuteAsync(scenario, failures, operation.Token);
            }
            finally
            {
                await ServerFailureObserver.ObserveAsync(() => CleanupAsync(scenario, original, observed, failures), failures);
            }
        }, failures);
        if (!acquired)
        {
            await ServerFailureObserver.ObserveAsync(() => CleanupAsync(scenario, null, false, failures), failures);
        }
    }

    private static async Task<(Task<GrainOperationReply>? Original, bool Observed)> ExecuteAsync(
        CommandObservedPolicyScenario scenario, List<Exception> failures, CancellationToken token)
    {
        Task<GrainOperationReply>? original = null;
        var observed = false;
        try
        {
            var request = scenario.Receive();
            var requestId = Guid.NewGuid();
            var gate = scenario.Observation.Hold(requestId, GrainRequestPhase.SubmitReturned);
            original = CommandObservedPolicyCalls.ReceiveAsync(scenario, requestId, request, token);
            await gate.Arrived.Task.WaitAsync(token);
            await scenario.Fixture.Database.Coordinator.ReadBarrierAsync(token);
            var retained = CommandObservedPolicyAssertions.Outcome(scenario, request.RequestId);
            await Assert.That(Errors.Code(retained.Problem)).IsNull();
            var initial = retained.Get<ReceiveResult>();
            var retainedBytes = CommandObservedPolicyAssertions.OutcomeBytes(scenario, request.RequestId);
            var payloadBytes = CommandObservedPolicyAssertions.RuntimePayloadBytes(scenario, initial);
            await CommandObservedPolicyAssertions.LiteralAsync(scenario, request, initial, P.First);
            await scenario.PolicyAsync(repaired: false, token);
            await CommandObservedPolicyAssertions.UnchangedAsync(scenario, request.RequestId, async () =>
            {
                gate.Release.TrySetResult();
                var refused = await original;
                observed = true;
                await Assert.That(refused.Error).IsEqualTo(ErrorCode.PermissionDenied);
                await Assert.That(refused.SafeDetail).IsEqualTo(P.Refusal);
                await Assert.That(refused.Payload.IsEmpty).IsTrue();
            }, token);
            await scenario.PolicyAsync(repaired: true, token);
            await RepairedAsync(scenario, request, initial, token);
            await ConservedAsync(scenario, request, initial, retainedBytes, payloadBytes);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        return (original, observed);
    }

    private static async Task RepairedAsync(CommandObservedPolicyScenario scenario, ReceiveRequest request,
        ReceiveResult initial, CancellationToken token)
    {
        var next = scenario.Receive();
        var healthy = await CommandObservedPolicyCalls.ReceiveAsync(scenario, Guid.NewGuid(), next, token);
        await scenario.Fixture.Database.Coordinator.ReadBarrierAsync(token);
        await CommandObservedPolicyAssertions.LiteralAsync(scenario, next,
            CommandObservedPolicyAssertions.Value<ReceiveResult>(healthy), P.Second);
        await scenario.Fixture.Database.Coordinator.ReadBarrierAsync(token);
        var replay = await CommandObservedPolicyCalls.ReceiveAsync(scenario, Guid.NewGuid(), request, token);
        await CommandObservedPolicyAssertions.EqualAsync(initial,
            CommandObservedPolicyAssertions.Value<ReceiveResult>(replay));
        await scenario.Fixture.Database.Coordinator.ReadBarrierAsync(token);
    }

    private static async Task ConservedAsync(CommandObservedPolicyScenario scenario, ReceiveRequest request,
        ReceiveResult initial, byte[] retainedBytes, byte[] payloadBytes)
    {
        await Assert.That(CommandObservedPolicyAssertions.OutcomeBytes(scenario, request.RequestId)
            .SequenceEqual(retainedBytes)).IsTrue();
        await Assert.That(CommandObservedPolicyAssertions.RuntimePayloadBytes(scenario, initial)
            .SequenceEqual(payloadBytes)).IsTrue();
    }

    private static async Task CleanupAsync(CommandObservedPolicyScenario scenario, Task<GrainOperationReply>? original,
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
            if (failures.Count != 0)
            {
                scenario.Fixture.RetainRoots();
            }
            await ServerFailureObserver.ObserveAsync(() => scenario.Fixture.DisposeAsync().AsTask(), failures);
        }
    }

}
