using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RuntimeConstructorCycleSettlement
{
    internal static async Task JoinCancelledAsync(Task? original, List<Exception> failures,
        CancellationToken caller)
    {
        if (original is null)
        {
            return;
        }
        var actual = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, actual);
        foreach (var error in actual)
        {
            if (original.IsCanceled && caller.IsCancellationRequested && error is OperationCanceledException cancelled)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                    await Assert.That(cancelled.CancellationToken.IsCancellationRequested).IsTrue(), failures);
            }
            else
            {
                failures.Add(error);
            }
        }
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(original.IsCanceled).IsTrue(), failures);
    }
}
