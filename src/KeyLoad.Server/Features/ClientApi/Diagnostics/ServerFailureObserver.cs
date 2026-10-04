using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace KeyLoad.Server;

/// <summary>Observes one actual server lifecycle stage to completion while retaining its terminal failures.</summary>
internal static class ServerFailureObserver
{
    /// <summary>Captures synchronous invocation failures, every actual task fault and task cancellation without relocating stage work.</summary>
    /// <param name="stage">Actual lifecycle operation to invoke in an async wrapper.</param>
    /// <param name="failures">Existing ordered failures to append without replacing exception identities.</param>
    /// <returns>Completion after the actual operation reaches its terminal state.</returns>
    internal static async Task ObserveAsync(Func<Task> stage, List<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(failures);
        var actual = new StrongBox<Task?>();
        async Task InvokeAsync()
        {
            actual.Value = stage();
            await actual.Value.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        var invocation = InvokeAsync();
        await invocation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        var terminal = actual.Value ?? invocation;
        if (terminal.Exception is { } error)
        {
            failures.AddRange(error.InnerExceptions);
        }
        else if (terminal.IsCanceled)
        {
            failures.Add(new TaskCanceledException(terminal));
        }
    }

    /// <summary>Runs synchronous cleanup inline through the same invocation and terminal-failure observation boundary.</summary>
    /// <param name="stage">Actual synchronous cleanup operation; no asynchronous work is relocated.</param>
    /// <param name="failures">Existing ordered failures to append while preserving their identities.</param>
    internal static void Observe(Action stage, List<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(failures);
        ObserveAsync(() =>
        {
            stage();
            return Task.CompletedTask;
        }, failures).GetAwaiter().GetResult();
    }

    /// <summary>Returns for no failures, rethrows one original failure, or emits every ordered failure as an aggregate.</summary>
    /// <param name="failures">Terminal failures collected from the actual cleanup stages.</param>
    internal static void ThrowIfAny(IReadOnlyList<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }
}
