using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Joins execution producer disposal and request-grain activation settlement.</summary>
internal static class CqrsStreamSettlement
{
    internal static async ValueTask<Exception?> SettleAsync<T>(IAsyncEnumerator<T>? enumerator, Action settled,
        Exception? primary)
    {
        var disposal = (await CaptureDisposalAsync(enumerator).ConfigureAwait(true)).Value;
        var activation = (CaptureActivation(settled)).Value;
        return (Combine(primary, disposal, activation)).Value;
    }

    internal static async ValueTask<Exception?> SettleAwaitedAsync<T>(IAsyncEnumerator<T>? enumerator, Func<ValueTask> settled,
        Exception? primary)
    {
        var disposal = (await CaptureDisposalAsync(enumerator).ConfigureAwait(true)).Value;
        var activation = (await CaptureActivationAsync(settled).ConfigureAwait(true)).Value;
        return (Combine(primary, disposal, activation)).Value;
    }

    private static async ValueTask<global::ManagedCode.Communication.Result<Exception?>> CaptureActivationAsync(Func<ValueTask> settled)
    {
        try
        {
            await settled().ConfigureAwait(true);
            return global::ManagedCode.Communication.Result<Exception?>.FailNotFound(nameof(CaptureActivationAsync));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error)) { return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error))); }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error)) { return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error))); }
    }

    private static async ValueTask<global::ManagedCode.Communication.Result<Exception?>> CaptureDisposalAsync<T>(IAsyncEnumerator<T>? enumerator)
    {
        if (enumerator is null)
        {
            return global::ManagedCode.Communication.Result<Exception?>.FailNotFound(nameof(CaptureDisposalAsync));
        }

        try
        {
            await enumerator.DisposeAsync().ConfigureAwait(true);
            return global::ManagedCode.Communication.Result<Exception?>.FailNotFound(nameof(CaptureDisposalAsync));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error)));
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error)));
        }
    }

    private static global::ManagedCode.Communication.Result<Exception?> CaptureActivation(Action settled)
    {
        try
        {
            settled();
            return global::ManagedCode.Communication.Result<Exception?>.FailNotFound(nameof(CaptureActivation));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error)));
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error)));
        }
    }

    private static global::ManagedCode.Communication.Result<Exception?> Combine(Exception? primary, Exception? disposal, Exception? activation)
    {
        var fatal = FindFatal(primary) ?? FindFatal(disposal) ?? FindFatal(activation);
        if (fatal is not null)
        {
            return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(fatal)));
        }

        var includePrimary = primary is not null && !Contains(disposal, primary) && !Contains(activation, primary);
        var distinctPrimary = includePrimary ? primary : null;
        if (distinctPrimary is not null)
        {
            if (disposal is not null && activation is not null)
            {
                return global::ManagedCode.Communication.Result<Exception?>.Succeed((Exception?)(new AggregateException(distinctPrimary, disposal, activation)));
            }

            var cleanup = disposal ?? activation;
            return cleanup is null ? global::ManagedCode.Communication.Result<Exception?>.FailNotFound(nameof(Combine)) : global::ManagedCode.Communication.Result<Exception?>.Succeed((Exception?)(new AggregateException(distinctPrimary, cleanup)));
        }

        if (disposal is not null && ReferenceEquals(disposal, activation))
        {
            return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(disposal)));
        }

        if (disposal is not null && activation is not null)
        {
            return global::ManagedCode.Communication.Result<Exception?>.Succeed((Exception?)(new AggregateException(disposal, activation)));
        }

        return ((Exception?)(disposal ?? activation)) is { } outcomeValue7 ? global::ManagedCode.Communication.Result<Exception?>.Succeed(outcomeValue7) : global::ManagedCode.Communication.Result<Exception?>.FailNotFound(nameof(Combine));
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
