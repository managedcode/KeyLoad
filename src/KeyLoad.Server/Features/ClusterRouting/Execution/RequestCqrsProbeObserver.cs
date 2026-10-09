using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeObserver : IGrainRequestPhaseObserver, IAsyncDisposable
{
    private readonly Lock disposeSync = new();
    private readonly RequestCqrsProbeFiles files;
    private readonly RequestCqrsProbeLifecycle lifecycle;
    private readonly ReplicaConfiguration replica;
    private readonly IOptions<ReplicaConfiguration> replicaOptions;
    private readonly string siloAddress;
    private readonly CancellationTokenSource stopping = new();
    private Task? shutdown;
    private readonly RequestCqrsProbeHeldCallback hold;
    internal RequestCqrsCanonicalApplyObserver Canonical { get; }
    private readonly RequestCqrsReceiverIssueAdjunct receiverIssueAdjunct;
    private readonly Lazy<ConnectionProbeCapture> connections;

    internal RequestCqrsProbeObserver(RequestCqrsProbeFiles files, IOptions<ReplicaConfiguration> replicaOptions,
        string siloAddress, IHostApplicationLifetime applicationLifetime, IOptions<RequestProbeExecutionOptions> executionOptions, TimeProvider? clock = null)
    {
        replica = replicaOptions.Value;
        this.replicaOptions = replicaOptions;
        this.siloAddress = siloAddress;
        this.files = files;
        hold = new(files, applicationLifetime, executionOptions, clock ?? TimeProvider.System, CreateMarker, stopping.Token);
        lifecycle = new(executionOptions);
        Canonical = new(files, lifecycle, CreateMarker, (claim, phase, token) => HoldAsync(claim, phase, null, token));
        receiverIssueAdjunct = new(files, lifecycle, CreateMarker, HoldAsync);
        connections = new(() => new ConnectionProbeCapture(files, executionOptions));
    }

    internal RequestCqrsProbeMigration CreateMigration(ReplicaSiloDiscoveryClient discovery)
        => new(files, lifecycle, replicaOptions, siloAddress, discovery, stopping.Token);

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        lifecycle.EnterCallback();
        return new ValueTask(ObserveCoreAsync(identity, phase, context, cancellationToken));
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
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(() =>
            {
                files.WriteClaimedProducerDisposed(CreateMarker(claim, RequestCqrsProbePhase.ProducerDisposed,
                    RequestCqrsProbeOutcome.Observed), claim.Arm);
            }, failures);
            ServerFailureObserver.Observe(() => receiverIssueAdjunct.ProducerDisposed(claim), failures);
            ServerFailureObserver.ThrowIfAny(failures);
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

    private async Task ObserveCoreAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase, IGrainContext context,
        CancellationToken requestCancellation)
    {
        RequestCqrsProbeClaim? claim = null;
        var gate = false;
        try
        {
            claim = Claim(identity, phase, files.ReadSnapshot());
            if (claim is null)
            { return; }
            if (await receiverIssueAdjunct.TryObserveAsync(claim, phase, context, requestCancellation).ConfigureAwait(true))
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
            var marker = CreateMarker(claim, selectedPhase, outcome);
            files.WriteMarker(marker);
            RequestCqrsProbeActivationCapture.Observe(files, marker, context);
            if (gate && selectedPhase == RequestCqrsProbePhase.RequestStarted)
            { await connections.Value.ObserveAsync(marker, context, requestCancellation).ConfigureAwait(true); }
            if (!gate)
            { ThrowOrdinary(); }
            await HoldAsync(claim, selectedPhase, context, requestCancellation).ConfigureAwait(true);
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
        var selected = RequestCqrsProbeClaimSelection.Find(identity, snapshot, replica.LocalId, phase);
        if (selected is null)
        { return null; }
        // Retain the first validated request identity so the later disposal callback can join across voters.
        return lifecycle.AddClaim(identity, selected);
    }

    private static bool TryGetPhase(GrainRequestPhase phase, out RequestCqrsProbePhase selectedPhase)
        => Enum.TryParse(phase.ToString(), ignoreCase: false, out selectedPhase);

    private Task HoldAsync(RequestCqrsProbeClaim claim, RequestCqrsProbePhase phase,
        IGrainContext? context, CancellationToken cancellationToken)
        => hold.RunAsync(claim, phase, context, cancellationToken);

    private RequestCqrsProbeMarkerRecord CreateMarker(RequestCqrsProbeClaim claim,
        RequestCqrsProbePhase phase, RequestCqrsProbeOutcome outcome)
        => new(RequestCqrsProbeProtocol.Version, RequestCqrsProbeProtocol.MarkerKind, claim.Arm.Record.SessionId,
            claim.Arm.Record.ArmId, claim.Identity.RequestId, claim.Identity.CommandId, phase, outcome,
            replica.LocalId, siloAddress, claim.EntryIndex, claim.EntryTerm);

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
        if (connections.IsValueCreated)
        { await ServerFailureObserver.ObserveAsync(() => connections.Value.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
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
