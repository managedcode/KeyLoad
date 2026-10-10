using KeyLoad.Core;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextOnlineValidation
{
    internal static NativeTextSeedCapture CaptureFull(DatabaseEngine database, NativeTextOnlineSession session)
    {
        var request = session.Request;
        var pin = NativeTextOnlineSourcePin.Capture(database, session.PrincipalId,
            new(request.Consumer, request.ConsumerGeneration, request.Collection, request.Field,
                request.NodeId, request.Placement), session.Budget);
        NativeTextSeedCapture? result = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => result = pin.ReadSeed(), failures);
        ServerFailureObserver.Observe(pin.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw NativeTextErrors.Corrupt();
    }

    internal static void Execute(DatabaseEngine database, NativeTextOnlineSession session,
        ServerRuntimeOptions options)
    {
        var manifest = session.Manifest ?? throw NativeTextErrors.Ownership();
        var checkpoint = session.Checkpoint ?? throw NativeTextErrors.Ownership();
        if (session.Intent is not null || session.Target is not null || session.CheckpointWork is not null
            || manifest.Bootstrap)
        { throw NativeTextErrors.Mismatch(); }
        var fresh = CaptureFull(database, session);
        NativeTextOnlineCapture.RequireSameAuthority(session, fresh);
        if (checkpoint.Checkpoint != fresh.Checkpoint || checkpoint.Checkpoint != fresh.UpperSequence
            || manifest.ThroughSequence != fresh.UpperSequence)
        { throw NativeTextErrors.Mismatch(); }
        NativeTextIncrementalSourceValidation.CompleteCorpus(manifest, fresh, session.Budget);
        NativeTextIncrementalPublishedInventory.Require(NativeTextOnlinePagePreparation.RequireOwner(session),
            manifest, session.Budget, options.NativeText);
        session.Current = fresh;
    }
}
