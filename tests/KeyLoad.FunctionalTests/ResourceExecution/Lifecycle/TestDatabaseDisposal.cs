namespace KeyLoad.FunctionalTests;

internal static class TestDatabaseDisposal
{
    internal static void Rollback(Exception primary, IDisposable? acquired, bool acquisitionStarted,
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
        // A failed nested execution constructor cannot publish a proven joined owner to this fixture.
        if (closed && !acquisitionStarted)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(deleteDirectory, failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
