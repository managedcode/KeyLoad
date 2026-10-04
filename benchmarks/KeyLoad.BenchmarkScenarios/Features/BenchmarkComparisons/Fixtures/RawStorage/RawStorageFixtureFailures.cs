using System.Runtime.ExceptionServices;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal static class RawStorageFixtureFailures
{
    internal static bool Capture(Action action, List<Exception> failures)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            failures.Add(failure);
            return false;
        }
    }

    internal static bool IsNonFatal(Exception failure)
        => failure is not OutOfMemoryException and not StackOverflowException and not AccessViolationException
            and not AppDomainUnloadedException and not BadImageFormatException and not CannotUnloadAppDomainException;

    internal static void Throw(Exception? primary, List<Exception> failures)
    {
        if (primary is null && failures.Count == 0)
        {
            return;
        }

        if (primary is not null && failures.Count == 0)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
            return;
        }

        if (primary is null && failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
            return;
        }

        var all = new List<Exception>(failures.Count + (primary is null ? 0 : 1));
        if (primary is not null)
        {
            all.Add(primary);
        }

        all.AddRange(failures);
        throw new AggregateException(all);
    }
}
