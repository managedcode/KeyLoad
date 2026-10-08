using System.Runtime.ExceptionServices;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Observes one actual server lifecycle stage to completion while retaining its terminal failures.</summary>
internal static class ServerFailureObserver
{
    /// <summary>Captures synchronous invocation failures, every actual task fault and task cancellation without relocating stage work.</summary>
    /// <param name="stage">Actual lifecycle operation invoked inline before observing its returned task.</param>
    /// <param name="failures">Existing ordered failures to append without replacing exception identities.</param>
    /// <returns>Completion after the actual operation reaches its terminal state.</returns>
    internal static async Task ObserveAsync(Func<Task> stage, List<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(failures);
        Task terminal;
        try
        { terminal = stage(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); return; }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); return; }
        await terminal.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (terminal.Exception is { } terminalFailure)
        {
            failures.AddRange(terminalFailure.InnerExceptions);
        }
        else if (terminal.IsCanceled)
        {
            failures.Add(new TaskCanceledException(terminal));
        }
    }

    /// <summary>Runs synchronous work inline and retains its actual exception without converting cancellation into a task.</summary>
    /// <param name="stage">Actual synchronous cleanup operation; no asynchronous work is relocated.</param>
    /// <param name="failures">Existing ordered failures to append while preserving their identities.</param>
    internal static void Observe(Action stage, List<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(failures);
        try
        { stage(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }

    /// <summary>Returns for no failures, rethrows one original failure, or emits every ordered failure as an aggregate.</summary>
    /// <param name="failures">Terminal failures collected from the actual cleanup stages.</param>
    internal static void ThrowIfAny(IReadOnlyList<Exception> failures)
    {
        const int EmptyFailuresCount = 1;
        const int IndexEmptyCount = 0;
        const int FailuresCountValidationBoundary = 1;

        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count == EmptyFailuresCount)
        {
            ExceptionDispatchInfo.Capture(failures[IndexEmptyCount]).Throw();
        }
        if (failures.Count > FailuresCountValidationBoundary)
        {
            throw new AggregateException(failures);
        }
    }
}
