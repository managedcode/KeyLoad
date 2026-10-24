using KeyLoad.Clustering;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class RequestDrainLifecycleFlow
{
    private const int NoFailures = 0;
    internal static async Task RunAsync(bool canceledStop, bool callbackFailure, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var clock = TimeProvider.System;
            var requests = new RequestWorkOwner(UnitRoutingOptions.Routing());
            RequestDrainLifecycleState state;
            try
            {
                state = new(requests, clock, failures, callbackFailure, token);
            }
            catch (Exception primary)
            {
                var openingFailures = new List<Exception> { primary };
                await ServerFailureObserver.ObserveAsync(() => requests.DisposeAsync().AsTask(), openingFailures);
                ServerFailureObserver.ThrowIfAny(openingFailures);
                throw;
            }
            using (state)
            {
                await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(state, canceledStop), failures);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(RequestDrainLifecycleState state, bool canceledStop)
    {
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var provider = Register(state);
            await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(state, provider, canceledStop), state.Failures);
            state.Release.TrySetResult();
            await JoinAsync(state, state.Failures);
        }, state.Failures);
        state.Release.TrySetResult();
        await ServerFailureObserver.ObserveAsync(() =>
        {
            state.Verification = RequestDrainLifecycleOperation.DisposeAndVerifyAsync(state);
            return state.Verification;
        }, state.Failures);
        if (state.Failures.Count == NoFailures)
        {
            await ServerFailureObserver.ObserveAsync(() => RequestDrainLifecycleOperation.ContinuedAsync(state), state.Failures);
        }
    }

    private static ServiceProvider Register(RequestDrainLifecycleState state)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Func<RequestWorkOwner>>(() => state.Requests);
        RequestDrainRegistration.Register(services);
        var provider = services.BuildServiceProvider();
        return provider;
    }

    private static async Task ExecuteAsync(RequestDrainLifecycleState state, ServiceProvider provider, bool canceledStop)
    {
        foreach (var participant in provider.GetServices<ILifecycleParticipant<ISiloLifecycle>>())
        {
            participant.Participate(state.Lifecycle);
        }
        await RequestDrainLifecycleOperation.SeedAsync(state);
        state.Lifecycle.Subscribe(RequestDrainLifecycleProtocol.ActiveObserver, ServiceLifecycleStage.Active,
            _ => Task.CompletedTask, _ => ServerFailureObserver.ObserveAsync(() => RequestDrainLifecycleOperation.ActiveAsync(state), state.Failures));
        state.Lifecycle.Subscribe(RequestDrainLifecycleProtocol.StorageObserver, ServiceLifecycleStage.RuntimeStorageServices,
            _ => Task.CompletedTask, _ => ServerFailureObserver.ObserveAsync(() => RequestDrainLifecycleOperation.StorageAsync(state), state.Failures));
        state.Lifecycle.Subscribe(nameof(ServiceLifecycleStage.ApplicationServices), ServiceLifecycleStage.ApplicationServices,
            _ => Task.CompletedTask, _ => Task.CompletedTask);
        state.Lifecycle.Subscribe(nameof(ServiceLifecycleStage.ApplicationServices) + nameof(ServiceLifecycleStage.Active),
            ServiceLifecycleStage.ApplicationServices + 1, _ => Task.CompletedTask, _ => Task.CompletedTask);
        await state.Lifecycle.OnStart(state.Token);
        state.Producer = RequestDrainLifecycleOperation.ProduceAsync(state);
        await state.Entered.Task.WaitAsync(state.Timing.CompletionTimeout, state.Clock, state.Token);
        using var callback = state.ExpectsFailure
            ? state.Requests.ShutdownToken.Register(() =>
            {
                throw state.CallbackFailure;
            }) : default;
        using var cancellation = new CancellationTokenSource();
        if (canceledStop)
        {
            await cancellation.CancelAsync();
        }
        state.Stopping = state.Lifecycle.OnStop(cancellation.Token);
        await state.Canceled.Task.WaitAsync(state.Timing.CompletionTimeout, state.Clock, state.Token);
        await Assert.That(state.Stopping.IsCompleted).IsFalse();
        await Assert.That(state.Requests.IsJoined).IsFalse();
        await Assert.That(state.Lifecycle.LowestStoppedStage).IsEqualTo(ServiceLifecycleStage.ApplicationServices + 1);
        await RequestDrainLifecycleOperation.ClosedAsync(state);
    }

    private static async Task JoinAsync(RequestDrainLifecycleState state, List<Exception> failures)
    {
        var stopping = state.Stopping ??= state.Lifecycle.OnStop();
        var drain = state.Drain ??= state.Requests.DrainAsync();
        await ServerFailureObserver.ObserveAsync(() => state.Producer ?? Task.CompletedTask, failures);
        await ServerFailureObserver.ObserveAsync(() => stopping, failures);
        await ServerFailureObserver.ObserveAsync(() => drain, state.DrainFailures);
        state.DrainObserved = true;
    }
}
