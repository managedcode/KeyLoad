using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Comparisons;

internal static class OpenLoopFailure
{
    internal static async Task<Exception?> ObserveAsync(Task original)
    {
        try
        {
            await original.ConfigureAwait(false);
            return null;
        }
        catch (Exception failure) when (original.IsFaulted || original.IsCanceled)
        {
            return failure;
        }
    }

    internal static Exception? Combine(Exception? primary, ImmutableArray<Exception> cleanup)
    {
        const int SingleItemCount = 1;
        const int NoObservedItems = 0;
        const int FirstElementIndex = 0;

        var failures = ImmutableArray.CreateBuilder<Exception>();
        if (primary is not null)
        {
            failures.Add(primary);
        }
        foreach (var failure in cleanup)
        {
            if (!failures.Any(existing => ReferenceEquals(existing, failure)))
            {
                failures.Add(failure);
            }
        }
        var retained = failures.ToImmutable();
        var fatal = retained.Select(CqrsRuntimeFailures.FindFatal).FirstOrDefault(item => item is not null);
        if (fatal is not null)
        {
            var ordered = ImmutableArray.CreateBuilder<Exception>();
            ordered.Add(fatal);
            foreach (var failure in retained)
            {
                if (!ReferenceEquals(failure, fatal) && !ordered.Any(item => ReferenceEquals(item, failure)))
                {
                    ordered.Add(failure);
                }
            }
            retained = ordered.ToImmutable();
            if (retained.Length == SingleItemCount)
            {
                ExceptionDispatchInfo.Capture(fatal).Throw();
            }
        }
        return retained.Length switch
        {
            NoObservedItems => null,
            SingleItemCount => retained[FirstElementIndex],
            _ => new AggregateException(OpenLoopFailureCodes.OpenLoopMeasurementFailed, retained)
        };
    }
}
