using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Preserves cancellation, join and disposal failures while settling one work owner.</summary>
internal static class RequestWorkSettlement
{
    internal static Exception? Observe(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

    internal static async Task<Exception?> CancelAsync(CancellationTokenSource source)
    {
        try
        {
            await source.CancelAsync();
            return null;
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

    internal static async Task<Exception?> JoinAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

    internal static void Rethrow(Exception? failure)
    {
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal static Exception? Preserve(Exception? primary, Exception? cleanup)
    {
        var fatal = CqrsRuntimeFailures.FindFatal(primary) ?? CqrsRuntimeFailures.FindFatal(cleanup);
        if (fatal is not null)
        {
            return fatal;
        }

        if (primary is null || ReferenceEquals(primary, cleanup))
        {
            return cleanup ?? primary;
        }

        if (cleanup is null)
        {
            return primary;
        }

        return new AggregateException(primary, cleanup);
    }
}
