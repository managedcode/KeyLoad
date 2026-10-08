using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnSessionOperations
{
    private const long Empty = 0;
    private const int LoadRetentionFrames = 2;

    internal static void BeginBuild(NativeAnnMaintenanceSession session, IOptions<AnnSeedOptions> seeds, ReadExecutionBudget budget)
    {
        var upper = session.Upper;
        var source = new NativeAnnReplaySource(upper.Scope, upper.DependencySha256!, upper.Records,
            upper.OwnedBytesUpperBound, upper.ProjectionCheckpoint!.Value);
        session.Replay = new(source, new(session.Request.Consumer, session.Request.IndexGeneration), seeds, budget, applyDeltas: false);
    }

    internal static AnnMaintenanceCapabilityResult Execute(DatabaseEngine database, NativeAnnGenerationOwner owner,
        NativeAnnStageStore stages, NativeAnnMaintenanceSession session, PrincipalRecord principal,
        AnnMaintenanceCapabilityRequest request, ServerRuntimeOptions configured, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var read = new ReadExecutionBudget(configured.Core.DatabaseLimits, clock, token);
        owner.MaintenanceMemory.Expand(session.Memory);
        var available = owner.MaintenanceMemory.Remaining(checked(session.RetainedBytes
            + (session.Replay?.MaximumStagePeakBytes ?? Empty)));
        var seeds = NativeAnnMaintenanceAdmission.Seeds(configured.Core.AnnSeed, available);
        var indexBudget = new AnnWorkBudget(read, configured.Core.AnnSeed.Value.MaxWorkUnits);
        session.ObserveStage(request.Kind, indexBudget);
        var current = AnnSeedCollector.CapturePinned(database, principal.Id, request.Maintenance, seeds, read);
        session.RequireUpper(current);
        using var currentStage = session.Replay?.EnterStageCancellation(token);
        switch (request.Kind)
        {
            case AnnMaintenanceCapabilityKind.Load:
                Load(stages, owner, session, current, seeds, indexBudget, read);
                break;
            case AnnMaintenanceCapabilityKind.ApplyPage:
                Apply(session, current, request);
                break;
            case AnnMaintenanceCapabilityKind.StagePage:
                Stage(stages, session, request, configured, indexBudget);
                break;
            case AnnMaintenanceCapabilityKind.Verify:
                session.Verified = RequireReplay(session).Finish(current);
                break;
            case AnnMaintenanceCapabilityKind.Publish:
                Publish(owner, session, current, request.CheckpointIntent, indexBudget);
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, NativeAnnProtocol.InvalidSource);
        }
        return Result(session, current, session.Manifest?.CheckpointIntent);
    }

    private static void Load(NativeAnnStageStore stages, NativeAnnGenerationOwner owner, NativeAnnMaintenanceSession session,
        AnnSeed current, IOptions<AnnSeedOptions> seeds, AnnWorkBudget indexBudget, ReadExecutionBudget read)
    {
        if (session.Replay is not null)
        { throw Errors.Fail(ErrorCode.Conflict, NativeAnnProtocol.Stale); }
        var available = owner.MaintenanceMemory.Remaining(checked(session.RetainedBytes + current.PeakBytesUpperBound));
        seeds = NativeAnnMaintenanceAdmission.Seeds(seeds, available / LoadRetentionFrames);
        var manifest = stages.Describe(session.Request, current);
        NativeAnnReplaySource source;
        if (manifest.IsPending)
        { source = stages.LoadPending(session.Request, current, seeds.Value, indexBudget, read); }
        else
        {
            var loaded = stages.LoadCompleted(session.Request, current, seeds.Value,
                available / LoadRetentionFrames, indexBudget, read);
            source = NativeAnnReplaySource.From(loaded.Seed);
            session.LoadedIndex = loaded.Index;
        }
        session.Manifest = manifest;
        session.ExplicitBuild = manifest.IsPending && manifest.ExplicitBuildStage;
        session.Replay = new(source, new(session.Request.Consumer, session.Request.IndexGeneration), seeds, read,
            applyDeltas: !session.ExplicitBuild);
    }

    private static void Apply(NativeAnnMaintenanceSession session, AnnSeed current, AnnMaintenanceCapabilityRequest request)
    {
        var page = request.Page ?? throw Errors.Fail(ErrorCode.Validation, NativeAnnProtocol.InvalidSource);
        if (current.ProjectionCheckpoint != page.Consumer.Checkpoint)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.Stale); }
        RequireReplay(session).Apply(page, session.Upper.Cut.OutboxTail);
        session.LastPage = page;
    }

    private static void Stage(NativeAnnStageStore stages, NativeAnnMaintenanceSession session,
        AnnMaintenanceCapabilityRequest request, ServerRuntimeOptions configured, AnnWorkBudget budget)
    {
        var page = session.LastPage ?? throw Errors.Fail(ErrorCode.Validation, NativeAnnProtocol.InvalidSource);
        var intent = request.CheckpointIntent ?? throw Errors.Fail(ErrorCode.Validation, NativeAnnProtocol.InvalidSource);
        if (intent.Token != page.Token || intent.Consumer != page.Consumer.Consumer || !intent.Effects.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt); }
        var admitted = NativeAnnCanonicalSource.Manifest(session.Request, session.Upper, string.Empty, configured.Core.PackedAnn.Value)
            with
        { ExplicitBuildStage = session.ExplicitBuild };
        var pending = RequireReplay(session).Pending(admitted, page.Consumer.Checkpoint, intent);
        session.Manifest = stages.SavePending(pending, budget);
    }

    private static void Publish(NativeAnnGenerationOwner owner, NativeAnnMaintenanceSession session,
        AnnSeed current, CommitProjectionBatchRequest? intent, AnnWorkBudget budget)
    {
        var verified = session.Verified ?? throw Errors.Fail(ErrorCode.Validation, NativeAnnProtocol.InvalidSource);
        session.RequireUpper(current);
        NativeAnnCanonicalSource.RequireExactUpper(current, verified);
        if (session.LoadedIndex is not null && session.LastPage is null && session.Manifest is { IsPending: false } existing)
        { session.Manifest = owner.AdmitLoaded(session.LoadedIndex, existing, current); session.LoadedIndex = null; return; }
        intent ??= session.Manifest?.CheckpointIntent;
        if (intent is null)
        { throw Errors.Fail(ErrorCode.Validation, NativeAnnProtocol.InvalidSource); }
        session.Manifest = owner.BuildAndPublish(session.Request, verified, intent, budget,
            session.LastPage?.Consumer.Checkpoint ?? session.Manifest?.ReplayAfter,
            checked(session.RetainedBytes + current.PeakBytesUpperBound));
        session.LoadedIndex = null;
    }

    internal static AnnMaintenanceCapabilityResult Result(NativeAnnMaintenanceSession session, AnnSeed current,
        CommitProjectionBatchRequest? intent)
    {
        if (session.Manifest is { } observed && observed.ReplayAfter == observed.ReplayThrough)
        { intent = null; }
        var source = NativeAnnCanonicalSource.Source(current);
        return new(session.Id, source, session.Replay?.Through
            ?? session.Manifest?.ReplayThrough ?? current.ProjectionCheckpoint ?? Empty,
            session.Verified?.Records.Length ?? session.Manifest?.Count ?? current.Records.Length, intent,
            session.Manifest is { IsPending: false } completed ? Convert.ToHexStringLower(completed.IndexSha256) : null,
            session.Manifest?.ReplayAfter ?? current.ProjectionCheckpoint ?? Empty, session.Manifest?.IsPending ?? false);
    }

    private static NativeAnnReplay RequireReplay(NativeAnnMaintenanceSession session)
        => session.Replay ?? throw Errors.Fail(ErrorCode.Validation, NativeAnnProtocol.InvalidSource);
}
