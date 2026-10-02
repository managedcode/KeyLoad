using System.Runtime.ExceptionServices;

namespace KeyLoad.RecoveryTests;

internal static class ReplicaMaterializerLifecycleErrors
{
    internal static async Task AttemptAsync(Func<Task> action, List<Exception> failures)
    {
        try
        { await action(); }
        catch (IOException error) { Add(error, failures); }
        catch (InvalidOperationException error) { Add(error, failures); }
        catch (OperationCanceledException error) { Add(error, failures); }
        catch (TimeoutException error) { Add(error, failures); }
        catch (KeyLoadException error) { Add(error, failures); }
    }

    internal static void Attempt(Action action, List<Exception> failures)
    {
        try
        { action(); }
        catch (IOException error) { Add(error, failures); }
        catch (UnauthorizedAccessException error) { Add(error, failures); }
        catch (ObjectDisposedException error) { Add(error, failures); }
        catch (InvalidOperationException error) { Add(error, failures); }
        catch (KeyLoadException error) { Add(error, failures); }
    }

    internal static void Add(Exception error, List<Exception> failures)
    {
        if (!failures.Contains(error))
        {
            failures.Add(error);
        }
    }

    internal static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }
}
