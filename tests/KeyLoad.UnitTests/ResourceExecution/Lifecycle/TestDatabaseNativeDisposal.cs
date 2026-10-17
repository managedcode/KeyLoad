namespace KeyLoad.UnitTests;

internal static class TestDatabaseNativeDisposal
{
    internal static void Rollback(Exception primary, IDisposable? acquired, bool nativeAcquisitionStarted,
        Action deleteDirectory)
    {
        var failures = new List<Exception> { primary };
        var closed = false;
        try
        {
            acquired?.Dispose();
            closed = acquired is not null;
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is null)
        {
            failures.Add(error);
        }
        catch (Exception error) when (ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            failures.Add(error);
        }
        // A failed nested native constructor cannot publish a proven joined owner to this fixture.
        if (closed && !nativeAcquisitionStarted)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(deleteDirectory, failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
