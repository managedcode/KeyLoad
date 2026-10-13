using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class RequestCqrsProbeObserver : IGrainRequestPhaseObserver, IGrainPartitionMovementJournalObserver, IGrainRequestProducerDisposedObserver, global::KeyLoad.Server.Features.DocumentStorage.IRemoteModelNativeFaultObserver, global::KeyLoad.Server.Features.DocumentStorage.IRemoteModelNativeReplayObserver, IAsyncDisposable
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
    private readonly NativeCanonicalModelCutAdmission canonicalCuts;
    internal RequestCqrsCanonicalApplyObserver Canonical { get; }
    internal RequestCqrsQueryReadCutObservation QueryCuts { get; }
    internal RequestCqrsReplicaLogObserver ReplicaLog { get; }
    internal RequestCqrsReplicaSnapshotObserver ReplicaSnapshot { get; }
    private readonly RequestCqrsReceiverIssueAdjunct receiverIssueAdjunct;
    private readonly RequestCqrsNativeModelObservation nativeModel;
    private readonly RequestCqrsNativeReplayObservation nativeReplay;
    private readonly Lazy<ConnectionProbeCapture> connections;
    internal RequestCqrsProbeProducerDisposal ProducerDisposal { get; }

    internal RequestCqrsProbeObserver(RequestCqrsProbeFiles files, IOptions<ReplicaConfiguration> replicaOptions,
        string siloAddress, IHostApplicationLifetime applicationLifetime, IOptions<RequestProbeExecutionOptions> executionOptions, TimeProvider? clock = null)
    {
        replica = replicaOptions.Value;
        this.replicaOptions = replicaOptions;
        this.siloAddress = siloAddress;
        this.files = files;
        hold = new(files, applicationLifetime, executionOptions, clock ?? TimeProvider.System, CreateMarker, stopping.Token);
        lifecycle = new(executionOptions);
        canonicalCuts = new(files, lifecycle, applicationLifetime, executionOptions, clock ?? TimeProvider.System, stopping.Token);
        QueryCuts = new(files, lifecycle, replica.LocalId, siloAddress);
        nativeModel = new(files, lifecycle, executionOptions, replica.LocalId, siloAddress, CreateMarker, HoldAsync);
        nativeReplay = new(files, lifecycle, executionOptions, replica.LocalId, siloAddress, CreateMarker, HoldAsync);
        ReplicaSnapshot = new(files, lifecycle, replicaOptions, executionOptions, clock ?? TimeProvider.System,
            CreateMarker, HoldAsync, stopping.Token, applicationLifetime.ApplicationStopping);
        Canonical = new(files, lifecycle, CreateMarker, (claim, phase, token) => HoldAsync(claim, phase, null, token));
        ReplicaLog = new(files, lifecycle, replica.LocalId, siloAddress, CreateReplicaLogMarker, ThrowOrdinary);
        receiverIssueAdjunct = new(files, lifecycle, CreateMarker, HoldAsync);
        ProducerDisposal = new(files, lifecycle, CreateMarker, receiverIssueAdjunct);
        connections = new(() => new ConnectionProbeCapture(files, executionOptions));
    }

    public ValueTask BeforeDispatchAsync(global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeRequest original,
        CancellationToken cancellationToken)
        => nativeModel.ObserveAsync(original, null, RequestCqrsProbePhase.NativeModelBeforeDispatch, cancellationToken);

    public ValueTask AfterDispatchAsync(global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeRequest original,
        global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeReply actual, CancellationToken cancellationToken)
        => nativeModel.ObserveAsync(original, actual, RequestCqrsProbePhase.NativeModelAfterDispatch, cancellationToken);

    public global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeReplayVariation? Select(
        global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeRequest original, CancellationToken token)
        => nativeReplay.Select(original, token);

    public ValueTask ObserveAsync(global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeRequest original,
        global::KeyLoad.Server.Features.DocumentStorage.RemoteModelNativeReplayExchange owner, CancellationToken token)
        => nativeReplay.ObserveAsync(original, owner, token);

    internal NativeCanonicalModelCutLease OpenCanonicalCut(CancellationToken requestCancellation)
        => canonicalCuts.Open(requestCancellation);

    internal RequestCqrsProbeMigration CreateMigration(ReplicaSiloDiscoveryClient discovery)
        => new(files, lifecycle, replicaOptions, siloAddress, discovery, stopping.Token);

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        lifecycle.EnterCallback();
        return new ValueTask(ObserveCoreAsync(identity, phase, context, cancellationToken));
    }

    public void CaptureReturned(GrainRequestProbeIdentity identity, OperationResult actual, IGrainContext context)
        => RequestCqrsMovementJournalCapture.CaptureReturned(lifecycle, files, identity, actual,
            context, replica.LocalId, siloAddress);

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ProducerDisposal.Complete(identity);
    }

    internal void AttachProducerDisposalRelay(RequestCqrsProbeNativeRelay relay) => ProducerDisposal.Attach(relay);

    public ValueTask ProducerDisposedAsync(GrainRequestProbeIdentity identity, Guid connectionId, IGrainContext context)
        => ProducerDisposal.CompleteAsync(identity, connectionId, context);

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
            if (claim is not null && identity.ReadKind is GrainReadKind.AstQuery or GrainReadKind.WaitForIndex
                && phase == GrainRequestPhase.AuthorizationReload)
            { claim.QueryReadContext = context; }
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
            if (selectedPhase == RequestCqrsProbePhase.EventVectorFirstDispatchReserved)
            { files.EventVectorExpiry.Publish(claim); }
            RequestCqrsMovementJournalCapture.PublishReturned(files, claim, marker);
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
        => RequestCqrsProbeClaimSelection.Bind(identity, phase, snapshot, replica.LocalId, lifecycle,
            TryGetPhase(phase, out _));

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

    private RequestCqrsProbeMarkerRecord CreateReplicaLogMarker(RequestCqrsProbeArmRecord arm, ReplicaEntry entry, RequestCqrsProbePhase phase)
        => new(RequestCqrsProbeProtocol.Version, RequestCqrsProbeProtocol.MarkerKind, arm.SessionId,
            arm.ArmId, arm.SourceRequestId!.Value, arm.CommandId, phase,
            phase == RequestCqrsProbePhase.ReplicaEntryAcknowledged ? RequestCqrsProbeOutcome.FaultRequested : RequestCqrsProbeOutcome.Observed,
            replica.LocalId, siloAddress, entry.Index, entry.Term);

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

}
