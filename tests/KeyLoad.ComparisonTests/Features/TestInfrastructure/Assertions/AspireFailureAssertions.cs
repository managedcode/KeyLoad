
namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal static class AspireFailureAssertions
{
    internal static readonly TimeSpan EventDeadline = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(30);

    internal static AspireFailureDeadline CreateDeadline()
        => new(TestDeadline, TestContext.Current!.Execution.CancellationToken);

    internal static async Task SettlesFailedAsync<TException>(Task operation, Func<Task> publish, CancellationToken token)
        where TException : Exception
    {
        var clock = TimeProvider.System;
        var elapsed = clock.GetTimestamp();
        await publish().WaitAsync(token);
        await Assert.ThrowsAsync<TException>(() => operation.WaitAsync(EventDeadline, clock, token));
        await Assert.That(clock.GetElapsedTime(elapsed) < EventDeadline).IsTrue();
        await Assert.That(operation.IsCompleted).IsTrue();
    }

    internal static Task JoinAsync(AspireFailureDeadline lifetime, params Task[] tasks)
        => JoinAsync(lifetime.Source, tasks);

    internal static async Task JoinAsync(CancellationTokenSource lifetime, params Task[] tasks)
    {
        await lifetime.CancelAsync();
        await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
