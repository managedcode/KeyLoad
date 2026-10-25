using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Search;

/// <summary>Serializes the original execution generation/session transitions and joined shutdown.</summary>
internal sealed class AnnMaintenanceState(Func<AnnGenerationOwner> createOwner)
{
    private readonly Lock gate = new();
    private AnnGenerationOwner? owner;
    private AnnMaintenanceSession? session;
    private bool beginning;
    private bool closed;
    private Task? disposal;

    internal AnnGenerationOwner EnsureOwner()
    {
        lock (gate)
        {
            RequireOpen();
            return owner ??= createOwner();
        }
    }

    internal AnnGenerationOwner? GetOwner()
    {
        lock (gate)
        {
            RequireOpen();
            return owner;
        }
    }

    internal AnnGenerationOwner RequirePublicOwner()
    {
        lock (gate)
        {
            RequireOpen();
            return owner ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        }
    }

    internal AnnMaintenanceSession RequireOriginal(AnnMaintenanceRequest request)
    {
        lock (gate)
        {
            RequireOpen();
            var current = session ?? throw Errors.Fail(ErrorCode.OwnershipLost, AnnProtocol.Ownership);
            if (!AnnOriginalResults.SameRequest(current.Request, request))
            {
                throw Errors.Fail(ErrorCode.Conflict, AnnProtocol.InvalidSource);
            }
            return current;
        }
    }

    internal void RequireSession(string principal, AnnMaintenanceCapabilityRequest request)
    {
        lock (gate)
        {
            RequireOpen();
            (session ?? throw Errors.Fail(ErrorCode.OwnershipLost, AnnProtocol.Ownership))
                .Require(request.SessionId, request.Maintenance, principal);
        }
    }

    internal (AnnGenerationOwner Owner, AnnMaintenanceSession Session) RequireStage(
        string principal, AnnMaintenanceCapabilityRequest request)
    {
        lock (gate)
        {
            RequireOpen();
            var currentOwner = owner ?? throw Errors.Fail(ErrorCode.OwnershipLost, AnnProtocol.Ownership);
            var currentSession = session ?? throw Errors.Fail(ErrorCode.OwnershipLost, AnnProtocol.Ownership);
            currentSession.Require(request.SessionId, request.Maintenance, principal);
            return (currentOwner, currentSession);
        }
    }

    internal async Task<AnnMaintenanceCapabilityResult> BeginAsync(DatabaseEngine database, PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, ServerRuntimeOptions configured,
        MaintenanceSessionWork work, CancellationToken serviceShutdown)
    {
        lock (gate)
        {
            RequireOpen();
            if (beginning || session is not null)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProtocol.Bound);
            }
            beginning = true;
        }
        try
        {
            return await AnnMaintenanceBeginning.RunAsync(database, principal, request, configured,
                gate, () => owner, EnsureOwner, PublishSession, work, serviceShutdown);
        }
        finally
        {
            lock (gate)
            {
                beginning = false;
            }
        }
    }

    private void PublishSession(AnnMaintenanceSession created)
    {
        lock (gate)
        {
            session = created;
        }
    }

    internal (bool HasValue, AnnMaintenanceWork? Value) ObserveWork(Guid id)
    {
        lock (gate)
        {
            if (closed || session?.Id != id || session.Replay is null)
            {
                return (false, default(AnnMaintenanceWork?));
            }
            var observed = session.Observation;
            return (true, (AnnMaintenanceWork?)(new(id, session.Replay.WorkUnits, observed?.Kind, observed?.Budget.WrittenBytes ?? AnnMaintenanceWork.NoWrittenBytes)));
        }
    }

    internal async Task AbortAsync(Guid id)
    {
        AnnGenerationOwner? actual;
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
        });
    }

    internal ValueTask DisposeAsync(Func<Task> closeOriginalAdmissions)
    {
        lock (gate)
        {
            closed = true;
            disposal ??= AnnMaintenanceLifecycle.DisposeAsync(closeOriginalAdmissions(),
                GetDisposingOwner, ClearSession);
            return new(disposal);
        }
    }

    private AnnGenerationOwner? GetDisposingOwner()
    {
        lock (gate)
        {
            return owner;
        }
    }

    private void RequireOpen()
    {
        ObjectDisposedException.ThrowIf(closed, typeof(AnnMaintenanceService));
    }

    private void ClearSession()
    {
        lock (gate)
        {
            session?.Memory.Dispose();
            session = null;
        }
    }
}
