using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class TextIncrementalBegin
{
    private const int OriginalFailureOnlyCount = 1;
    internal static TextMaintenanceCapabilityResult Run(DatabaseEngine database, string root, PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, ServerRuntimeOptions configured, TimeProvider clock,
        CancellationTokenSource shutdown, TextIncrementalSessions sessions, Action<TextFaultStage>? faultObserver,
        TextResourceOwnership? resources, Action<string, TextResourceReservation> retainGeneration,
        DateTimeOffset? expiry, IResourceLifetimeWorkBinding? binding, CancellationToken token)
    {
        var work = new MaintenanceSessionWork(configured.Core.DatabaseLimits, clock,
            expiry ?? clock.GetUtcNow().AddSeconds(configured.Core.DatabaseLimits.Value.QueryDeadlineSeconds), shutdown.Token);
        var captureStarted = false;
        var captured = false;
        try
        {
            sessions.BeginWork(request.SessionId, work);
            using var stage = work.EnterStage(binding, token);
            var limits = configured.Core.DatabaseLimits.Value;
            captureStarted = true;
            var actual = Capture(database, root, request.SessionId, principal,
                request.Maintenance, work, limits.MaxScanRecords, limits.MaxScanRecords, configured.RuntimeText,
                faultObserver, resources, retainGeneration, sessions.Own);
            captured = true;
            sessions.Add(actual);
            actual.Budget.Check();
            return TextIncrementalCapabilityResult.Create(actual, actual.Upper);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (!captureStarted)
            {
                ServerFailureObserver.Observe(work.DisposeAfterStage, failures);
            }
            if (captured || work.ReleaseRequested)
            {
                ServerFailureObserver.Observe(() => sessions.Retire(request.SessionId), failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static TextIncrementalSession Capture(DatabaseEngine database, string root,
        Guid sessionId, PrincipalRecord principal, TextIndexMaintenanceRequest request, MaintenanceSessionWork work,
        int maximumRecords, int maximumChanges, IOptions<TextExecutionOptions> options,
        Action<TextFaultStage>? faultObserver = null, TextResourceOwnership? resources = null,
        Action<string, TextResourceReservation>? retainGeneration = null,
        Action<TextIncrementalSession>? ownSession = null)
    {
        var budget = work.Budget;
        TextResourceReservation? reservation = null;
        TextIncrementalSession? captured = null;
        void CaptureOwnership(TextIncrementalSession actual)
        {
            captured = actual;
            actual.OperationReservation = reservation;
            ownSession?.Invoke(actual);
        }
        try
        {
            budget.Check();
            reservation = resources?.ReserveLease(budget);
            var admitted = TextSeedCollector.Capture(database, principal.Id,
                new(request.Consumer, request.IndexGeneration, request.Collection, request.Field,
                    request.NodeId, request.Placement), budget);
            var leaf = TextIncrementalEnrollmentFiles.Find(root, request, budget, options);
            if (leaf is null)
            {
                if (request.Mode != TextIndexMaintenanceMode.Build)
                {
                    throw TextErrors.Mismatch();
                }
                return Create(root, sessionId, request, admitted, work, options, faultObserver,
                    resources, retainGeneration, CaptureOwnership);
            }
            return Restore(root, leaf, sessionId, request, admitted, work, maximumRecords,
                maximumChanges, options, faultObserver, resources, CaptureOwnership);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            if (captured is not null)
            {
                ServerFailureObserver.Observe(() => TextIncrementalSessionLifetime.RuntimeDispose(captured), failures);
            }
            else if (reservation is not null)
            {
                ServerFailureObserver.Observe(reservation.CompleteAfterJoinedCleanup, failures);
            }
            if (captured is null && failures.Count == OriginalFailureOnlyCount)
            {
                ServerFailureObserver.Observe(work.DisposeAfterStage, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static TextIncrementalSession Create(string root, Guid sessionId, TextIndexMaintenanceRequest request,
        TextSeedCapture admitted, MaintenanceSessionWork work, IOptions<TextExecutionOptions> options,
        Action<TextFaultStage>? faultObserver, TextResourceOwnership? resources,
        Action<string, TextResourceReservation>? retainGeneration, Action<TextIncrementalSession> captureOwnership)
    {
        var budget = work.Budget;
        var session = new TextIncrementalSession(sessionId, request, admitted,
            admitted.UpperSequence, work);
        captureOwnership(session);
        var leaf = TextValidation.GenerationLeaf();
        if (resources is not null)
        {
            if (retainGeneration is null)
            {
                throw TextErrors.Ownership();
            }
            var reserved = resources.ReserveGeneration(root, leaf, budget);
            retainGeneration(leaf, reserved);
        }
        TextIncrementalRoot.CreateGeneration(root, leaf, session.Scope, request,
            admitted.UpperSequence, admitted.ResourceSha256, budget, options, resources);
        session.GenerationLeaf = leaf;
        session.RuntimeOwner = new(root, leaf, request.NodeId, options, faultObserver, resources);
        session.OriginalBuild = true;
        session.RuntimeOwner.Observe(TextFaultStage.OwnerFlushed);
        budget.Check();
        return session;
    }

    private static TextIncrementalSession Restore(string root, string leaf, Guid sessionId,
        TextIndexMaintenanceRequest request, TextSeedCapture admitted, MaintenanceSessionWork work,
        int maximumRecords, int maximumChanges, IOptions<TextExecutionOptions> options,
        Action<TextFaultStage>? faultObserver, TextResourceOwnership? resources,
        Action<TextIncrementalSession> captureOwnership)
    {
        var budget = work.Budget;
        var path = Path.Combine(root, leaf);
        var enrolled = TextIncrementalEnrollmentFiles.Read(path, request.NodeId, budget, options);
        var manifestPath = Path.Combine(path, TextIncrementalProtocol.ManifestFile);
        var intentPath = Path.Combine(path, TextIncrementalProtocol.IntentFile);
        var hasManifest = File.Exists(manifestPath);
        var hasIntent = File.Exists(intentPath);
        var through = TextIncrementalRecoveryIdentity.RequireAdmittedUpper(enrolled, request,
            admitted, hasManifest || hasIntent, budget);
        var session = new TextIncrementalSession(sessionId, request, admitted,
            through, work, enrolled.Scope)
        {
            GenerationLeaf = leaf
        };
        captureOwnership(session);
        session.RuntimeOwner = new(root, leaf, request.NodeId, options, faultObserver, resources);
        if (hasManifest)
        {
            session.Manifest = TextIncrementalMetadata.ReadManifest(path, options.Value.MaximumDiskBytes, budget);
            TextIncrementalValidation.Manifest(session.Manifest, enrolled.Scope, request.Consumer,
                request.IndexGeneration, request.Placement, maximumRecords, budget, options);
        }
        if (hasIntent)
        {
            session.Intent = TextIncrementalMetadata.ReadIntent(path, options.Value.MaximumDiskBytes, budget);
            TextIncrementalIntentValidation.Require(session.Intent, enrolled.Scope, request.Consumer,
                request.IndexGeneration, request.Placement, maximumRecords, maximumChanges, budget);
            TextIncrementalRecoveryIdentity.RequireIntent(enrolled, session.Intent, session.Manifest, budget);
            session.Target = TextIncrementalRecoveryTarget.FromIntent(session.Intent, session.Manifest, budget);
        }
        if (!hasIntent && session.Manifest is { } complete)
        {
            if (!complete.Bootstrap && complete.LastSettledCheckpointRequest is null)
            {
                throw TextErrors.Corrupt();
            }
            TextIncrementalPublishedInventory.Require(session.RuntimeOwner, complete, budget, options);
        }
        budget.Check();
        return session;
    }
}
