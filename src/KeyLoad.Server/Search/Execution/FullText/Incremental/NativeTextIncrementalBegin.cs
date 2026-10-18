using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalBegin
{
    internal static TextMaintenanceCapabilityResult Run(DatabaseEngine database, string root, PrincipalRecord principal,
        TextMaintenanceCapabilityRequest request, ServerRuntimeOptions configured, TimeProvider clock,
        CancellationToken shutdown, NativeTextIncrementalSessions sessions, Action<NativeTextFaultStage>? faultObserver,
        NativeTextResourceOwnership? resources, Action<string, NativeTextResourceReservation> retainGeneration,
        DateTimeOffset? expiry, IResourceLifetimeWorkBinding? binding, CancellationToken token)
    {
        var work = new NativeMaintenanceSessionWork(configured.Core.DatabaseLimits, clock,
            expiry ?? clock.GetUtcNow().AddSeconds(configured.Core.DatabaseLimits.Value.QueryDeadlineSeconds), shutdown);
        var captureStarted = false;
        var captured = false;
        try
        {
            sessions.BeginWork(request.SessionId, work);
            using var stage = work.EnterStage(binding, token);
            var limits = configured.Core.DatabaseLimits.Value;
            captureStarted = true;
            var actual = Capture(database, root, request.SessionId, principal,
                request.Maintenance, work, limits.MaxScanRecords, limits.MaxScanRecords, configured.NativeText,
                faultObserver, resources, retainGeneration, sessions.Own);
            captured = true;
            sessions.Add(actual);
            actual.Budget.Check();
            return NativeTextIncrementalCapabilityResult.Create(actual, actual.Upper);
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

    private static NativeTextIncrementalSession Capture(DatabaseEngine database, string root,
        Guid sessionId, PrincipalRecord principal, TextIndexMaintenanceRequest request, NativeMaintenanceSessionWork work,
        int maximumRecords, int maximumChanges, IOptions<NativeTextExecutionOptions> options,
        Action<NativeTextFaultStage>? faultObserver = null, NativeTextResourceOwnership? resources = null,
        Action<string, NativeTextResourceReservation>? retainGeneration = null,
        Action<NativeTextIncrementalSession>? ownSession = null)
    {
        var budget = work.Budget;
        NativeTextResourceReservation? reservation = null;
        NativeTextIncrementalSession? captured = null;
        void CaptureOwnership(NativeTextIncrementalSession actual)
        {
            captured = actual;
            actual.OperationReservation = reservation;
            ownSession?.Invoke(actual);
        }
        try
        {
            budget.Check();
            reservation = resources?.ReserveLease(budget);
            var admitted = NativeTextSeedCollector.Capture(database, principal.Id,
                new(request.Consumer, request.IndexGeneration, request.Collection, request.Field,
                    request.NodeId, request.Placement), budget);
            var leaf = NativeTextIncrementalEnrollmentFiles.Find(root, request, budget, options);
            if (leaf is null)
            {
                if (request.Mode != TextIndexMaintenanceMode.Build)
                {
                    throw NativeTextErrors.Mismatch();
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
                ServerFailureObserver.Observe(() => NativeTextIncrementalSessionLifetime.DisposeNative(captured), failures);
            }
            else if (reservation is not null)
            {
                ServerFailureObserver.Observe(reservation.CompleteAfterJoinedCleanup, failures);
            }
            if (captured is null && failures.Count == 1)
            {
                ServerFailureObserver.Observe(work.DisposeAfterStage, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static NativeTextIncrementalSession Create(string root, Guid sessionId, TextIndexMaintenanceRequest request,
        NativeTextSeedCapture admitted, NativeMaintenanceSessionWork work, IOptions<NativeTextExecutionOptions> options,
        Action<NativeTextFaultStage>? faultObserver, NativeTextResourceOwnership? resources,
        Action<string, NativeTextResourceReservation>? retainGeneration, Action<NativeTextIncrementalSession> captureOwnership)
    {
        var budget = work.Budget;
        var session = new NativeTextIncrementalSession(sessionId, request, admitted,
            admitted.UpperSequence, work);
        captureOwnership(session);
        var leaf = NativeTextValidation.GenerationLeaf();
        if (resources is not null)
        {
            if (retainGeneration is null)
            {
                throw NativeTextErrors.Ownership();
            }
            var reserved = resources.ReserveGeneration(root, leaf, budget);
            retainGeneration(leaf, reserved);
        }
        NativeTextIncrementalRoot.CreateGeneration(root, leaf, session.Scope, request,
            admitted.UpperSequence, admitted.ResourceSha256, budget, options, resources);
        session.GenerationLeaf = leaf;
        session.NativeOwner = new(root, leaf, request.NodeId, options, faultObserver, resources);
        session.OriginalBuild = true;
        session.NativeOwner.Observe(NativeTextFaultStage.OwnerFlushed);
        budget.Check();
        return session;
    }

    private static NativeTextIncrementalSession Restore(string root, string leaf, Guid sessionId,
        TextIndexMaintenanceRequest request, NativeTextSeedCapture admitted, NativeMaintenanceSessionWork work,
        int maximumRecords, int maximumChanges, IOptions<NativeTextExecutionOptions> options,
        Action<NativeTextFaultStage>? faultObserver, NativeTextResourceOwnership? resources,
        Action<NativeTextIncrementalSession> captureOwnership)
    {
        var budget = work.Budget;
        var path = Path.Combine(root, leaf);
        var enrolled = NativeTextIncrementalEnrollmentFiles.Read(path, request.NodeId, budget, options);
        var manifestPath = Path.Combine(path, NativeTextIncrementalProtocol.ManifestFile);
        var intentPath = Path.Combine(path, NativeTextIncrementalProtocol.IntentFile);
        var hasManifest = File.Exists(manifestPath);
        var hasIntent = File.Exists(intentPath);
        var through = NativeTextIncrementalRecoveryIdentity.RequireAdmittedUpper(enrolled, request,
            admitted, hasManifest || hasIntent, budget);
        var session = new NativeTextIncrementalSession(sessionId, request, admitted,
            through, work, enrolled.Scope)
        {
            GenerationLeaf = leaf
        };
        captureOwnership(session);
        session.NativeOwner = new(root, leaf, request.NodeId, options, faultObserver, resources);
        if (hasManifest)
        {
            session.Manifest = NativeTextIncrementalMetadata.ReadManifest(path, options.Value.MaximumDiskBytes, budget);
            NativeTextIncrementalValidation.Manifest(session.Manifest, enrolled.Scope, request.Consumer,
                request.IndexGeneration, request.Placement, maximumRecords, budget, options);
        }
        if (hasIntent)
        {
            session.Intent = NativeTextIncrementalMetadata.ReadIntent(path, options.Value.MaximumDiskBytes, budget);
            NativeTextIncrementalIntentValidation.Require(session.Intent, enrolled.Scope, request.Consumer,
                request.IndexGeneration, request.Placement, maximumRecords, maximumChanges, budget);
            NativeTextIncrementalRecoveryIdentity.RequireIntent(enrolled, session.Intent, session.Manifest, budget);
            session.Target = NativeTextIncrementalRecoveryTarget.FromIntent(session.Intent, session.Manifest, budget);
        }
        if (!hasIntent && session.Manifest is { } complete)
        {
            if (!complete.Bootstrap && complete.LastSettledCheckpointRequest is null)
            {
                throw NativeTextErrors.Corrupt();
            }
            NativeTextIncrementalPublishedInventory.Require(session.NativeOwner, complete, budget, options);
        }
        budget.Check();
        return session;
    }
}
