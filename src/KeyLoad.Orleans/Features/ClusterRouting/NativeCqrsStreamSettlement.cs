using ManagedCode.Communication.CQRS;

namespace KeyLoad.Orleans;

/// <summary>Joins native producer disposal and request-grain activation settlement.</summary>
internal static class NativeCqrsStreamSettlement
{
    internal static async ValueTask<Exception?> SettleAsync<T>(IAsyncEnumerator<T>? enumerator, Action settled,
        Exception? primary)
    {
        var disposal = await CaptureDisposalAsync(enumerator).ConfigureAwait(true);
        var activation = CaptureActivation(settled);
        return Combine(primary, disposal, activation);
    }

    private static async ValueTask<Exception?> CaptureDisposalAsync<T>(IAsyncEnumerator<T>? enumerator)
    {
        if (enumerator is null)
        {
            return null;
        }

        try
        {
            await enumerator.DisposeAsync().ConfigureAwait(true);
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

    private static Exception? CaptureActivation(Action settled)
    {
        try
        {
            settled();
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

    private static Exception? Combine(Exception? primary, Exception? disposal, Exception? activation)
    {
        var fatal = FindFatal(primary) ?? FindFatal(disposal) ?? FindFatal(activation);
        if (fatal is not null)
        {
            return fatal;
        }

        var includePrimary = primary is not null && !Contains(disposal, primary) && !Contains(activation, primary);
        var distinctPrimary = includePrimary ? primary : null;
        if (distinctPrimary is not null)
        {
            if (disposal is not null && activation is not null)
            {
                return new AggregateException(distinctPrimary, disposal, activation);
            }

            var cleanup = disposal ?? activation;
            return cleanup is null ? null : new AggregateException(distinctPrimary, cleanup);
        }

        if (disposal is not null && ReferenceEquals(disposal, activation))
        {
            return disposal;
        }

        if (disposal is not null && activation is not null)
        {
            return new AggregateException(disposal, activation);
        }

        return disposal ?? activation;
    }

    private static bool Contains(Exception? error, Exception candidate)
    {
        if (ReferenceEquals(error, candidate))
        {
            return true;
        }

        if (error is not AggregateException aggregate)
        {
            return false;
        }

        foreach (var inner in aggregate.InnerExceptions)
        {
            if (ReferenceEquals(inner, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static Exception? FindFatal(Exception? error) => CqrsRuntimeFailures.FindFatal(error);
}
