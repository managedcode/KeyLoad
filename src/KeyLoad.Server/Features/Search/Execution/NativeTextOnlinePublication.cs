using KeyLoad.Core;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlinePublication
{
    internal static void Issue(DatabaseEngine database, NativeTextOnlineSession session,
        ServerRuntimeOptions options, TimeProvider clock)
    {
        if (session.Issued is not null)
        { throw NativeTextErrors.Mismatch(); }
        NativeTextOnlineValidation.Execute(database, session, options);
        NativeTextOnlineStagingFiles.Require(NativeTextOnlinePagePreparation.RequireOwner(session).Path,
            session, options.NativeText);
        var manifest = session.Manifest ?? throw NativeTextErrors.Ownership();
        var original = session.Base ?? throw NativeTextErrors.Ownership();
        var current = session.Current ?? throw NativeTextErrors.Ownership();
        var checkpoint = session.Checkpoint ?? throw NativeTextErrors.Ownership();
        var intent = session.CheckpointIntent ?? throw NativeTextErrors.Ownership();
        var result = new OnlineTextIndexMaintenanceResult(session.Request.CommandId, session.Request.Consumer,
            session.Request.ConsumerGeneration, NativeTextOnlineCapabilityResult.Cut(session.Request.NodeId, original),
            NativeTextOnlineCapabilityResult.Cut(session.Request.NodeId, current),
            NativeTextOnlineCapabilityResult.Digest(manifest, session.Budget), manifest.Records.Length, checkpoint);
        var digest = NativeTextOnlineManifestDigest.Calculate(Path.Combine(
            NativeTextOnlinePagePreparation.RequireOwner(session).Path, NativeTextIncrementalProtocol.ManifestFile),
            session.Budget, options.NativeText);
        var command = new OnlineTextPublicationPhaseCommand(session.Request, result, current.DataEpoch,
            session.Leaf ?? throw NativeTextErrors.Ownership(), digest, session.GenerationId,
            session.ExpectedCurrentCommandId, session.Id);
        session.RegisterPublication(result);
        session.Issued = database.CreateOnlineTextPublicationOperation(session.PrincipalId, clock.GetUtcNow(),
            new(command, current, intent, session.OriginalExpiry), session.Budget);
    }
}
