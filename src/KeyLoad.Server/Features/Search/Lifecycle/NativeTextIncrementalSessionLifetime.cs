namespace KeyLoad.Server.Features.Search;

/// <summary>Retains an unsettled actual native owner until its own disposal completes.</summary>
internal static class NativeTextIncrementalSessionLifetime
{
    internal static void DisposeNative(NativeTextIncrementalSession session)
    {
        var owner = session.NativeOwner;
        if (owner is null)
        { return; }
        owner.Dispose();
        session.NativeOwner = null;
    }

    internal static void DisposeAll(IEnumerable<NativeTextIncrementalSession> sessions)
    {
        var failures = new List<Exception>();
        foreach (var session in sessions)
        {
            ServerFailureObserver.Observe(() => DisposeNative(session), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
