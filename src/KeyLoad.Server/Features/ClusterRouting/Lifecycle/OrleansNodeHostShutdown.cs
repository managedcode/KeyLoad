using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OrleansNodeHostShutdown
{
    internal static async Task JoinAsync(IHost stopping, PartitionHost partition,
        NativeRequestWorkOwner requestWork, ServerRuntimeOptions runtimeOptions, TimeProvider runtimeClock,
        Action clearGrains, Action clearHost, List<Exception> failures)
    {
        var shutdownTimeout = runtimeOptions.Membership.Value.ShutdownTimeout;
        using var deadline = new CancellationTokenSource(shutdownTimeout, runtimeClock);
        await ServerFailureObserver.ObserveAsync(requestWork.DrainAsync, failures).ConfigureAwait(false);
        if (!requestWork.IsJoined)
        {
            ServerFailureObserver.ThrowIfAny(failures);
            throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RequestWorkNotJoined);
        }
        await JoinProducerDisposalAsync(stopping.Services, failures).ConfigureAwait(false);
        await ObserveOriginalHostStopAsync(stopping, failures, deadline.Token).ConfigureAwait(false);
        stopping.Services.GetRequiredService<RuntimeJournalAdmission>().Close();
        clearGrains();
        // A failed native Start has no lifecycle rollback. Drain the endpoint while its transport still exists.
        ServerFailureObserver.Observe(() => stopping.Services.GetService<ReplicaSiloDiscoveryState>()?.StopDiscovery(), failures);
        await ServerFailureObserver.ObserveAsync(() => partition.Consensus.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        clearHost();
        if (stopping is IAsyncDisposable asynchronous)
        {
            await ServerFailureObserver.ObserveAsync(() => asynchronous.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        else
        {
            ServerFailureObserver.Observe(stopping.Dispose, failures);
        }
    }

    private const string NativeColdPrefix = "KEYLOAD_NATIVE_COLD";
    private const string NativeColdNode = "KeyLoad.NativeCold.Node";
    private const string NativeColdStage = "KeyLoad.NativeCold.Stage";
    private const string NativeColdTask = "KeyLoad.NativeCold.OriginalTaskId";
    private const string NativeColdStatus = "KeyLoad.NativeCold.OriginalStatusAtCapture";
    private const string NativeColdRequested = "KeyLoad.NativeCold.NativeMembershipRequested";
    private const string NativeColdMatches = "KeyLoad.NativeCold.MatchesNativeMembershipToken";
    private const string NodeField = " node=";
    private const string StageField = " stage=";
    private const string PhaseField = " phase=";
    private const string TaskField = " task=";
    private const string StatusField = " status=";
    private const string RequestedField = " nativeMembershipRequested=";
    private const string CanceledField = " canceled=";
    private const string MatchesField = " matchesNativeMembership=";
    private const int UnknownFixtureNode = -1;
    private const int NativeHostStopStage = 10;
    private const int MissingTaskId = 0;
    private const int MissingTaskStatus = -1;
    private const int CapturePhase = 0;

    private static async Task ObserveOriginalHostStopAsync(IHost stopping,
        List<Exception> failures, CancellationToken originalCancellation)
    {
        Task? original = null;
        var captured = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() =>
        {
            original = stopping.StopAsync(originalCancellation);
            return original;
        }, captured).ConfigureAwait(false);
        failures.AddRange(captured);
        var taskId = original?.Id ?? MissingTaskId;
        var status = original is null ? MissingTaskStatus : (int)original.Status;
        var requested = originalCancellation.IsCancellationRequested;
        foreach (var error in captured)
        {
            await ServerFailureObserver.ObserveAsync(() => CaptureOriginalHostStopAsync(error,
                taskId, status, requested, originalCancellation), failures).ConfigureAwait(false);
        }
    }

    private static Task CaptureOriginalHostStopAsync(Exception error, int taskId,
        int status, bool requested, CancellationToken originalCancellation)
    {
        var canceled = error as OperationCanceledException;
        var matches = canceled is not null && canceled.CancellationToken == originalCancellation;
        if (!error.Data.Contains(NativeColdStage))
        {
            error.Data[NativeColdNode] = UnknownFixtureNode;
            error.Data[NativeColdStage] = NativeHostStopStage;
            error.Data[NativeColdTask] = taskId;
            error.Data[NativeColdStatus] = status;
            error.Data[NativeColdRequested] = requested;
            error.Data[NativeColdMatches] = matches;
        }
        return Console.Error.WriteLineAsync(FormattableString.Invariant($"{NativeColdPrefix}{NodeField}{UnknownFixtureNode}{StageField}{NativeHostStopStage}{PhaseField}{CapturePhase}{TaskField}{taskId}{StatusField}{status}{RequestedField}{requested}{CanceledField}{canceled is not null}{MatchesField}{matches}"));
    }

    private static async Task JoinProducerDisposalAsync(IServiceProvider services, List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (services.GetService<RequestCqrsProbeNativeRelay>() is { } relay)
            {
                await relay.StopAsync().ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (services.GetService<RequestCqrsProbeObserver>() is { } observer)
            {
                await observer.DisposeAsync().ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
    }
}
