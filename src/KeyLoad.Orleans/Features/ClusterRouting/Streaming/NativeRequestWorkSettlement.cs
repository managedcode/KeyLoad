using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

/// <summary>Preserves cancellation, join and disposal failures while settling one work owner.</summary>
internal static class NativeRequestWorkSettlement
{
    internal static async Task<Exception?> CancelAsync(CancellationTokenSource source)
    {
        try
        {
            await source.CancelAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

    internal static async Task<Exception?> JoinAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
            return null;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
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
