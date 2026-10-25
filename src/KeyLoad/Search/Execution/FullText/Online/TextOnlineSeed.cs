using KeyLoad.Core;
using KeyLoad.Query.Search;

namespace KeyLoad.Server.Search;

internal static class TextOnlineSeed
{
    private const string GenerationLeafPrefix = "generation-";
    private const string GenerationIdFormat = "N";

    internal static void Execute(DatabaseEngine database, TextOnlineSession session,
        TextOnlineRoot root, TextResourceOwnership resources, ServerRuntimeOptions options)
    {
        var pin = session.SourcePin ?? throw TextErrors.Ownership();
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => session.Base = pin.ReadSeed(), failures);
        ServerFailureObserver.Observe(pin.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        session.SourcePin = null;
        var fresh = TextOnlineCapture.CurrentMetadata(database, session);
        TextOnlineCapture.RequireSameAuthority(session, fresh);
        var seed = session.Base ?? throw TextErrors.Ownership();
        session.Current = fresh;
        var scope = session.Scope ?? throw TextErrors.Ownership();
        var budget = session.Budget;
        var plan = TextIncrementalSeedPlanner.Plan(seed, scope, budget, options.Core.QueryExecution.Value);
        session.Leaf = TextValidation.GenerationLeaf();
        session.GenerationId = Guid.ParseExact(session.Leaf[GenerationLeafPrefix.Length..], GenerationIdFormat);
        session.GenerationReservation = resources.ReserveGeneration(root.Directory, session.Leaf, budget);
        root.CreateGeneration(session.Leaf, scope, session.GenerationReservation, budget);
        TextOnlineStagingFiles.Write(Path.Combine(root.Directory, session.Leaf), session,
            options.RuntimeText, resources);
        session.RuntimeOwner = new(root.Directory, session.Leaf, session.Request.NodeId,
            options.RuntimeText, resources: resources);
        var owner = session.RuntimeOwner;
        TextFile[] files = [];
        ServerFailureObserver.Observe(() =>
        {
            owner.Open(budget);
            owner.ApplyOnlineSeed(plan, budget);
            files = owner.CloseAndCapture(budget);
        }, failures);
        ServerFailureObserver.Observe(owner.CloseAfterOperation, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var manifest = new TextIncrementalManifest(TextIncrementalProtocol.ManifestFormatVersion,
            scope, session.Request.Consumer, session.Request.ConsumerGeneration, session.Request.Placement,
            seed.UpperSequence, seed.AppliedPosition, plan.NextRecord, plan.Records, files,
            TextProjectionProtocol.TokenizerVersion, TextProjectionProtocol.HashVersion, seed.ResourceSha256, true);
        TextIncrementalValidation.Manifest(manifest, scope, session.Request.Consumer,
            session.Request.ConsumerGeneration, session.Request.Placement,
            budget, options.RuntimeText);
        TextIncrementalMetadata.Publish(owner.Path, manifest, options.RuntimeText.Value.MaximumDiskBytes,
            budget, options.RuntimeText, resources);
        session.Manifest = manifest;
        budget.Check();
    }
}
