using System.Runtime.ExceptionServices;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests;

internal static class TestDatabaseJoinedLifetime
{
    internal static void DisposeInOrder(IDisposable? first, IDisposable second)
    {
        var failures = new List<Exception>();
        var fatal = Observe(() => first?.Dispose(), failures);
        var secondFatal = Observe(second.Dispose, failures);
        ThrowIfAny(failures, fatal ?? secondFatal);
    }

    internal static Exception? Observe(Action operation, List<Exception> failures)
    {
        try
        { operation(); }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        { failures.Add(failure); }
        catch (Exception fatal) when (CqrsRuntimeFailures.FindFatal(fatal) is not null)
        { failures.Add(fatal); return fatal; }
        return null;
    }

    internal static async Task<Exception?> ObserveAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        { await operation(); }
        catch (Exception failure) when (CqrsRuntimeFailures.FindFatal(failure) is null)
        { failures.Add(failure); }
        catch (Exception fatal) when (CqrsRuntimeFailures.FindFatal(fatal) is not null)
        { failures.Add(fatal); return fatal; }
        return null;
    }

    internal static Exception RetainFatal(List<Exception> failures, Exception? originalFatal, Exception failure)
    {
        failures.Add(failure);
        return originalFatal ?? failure;
    }

    internal static void ThrowIfAny(List<Exception> failures, Exception? fatal)
    {
        if (fatal is not null)
        { ExceptionDispatchInfo.Capture(failures.Count == 1 ? fatal : new AggregateException(failures)).Throw(); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
