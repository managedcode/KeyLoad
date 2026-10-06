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
        const int EmptyFailuresCount = 0;
        const int ThrowEmptyFailuresCount = 1;
        const int IndexEmptyCount = 0;

        if (primary is null && failures.Count == EmptyFailuresCount)
        {
            return;
        }

        if (primary is not null && failures.Count == EmptyFailuresCount)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
            return;
        }

        if (primary is null && failures.Count == ThrowEmptyFailuresCount)
        {
            ExceptionDispatchInfo.Capture(failures[IndexEmptyCount]).Throw();
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
