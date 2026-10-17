using System.Runtime.ExceptionServices;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Retains original guarded failures and every independent registered-handle cleanup cause.</summary>
internal static class ZoneTreeExistingStoreCleanup
{
    private const int FirstFailureIndex = 0;
    private const int SingleFailureCount = 1;

    internal static void FailedConstruction(ZoneTreeStoreRuntime runtime, Exception original)
    {
        var failures = new List<Exception> { original };
        CloseRegistered(runtime, failures);
        Capture(runtime.Gate.Dispose, failures);
        ThrowFailures(failures);
    }

    internal static void FailedHandoff(ZoneTreeStoreRuntime runtime, Exception original)
    {
        var failures = new List<Exception> { original };
        Capture(runtime.Dispose, failures);
        ThrowFailures(failures);
    }

    internal static void CloseRegistered(ZoneTreeStoreRuntime runtime, List<Exception> failures)
    {
        // A null Tree does not establish provider partial-open settlement; the inspector process remains required.
        Capture(() => runtime.Tree?.Dispose(), failures);
        Capture(() => runtime.Journal?.Dispose(), failures);
        Capture(() => runtime.Ownership?.Dispose(), failures);
    }

    internal static void Capture(Action cleanup, List<Exception> failures)
    {
        try
        {
            Invoke(cleanup);
        }
        catch (AggregateException failure)
        {
            failures.Add(failure.InnerExceptions[FirstFailureIndex]);
        }
    }

    internal static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == SingleFailureCount)
        {
            ExceptionDispatchInfo.Capture(failures[FirstFailureIndex]).Throw();
        }
        if (failures.Count > SingleFailureCount)
        {
            throw new AggregateException(failures);
        }
    }

    private static void Invoke(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception original)
        {
            throw new AggregateException(original);
        }
    }
}
