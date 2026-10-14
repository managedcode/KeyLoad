namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsFixtureDisposalRegistration
{
    internal static bool IsJoined(System.Threading.Lock disposalGate, in Task? disposal)
    {
        lock (disposalGate)
        {
            return disposal is { IsCompletedSuccessfully: true };
        }
    }

    internal static Task Get(System.Threading.Lock disposalGate, ref Task? disposal, Func<Task, Task> dispose)
    {
        TaskCompletionSource registered;
        Task task;
        lock (disposalGate)
        {
            if (disposal is not null)
            {
                return disposal;
            }
            registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            task = dispose(registered.Task);
            disposal = task;
        }
        registered.TrySetResult();
        return task;
    }

}
