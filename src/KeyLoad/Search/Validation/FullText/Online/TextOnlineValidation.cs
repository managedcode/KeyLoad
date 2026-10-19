using KeyLoad.Core;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class TextOnlineValidation
{
    internal static TextSeedCapture CaptureFull(DatabaseEngine database, TextOnlineSession session)
    {
        var request = session.Request;
        var pin = TextOnlineSourcePin.Capture(database, session.PrincipalId,
            new(request.Consumer, request.ConsumerGeneration, request.Collection, request.Field,
                request.NodeId, request.Placement), session.Budget);
        TextSeedCapture? result = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => result = pin.ReadSeed(), failures);
        ServerFailureObserver.Observe(pin.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw TextErrors.Corrupt();
    }

    internal static void Execute(DatabaseEngine database, TextOnlineSession session,
        ServerRuntimeOptions options)
    {
        var manifest = session.Manifest ?? throw TextErrors.Ownership();
        var checkpoint = session.Checkpoint ?? throw TextErrors.Ownership();
        if (session.Intent is not null || session.Target is not null || session.CheckpointWork is not null
            || manifest.Bootstrap)
        {
            throw TextErrors.Mismatch();
        }
        var fresh = CaptureFull(database, session);
        TextOnlineCapture.RequireSameAuthority(session, fresh);
        if (checkpoint.Checkpoint != fresh.Checkpoint || checkpoint.Checkpoint != fresh.UpperSequence
            || manifest.ThroughSequence != fresh.UpperSequence)
        {
            throw TextErrors.Mismatch();
        }
        TextIncrementalSourceValidation.CompleteCorpus(manifest, fresh, session.Budget);
        TextIncrementalPublishedInventory.Require(TextOnlinePagePreparation.RequireOwner(session),
            manifest, session.Budget, options.RuntimeText);
        session.Current = fresh;
    }
}
