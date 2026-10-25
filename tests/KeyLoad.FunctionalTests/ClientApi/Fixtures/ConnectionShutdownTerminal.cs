using KeyLoad.Server;
using Microsoft.AspNetCore.Connections;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class ConnectionShutdownTerminal(Exception? original)
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource transportClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<Task> joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ServerConnectionRegistry? registry;
    private Exception? claimed;
    private int entered;

    internal Task TransportClosed => transportClosed.Task;
    internal Task<Task> Joined => joined.Task;
    internal bool HasEntered => Volatile.Read(ref entered) != 0;

    internal void Bind(ServerConnectionRegistry owner) => registry = owner;
    internal void Release() => release.TrySetResult();
    internal void Claim(Exception provenOriginal) => claimed = provenOriginal;

    internal async Task RunAsync(ConnectionContext transport, ConnectionDelegate next)
    {
        Interlocked.Exchange(ref entered, 1);
        var running = registry!.RunAsync(transport, async () =>
        {
            try
            {
                await next(transport).ConfigureAwait(false);
            }
            finally
            {
                transportClosed.TrySetResult();
            }
            await release.Task.ConfigureAwait(false);
            if (original is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw();
            }
        });
        await running.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        joined.TrySetResult(running);
        await running.ConfigureAwait(false);
    }

    internal async Task ObserveShutdownAsync(ServerConnectionRegistry owner, List<Exception> failures)
    {
        var shutdownFailures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => owner.ShutdownAsync(CancellationToken.None), shutdownFailures)
            .ConfigureAwait(false);
        if (HasEntered)
        {
            var actual = await joined.Task.ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => actual, shutdownFailures).ConfigureAwait(false);
        }
        foreach (var failure in shutdownFailures)
        {
            if (!ReferenceEquals(failure, claimed))
            {
                failures.Add(failure);
            }
        }
    }
}
