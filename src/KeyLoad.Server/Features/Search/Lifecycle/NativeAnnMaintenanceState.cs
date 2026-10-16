using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

/// <summary>Serializes the original native generation/session transitions and joined shutdown.</summary>
internal sealed class NativeAnnMaintenanceState(Func<NativeAnnGenerationOwner> createOwner)
{
    private readonly Lock gate = new();
    private NativeAnnGenerationOwner? owner;
    private NativeAnnMaintenanceSession? session;
    private bool beginning;
    private bool closed;
    private Task? disposal;

    internal NativeAnnGenerationOwner EnsureOwner()
    {
        lock (gate)
        {
            RequireOpen();
            return owner ??= createOwner();
        }
    }

    internal NativeAnnGenerationOwner? GetOwner()
    {
        lock (gate)
        {
            RequireOpen();
            return owner;
        }
    }

    internal NativeAnnGenerationOwner RequirePublicOwner()
    {
        lock (gate)
        {
            RequireOpen();
            return owner ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory);
        }
    }

    internal NativeAnnMaintenanceSession RequireOriginal(AnnMaintenanceRequest request)
    {
        lock (gate)
        {
            RequireOpen();
            var current = session ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership);
            if (!NativeAnnOriginalResults.SameRequest(current.Request, request))
            {
                throw Errors.Fail(ErrorCode.Conflict, NativeAnnProtocol.InvalidSource);
            }
            return current;
        }
    }

    internal void RequireSession(string principal, AnnMaintenanceCapabilityRequest request)
    {
        lock (gate)
        {
            RequireOpen();
            (session ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership))
                .Require(request.SessionId, request.Maintenance, principal);
        }
    }

    internal (NativeAnnGenerationOwner Owner, NativeAnnMaintenanceSession Session) RequireStage(
        string principal, AnnMaintenanceCapabilityRequest request)
    {
        lock (gate)
        {
            RequireOpen();
            var currentOwner = owner ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership);
            var currentSession = session ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership);
            currentSession.Require(request.SessionId, request.Maintenance, principal);
            return (currentOwner, currentSession);
        }
    }

    internal async Task<AnnMaintenanceCapabilityResult> BeginAsync(DatabaseEngine database, PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, ServerRuntimeOptions configured, TimeProvider clock,
        CancellationToken token, ReadExecutionBudget? originalBudget)
    {
        lock (gate)
        {
            RequireOpen();
            if (beginning || session is not null)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, NativeAnnProtocol.Bound);
            }
            beginning = true;
        }
        try
        {
            return await NativeAnnMaintenanceBeginning.RunAsync(database, principal, request, configured,
                clock, gate, () => owner, EnsureOwner, PublishSession, token, originalBudget).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                beginning = false;
            }
        }
    }

    private void PublishSession(NativeAnnMaintenanceSession created)
    {
        lock (gate)
        {
            session = created;
        }
    }

    internal NativeAnnMaintenanceWork? ObserveWork(Guid id)
    {
        lock (gate)
        {
            if (closed || session?.Id != id || session.Replay is null)
            {
                return null;
            }
            var observed = session.Observation;
            return new(id, session.Replay.WorkUnits, observed?.Kind, observed?.Budget.WrittenBytes ?? NativeAnnMaintenanceWork.NoWrittenBytes);
        }
    }

    internal async Task AbortAsync(Guid id)
    {
        NativeAnnGenerationOwner? actual;
        lock (gate)
        {
            actual = owner;
        }
        if (actual is null)
        {
            return;
        }
        await actual.RunAsync(_ =>
        {
            lock (gate)
            {
                if (session?.Id == id)
                {
                    session.Memory.Dispose();
                    session = null;
                }
            }
            return true;
        }).ConfigureAwait(false);
    }

    internal ValueTask DisposeAsync(Func<Task> closeOriginalAdmissions)
    {
        lock (gate)
        {
            closed = true;
            disposal ??= NativeAnnMaintenanceLifecycle.DisposeAsync(closeOriginalAdmissions(),
                GetDisposingOwner, ClearSession);
            return new(disposal);
        }
    }

    private NativeAnnGenerationOwner? GetDisposingOwner()
    {
        lock (gate)
        {
            return owner;
        }
    }

    private void RequireOpen()
    {
        if (closed)
        {
            throw new ObjectDisposedException(typeof(NativeAnnMaintenanceService).FullName);
        }
    }

    private void ClearSession()
    {
        lock (gate)
        {
            session = null;
        }
    }
}
