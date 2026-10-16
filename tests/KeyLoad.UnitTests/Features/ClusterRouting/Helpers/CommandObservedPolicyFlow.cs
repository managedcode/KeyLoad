using KeyLoad.Clustering;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ClusterRouting.CommandObservedPolicyProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class CommandObservedPolicyFlow
{
    internal static async Task RunAsync(bool header, CancellationToken runner)
    {
        var scenario = new CommandObservedPolicyScenario(header);
        var failures = new List<Exception>();
        Task<GrainOperationReply>? original = null;
        var observed = false;
        CancellationTokenSource? timeout = null;
        CancellationTokenSource? operation = null;
        try
        {
            await scenario.InitializeAsync(runner);
            timeout = new CancellationTokenSource(scenario.Fixture.Timing.CompletionTimeout, TimeProvider.System);
            operation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, runner);
            var token = operation.Token;
            var request = scenario.Receive();
            var requestId = Guid.NewGuid();
            var gate = scenario.Observation.Hold(requestId, GrainRequestPhase.SubmitReturned);
            original = CommandObservedPolicyCalls.ReceiveAsync(scenario, requestId, request, token);
            await gate.Arrived.Task.WaitAsync(token);
            await scenario.Fixture.Database.Coordinator.ReadBarrierAsync(token);
            var retained = CommandObservedPolicyAssertions.Outcome(scenario, request.RequestId);
            await Assert.That(retained.Error).IsNull();
            var initial = retained.Get<ReceiveResult>();
            var retainedBytes = CommandObservedPolicyAssertions.OutcomeBytes(scenario, request.RequestId);
            var payloadBytes = CommandObservedPolicyAssertions.NativePayloadBytes(scenario, initial);
            await CommandObservedPolicyAssertions.LiteralAsync(scenario, request, initial, P.First);
            await scenario.PolicyAsync(repaired: false, token);
            await CommandObservedPolicyAssertions.UnchangedAsync(scenario, request.RequestId, async () =>
            {
                gate.Release.TrySetResult();
                var refused = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
                {
                    _ = await original;
                });
                observed = true;
                await Assert.That((refused ?? throw new InvalidOperationException(nameof(refused))).Code).IsEqualTo(ErrorCode.PermissionDenied);
                await Assert.That(refused.Message).IsEqualTo(P.Refusal);
            }, token);
            await scenario.PolicyAsync(repaired: true, token);
            await RepairedAsync(scenario, request, initial, token);
            await ConservedAsync(scenario, request, initial, retainedBytes, payloadBytes);
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        finally
        {
            await ServerFailureObserver.ObserveAsync(() =>
                CleanupAsync(scenario, original, observed, failures), failures);
            DisposeCancellation(operation, failures);
            DisposeCancellation(timeout, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
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
        await Assert.That(CommandObservedPolicyAssertions.NativePayloadBytes(scenario, initial)
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

    private static void DisposeCancellation(CancellationTokenSource? owner, List<Exception> failures)
    {
        try
        {
            owner?.Dispose();
        }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            failures.Add(cleanup);
        }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        {
            failures.Add(cleanup);
        }
    }
}
