using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Preserves cancellation, join and disposal failures while settling one work owner.</summary>
internal static class RequestWorkSettlement
{
    internal static (bool HasValue, Exception? Value) Observe(Action action)
    {
        try
        {
            action();
            return (false, default(Exception?));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
        }
    }

    internal static async Task<(bool HasValue, Exception? Value)> CancelAsync(CancellationTokenSource source)
    {
        try
        {
            await source.CancelAsync();
            return (false, default(Exception?));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
        }
    }

    internal static async Task<(bool HasValue, Exception? Value)> JoinAsync(Task task)
    {
        try
        {
            await task;
            return (false, default(Exception?));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
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
