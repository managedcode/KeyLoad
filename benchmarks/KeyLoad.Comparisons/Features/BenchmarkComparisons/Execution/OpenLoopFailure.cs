using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Comparisons;

internal static class OpenLoopFailure
{
    internal static Exception? Combine(Exception? primary, ImmutableArray<Exception> cleanup)
    {
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
            if (retained.Length == 1)
            {
                ExceptionDispatchInfo.Capture(fatal).Throw();
            }
        }
        return retained.Length switch
        {
            0 => null,
            1 => retained[0],
            _ => new AggregateException(OpenLoopFailureCodes.OpenLoopMeasurementFailed, retained)
        };
    }
}
