using KeyLoad.Core;
using KeyLoad.Orleans.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineOriginalReplay(DatabaseEngine database, ServerRuntimeOptions options,
    TimeProvider clock, NativeTextResourceOwnership resources, NativeTextOnlineSessions sessions,
    NativeTextOnlineCatalogOwner catalog, Func<NativeTextOnlineSession, Task> cleanupSession)
{
    private const int NoRetainedSessions = 0;

    internal async Task<OnlineTextCapabilityResult?> ExecuteAsync(PrincipalRecord principal,
        OnlineTextCapabilityRequest request, DateTimeOffset expiry, CancellationToken token)
    {
        var budget = new ReadExecutionBudget(options.Core.DatabaseLimits, clock, token);
        budget.ConstrainLifetime(expiry);
        var original = database.ReadOnlineTextOriginalOutcome(principal.Id, request.Request, budget);
        if (original is null)
        { return null; }
        var result = original.Get<OnlineTextIndexMaintenanceResult>();
        var publication = database.ReadOnlineTextOriginalPublication(principal.Id, request.Request, budget)
            ?? throw NativeTextErrors.Ownership();
        var retained = sessions.CaptureOriginals(principal.Id, request.Request, budget);
        foreach (var session in retained)
        { await session.CloseCommandAdmissionAndJoinAsync().ConfigureAwait(false); }
        var current = catalog.ReadCommittedCurrent(principal.Id, request.Request, budget);
        var currentRequest = new OnlineTextIndexMaintenanceRequest(current.CommandId, current.Consumer,
            current.Authority.Collection, current.Authority.Field, current.ConsumerGeneration,
            current.PublishedCut.NodeId, current.Authority.Placement);
        NativeTextResourceReservation? admitted = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (retained.Length == NoRetainedSessions)
            { admitted = resources.ReserveLease(budget); }
            var active = catalog.PublishCurrentCatalog(current, currentRequest, budget, null);
            foreach (var session in retained)
            {
                if (session.Publication.CapturedOriginal() is null)
                {
                    // CloseCommandAdmissionAndJoinAsync has atomically closed registration; this staging owner never enqueued35.
                    await cleanupSession(session).ConfigureAwait(false);
                    continue;
                }
                if (session.Leaf != publication.Authority.Leaf)
                { throw NativeTextErrors.Mismatch(); }
                var generation = catalog.RequireGeneration(publication, budget);
                if (!session.CatalogCommitted)
                {
                    generation.AdoptReservation(session.GenerationReservation);
                    session.GenerationReservation = null;
                    session.CatalogCommitted = true;
                }
                if (!ReferenceEquals(active, generation))
                { catalog.BeginRetirement(generation); }
                await cleanupSession(session).ConfigureAwait(false);
            }
        }, failures).ConfigureAwait(false);
        if (admitted is not null)
        { ServerFailureObserver.Observe(admitted.CompleteAfterJoinedCleanup, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        var reply = new OnlineTextCapabilityResult(request.SessionId, null, null,
            result.PublishedCut.ThroughSequence, result.TrackedRecords, result.IndexSha256,
            result, null, null, null);
        budget.CheckResult(reply);
        return reply;
    }
}
