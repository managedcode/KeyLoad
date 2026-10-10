using KeyLoad.Core;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineSeed
{
    private const string GenerationLeafPrefix = "generation-";
    private const string GenerationIdFormat = "N";

    internal static void Execute(DatabaseEngine database, NativeTextOnlineSession session,
        NativeTextOnlineRoot root, NativeTextResourceOwnership resources, ServerRuntimeOptions options)
    {
        var pin = session.SourcePin ?? throw NativeTextErrors.Ownership();
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => session.Base = pin.ReadSeed(), failures);
        ServerFailureObserver.Observe(pin.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        session.SourcePin = null;
        var fresh = NativeTextOnlineCapture.CurrentMetadata(database, session);
        NativeTextOnlineCapture.RequireSameAuthority(session, fresh);
        var seed = session.Base ?? throw NativeTextErrors.Ownership();
        session.Current = fresh;
        var scope = session.Scope ?? throw NativeTextErrors.Ownership();
        var budget = session.Budget;
        var plan = NativeTextIncrementalSeedPlanner.Plan(seed, scope,
            database.Limits.MaxScanRecords, budget, options.Core.QueryExecution.Value);
        session.Leaf = NativeTextValidation.GenerationLeaf();
        session.GenerationId = Guid.ParseExact(session.Leaf[GenerationLeafPrefix.Length..], GenerationIdFormat);
        session.GenerationReservation = resources.ReserveGeneration(root.Directory, session.Leaf, budget);
        root.CreateGeneration(session.Leaf, scope, session.GenerationReservation, budget);
        NativeTextOnlineStagingFiles.Write(Path.Combine(root.Directory, session.Leaf), session,
            options.NativeText, resources);
        session.NativeOwner = new(root.Directory, session.Leaf, session.Request.NodeId,
            options.NativeText, resources: resources);
        var owner = session.NativeOwner;
        NativeTextFile[] files = [];
        ServerFailureObserver.Observe(() =>
        {
            owner.Open(budget);
            owner.ApplyOnlineSeed(plan, budget);
            files = owner.CloseAndCapture(budget);
        }, failures);
        ServerFailureObserver.Observe(owner.CloseAfterOperation, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var manifest = new NativeTextIncrementalManifest(NativeTextIncrementalProtocol.FormatVersion,
            scope, session.Request.Consumer, session.Request.ConsumerGeneration, session.Request.Placement,
            seed.UpperSequence, seed.AppliedPosition, plan.NextRecord, plan.Records, files,
            TextProjectionProtocol.TokenizerVersion, TextProjectionProtocol.HashVersion, seed.ResourceSha256, true);
        NativeTextIncrementalValidation.Manifest(manifest, scope, session.Request.Consumer,
            session.Request.ConsumerGeneration, session.Request.Placement, database.Limits.MaxScanRecords,
            budget, options.NativeText);
        NativeTextIncrementalMetadata.Publish(owner.Path, manifest, options.NativeText.Value.MaximumDiskBytes,
            budget, options.NativeText, resources);
        session.Manifest = manifest;
        budget.Check();
    }
}
