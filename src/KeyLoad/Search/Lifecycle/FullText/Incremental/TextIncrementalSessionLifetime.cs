namespace KeyLoad.Server.Search;

/// <summary>Retains an unsettled actual execution owner until its own disposal completes.</summary>
internal static class TextIncrementalSessionLifetime
{
    internal static void RuntimeDispose(TextIncrementalSession session)
    {
        var owner = session.RuntimeOwner;
        owner?.Dispose();
        session.RuntimeOwner = null;
        session.OperationReservation?.CompleteAfterJoinedCleanup();
        session.OperationReservation = null;
        session.Work.DisposeAfterStage();
    }

    internal static void DisposeAll(IEnumerable<TextIncrementalSession> sessions)
    {
        var failures = new List<Exception>();
        foreach (var session in sessions)
        {
            ServerFailureObserver.Observe(() => RuntimeDispose(session), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
