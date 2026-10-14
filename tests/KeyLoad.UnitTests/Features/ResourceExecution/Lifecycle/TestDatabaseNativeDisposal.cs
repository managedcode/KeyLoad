namespace KeyLoad.UnitTests;

internal static class TestDatabaseNativeDisposal
{
    internal static void Run(IDisposable? disposeReplica, Action disposeStore, Action deleteDirectory)
    {
        var failures = new List<Exception>();
        try
        {
            disposeReplica?.Dispose();
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        KeyLoad.Server.ServerFailureObserver.Observe(disposeStore, failures);
        KeyLoad.Server.ServerFailureObserver.Observe(deleteDirectory, failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
