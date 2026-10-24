using KeyLoad.Core;
using KeyLoad.Core.Search;

namespace KeyLoad.Server.Search;

internal static class TextOnlinePublication
{
    internal static void Issue(DatabaseEngine database, TextOnlineSession session,
        ServerRuntimeOptions options, TimeProvider clock)
    {
        if (session.Issued is not null)
        {
            throw TextErrors.Mismatch();
        }
        TextOnlineValidation.Execute(database, session, options);
        TextOnlineStagingFiles.Require(TextOnlinePagePreparation.RequireOwner(session).Path,
            session, options.RuntimeText);
        var manifest = session.Manifest ?? throw TextErrors.Ownership();
        var original = session.Base ?? throw TextErrors.Ownership();
        var current = session.Current ?? throw TextErrors.Ownership();
        var checkpoint = session.Checkpoint ?? throw TextErrors.Ownership();
        var intent = session.CheckpointIntent ?? throw TextErrors.Ownership();
        var result = new OnlineTextIndexMaintenanceResult(session.Request.CommandId, session.Request.Consumer,
            session.Request.ConsumerGeneration, TextOnlineCapabilityResult.Cut(session.Request.NodeId, original),
            TextOnlineCapabilityResult.Cut(session.Request.NodeId, current),
            TextOnlineCapabilityResult.Digest(manifest, session.Budget), manifest.Records.Length, checkpoint);
        var digest = TextOnlineManifestDigest.Calculate(Path.Combine(
            TextOnlinePagePreparation.RequireOwner(session).Path, TextIncrementalProtocol.ManifestFile),
            session.Budget, options.RuntimeText);
        var command = new OnlineTextPublicationPhaseCommand(session.Request, result, current.DataEpoch,
            session.Leaf ?? throw TextErrors.Ownership(), digest, session.GenerationId,
            session.ExpectedCurrentCommandId, session.Id);
        session.RegisterPublication(result);
        session.Issued = database.CreateOnlineTextPublicationOperation(session.PrincipalId, clock.GetUtcNow(),
            new(command, current, intent, session.OriginalExpiry), session.Budget);
    }
}
