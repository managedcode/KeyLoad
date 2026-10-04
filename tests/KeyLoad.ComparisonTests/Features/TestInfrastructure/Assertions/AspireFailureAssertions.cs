using System.Diagnostics;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal static class AspireFailureAssertions
{
    internal static readonly TimeSpan EventDeadline = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(30);

    internal static CancellationTokenSource CreateDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TestDeadline);
        return deadline;
    }

    internal static async Task SettlesFailedAsync<TException>(Task operation, Func<Task> publish, CancellationToken token)
        where TException : Exception
    {
        var elapsed = Stopwatch.StartNew();
        await publish().WaitAsync(token);
        await Assert.ThrowsAsync<TException>(() => operation.WaitAsync(EventDeadline, token));
        await Assert.That(elapsed.Elapsed < EventDeadline).IsTrue();
        await Assert.That(operation.IsCompleted).IsTrue();
    }

    internal static async Task JoinAsync(CancellationTokenSource lifetime, params Task[] tasks)
    {
        await lifetime.CancelAsync();
        await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
