using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeObserver : IGrainRequestPhaseObserver, IReplicaDiscoveryObservationSink, IAsyncDisposable
{
    private readonly Lock disposeSync = new();
    private readonly RequestCqrsProbeFiles files;
    private readonly RequestCqrsProbeLifecycle lifecycle;
    private readonly ReplicaConfiguration replica;
    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly string siloAddress;
    private readonly CancellationTokenSource stopping = new();
    private Task? shutdown;
    private readonly RequestProbeExecutionOptions settings;
    private readonly TimeProvider clock;

    internal RequestCqrsProbeObserver(RequestCqrsProbeFiles files, IOptions<ReplicaConfiguration> replicaOptions,
        string siloAddress, IHostApplicationLifetime applicationLifetime, IOptions<RequestProbeExecutionOptions> executionOptions, TimeProvider? clock = null)
    {
        replica = replicaOptions.Value;
        this.applicationLifetime = applicationLifetime;
        this.siloAddress = siloAddress;
        this.files = files;
        settings = executionOptions.Value;
        this.clock = clock ?? TimeProvider.System;
        lifecycle = new(executionOptions);
    }

    public ValueTask ObserveIncompatibleAsync(string voterId, ReplicaDiscoveryObservation observation,
        CancellationToken cancellationToken)
        => RequestCqrsProbeDiscoveryObservation.Record(files, replica, voterId, observation, cancellationToken);

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        lifecycle.EnterCallback();
        return new ValueTask(ObserveCoreAsync(identity, phase, cancellationToken));
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        // The ingress request grain may dispose a producer after its selected phase ran on another voter.
        var claim = lifecycle.FindClaim(identity.RequestId);
        if (claim is null)
        { return; }
        lifecycle.EnterClaimedCallback();
        try
        {
            if (claim.Identity != identity)
            { throw Invalid(); }
            files.RequireClaimArmActiveOrRetired(claim.Arm);
            files.WriteMarker(CreateMarker(claim, RequestCqrsProbePhase.ProducerDisposed, RequestCqrsProbeOutcome.Observed), claim.Arm);
        }
        finally
        {
            lifecycle.RemoveClaim(identity.RequestId, claim);
            lifecycle.ExitCallback();
        }
    }

    public ValueTask DisposeAsync()
    {
        TaskCompletionSource start;
        Task completion;
        lock (disposeSync)
        {
            if (shutdown is not null)
            { return new ValueTask(shutdown); }
            // OrleansNode drains admitted work before silo stop; this joins callbacks before disposing observer state.
            var join = lifecycle.StopAdmissionAndJoin();
            start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdown = DisposeAfterJoinAsync(start.Task, join);
            completion = shutdown;
        }
        start.SetResult();
        return new ValueTask(completion);
    }

    private async Task ObserveCoreAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        CancellationToken requestCancellation)
    {
        RequestCqrsProbeClaim? claim = null;
        var gate = false;
        try
        {
            claim = Claim(identity, phase, files.ReadSnapshot());
            if (claim is null)
            { return; }
            if (!TryGetPhase(phase, out var selectedPhase) || claim.Arm.Record.Phase != selectedPhase)
            { return; }
            files.RequireActiveArm(claim.Arm);
            if (claim.Arm.Record.Action == RequestCqrsProbeAction.Hold)
            {
                lifecycle.EnterGate();
                gate = true;
            }
            var outcome = gate ? RequestCqrsProbeOutcome.Observed : RequestCqrsProbeOutcome.FaultRequested;
            files.WriteMarker(CreateMarker(claim, selectedPhase, outcome));
            if (!gate)
            { ThrowOrdinary(); }
            await HoldAsync(claim, selectedPhase, requestCancellation).ConfigureAwait(true);
        }
        finally
        {
            if (gate)
            { lifecycle.ExitGate(); }
            lifecycle.ExitCallback();
        }
    }

    private RequestCqrsProbeClaim? Claim(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        RequestCqrsProbeSnapshot snapshot)
    {
        if (!TryGetPhase(phase, out _))
        { throw Invalid(); }
        var existing = lifecycle.FindClaim(identity.RequestId);
        if (existing is not null)
        {
            if (existing.Identity != identity)
            { throw Invalid(); }
            return existing;
        }
        var selected = RequestCqrsProbeClaimSelection.Find(identity, snapshot);
        if (selected is null)
        { return null; }
        // Retain the first validated request identity so the later disposal callback can join across voters.
        return lifecycle.AddClaim(identity, selected);
    }

    private static bool TryGetPhase(GrainRequestPhase phase, out RequestCqrsProbePhase selectedPhase)
        => Enum.TryParse(phase.ToString(), ignoreCase: false, out selectedPhase);

    private async Task HoldAsync(RequestCqrsProbeClaim claim, RequestCqrsProbePhase phase,
        CancellationToken requestCancellation)
    {
        const int ReleasesLengthValidationBoundary = 1;
        const int EmptyReleasesLength = 1;
        const int ReleasesFirstIndex = 0;

        var holdTimeout = settings.HoldTimeout;
        var pollInterval = settings.PollInterval;
        using var ceiling = new CancellationTokenSource(holdTimeout, clock);
        using var hostStop = CancellationTokenSource.CreateLinkedTokenSource(applicationLifetime.ApplicationStopping, stopping.Token);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation, hostStop.Token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Token, ceiling.Token);
        try
        {
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                var snapshot = files.ReadSnapshot();
                RequestCqrsProbeFiles.RequireActiveArm(claim.Arm, snapshot);
                var releases = snapshot.Releases.Where(record => record.ArmId == claim.Arm.Record.ArmId).ToArray();
                if (releases.Length > ReleasesLengthValidationBoundary || releases.Length == EmptyReleasesLength
                    && (releases[ReleasesFirstIndex].RequestId != claim.Identity.RequestId || releases[ReleasesFirstIndex].SessionId != claim.Arm.Record.SessionId))
                { throw Invalid(); }
                if (releases.Length == EmptyReleasesLength)
                {
                    files.WriteMarker(CreateMarker(claim, phase, RequestCqrsProbeOutcome.Released));
                    return;
                }
                await Task.Delay(pollInterval, clock, linked.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException cancellation) when (linked.IsCancellationRequested)
        {
            try
            { files.WriteMarker(CreateMarker(claim, phase, RequestCqrsProbeOutcome.Cancelled)); }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { throw new AggregateException(cancellation, cleanup); }
            throw;
        }
    }

    private RequestCqrsProbeMarkerRecord CreateMarker(RequestCqrsProbeClaim claim,
        RequestCqrsProbePhase phase, RequestCqrsProbeOutcome outcome)
        => new(RequestCqrsProbeProtocol.Version, RequestCqrsProbeProtocol.MarkerKind, claim.Arm.Record.SessionId,
            claim.Arm.Record.ArmId, claim.Identity.RequestId, claim.Identity.CommandId, phase, outcome,
            replica.LocalId, siloAddress);

    private static void ThrowOrdinary()
    {
        var error = new IOException(RequestCqrsProbeProtocol.OrdinaryMessage);
        error.Data[RequestCqrsProbeProtocol.OrdinaryDataKey] = RequestCqrsProbeProtocol.OrdinaryDataValue;
        ThrowPrivateProbeStackCanary(error);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void ThrowPrivateProbeStackCanary(IOException error) => throw error;

    private async Task DisposeAfterJoinAsync(Task start, Task join)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(stopping.Cancel, failures);
        await ServerFailureObserver.ObserveAsync(() => join, failures).ConfigureAwait(false);
        try
        {
            stopping.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
