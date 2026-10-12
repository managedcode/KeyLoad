namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class NativeCanonicalModelCutLease : IDisposable
{
    private const int ActiveLease = 0;
    private const int DisposedLease = 1;
    private readonly RequestCqrsProbeFiles files;
    private readonly RequestCqrsProbeLifecycle lifecycle;
    private readonly CancellationTokenSource ceiling;
    private readonly CancellationTokenSource linked;
    private RequestCqrsProbeLoadedArm? arm;
    private int disposed;
    internal CancellationToken Token => linked.Token;

    internal NativeCanonicalModelCutLease(RequestCqrsProbeFiles files,
        RequestCqrsProbeLifecycle lifecycle, TimeSpan holdTimeout, TimeProvider clock,
        CancellationToken requestCancellation, CancellationToken applicationStopping,
        CancellationToken stopping)
    {
        this.files = files;
        this.lifecycle = lifecycle;
        ceiling = new CancellationTokenSource(holdTimeout, clock);
        try
        {
            linked = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation,
                applicationStopping, stopping, ceiling.Token);
        }
        catch (Exception error) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { ReleaseCeiling(error); throw; }
        catch (Exception error) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { ReleaseCeiling(error); throw; }
        try
        { linked.Token.ThrowIfCancellationRequested(); }
        catch (Exception error) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { ReleaseSources(error); throw; }
        catch (Exception error) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { ReleaseSources(error); throw; }
    }

    internal void Bind(NativeCanonicalModelCutRequest request)
    {
        Token.ThrowIfCancellationRequested();
        NativeCanonicalModelCutCodec.ValidateRequest(request);
        if (arm is not null)
        { throw NativeCanonicalModelCutCodec.Invalid(); }
        var snapshot = files.ReadSnapshot();
        var selected = snapshot.Arms.SingleOrDefault(candidate => candidate.Record.ArmId == request.ArmId);
        if (selected is null || selected.Record.SessionId != request.SessionId
            || selected.Record.CommandId != request.AcceptCommandId
            || selected.Record.PrincipalId != request.LogicalPrincipalId
            || selected.Record.ReadKind is not null || selected.Record.Action != RequestCqrsProbeAction.Hold)
        { throw NativeCanonicalModelCutCodec.Invalid(); }
        arm = selected;
        RequireCurrent();
    }

    internal void RequireCurrent()
    {
        Token.ThrowIfCancellationRequested();
        if (arm is null || Volatile.Read(ref disposed) != ActiveLease)
        { throw NativeCanonicalModelCutCodec.Invalid(); }
        var snapshot = files.ReadSnapshot();
        RequestCqrsProbeFiles.RequireActiveArm(arm, snapshot);
        // Settlement retains the immutable arm until its owner explicitly retires it.
        Token.ThrowIfCancellationRequested();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, DisposedLease) != ActiveLease)
        { return; }
        var failures = new List<Exception>();
        DisposeLinked(failures);
        DisposeCeiling(failures);
        ServerFailureObserver.Observe(lifecycle.ExitGate, failures);
        ServerFailureObserver.Observe(lifecycle.ExitCallback, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void ReleaseCeiling(Exception primary)
    {
        var failures = new List<Exception> { primary };
        DisposeCeiling(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void ReleaseSources(Exception primary)
    {
        var failures = new List<Exception> { primary };
        DisposeLinked(failures);
        DisposeCeiling(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void DisposeLinked(List<Exception> failures)
    {
        try
        { linked.Dispose(); }
        catch (Exception error) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }

    private void DisposeCeiling(List<Exception> failures)
    {
        try
        { ceiling.Dispose(); }
        catch (Exception error) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }
}
