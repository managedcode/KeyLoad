namespace KeyLoad.Server;

/// <summary>Retains physical cleanup on the same execution Orleans context which opened the session.</summary>
internal sealed class PartitionStorageOwnerContext
{
    private readonly IGrainContext context;
    private readonly IGrainContextAccessor accessor;

    internal PartitionStorageOwnerContext(IGrainContext context)
    {
        this.context = context;
        accessor = context.ActivationServices.GetRequiredService<IGrainContextAccessor>();
    }

    internal IGrainContext Context => context;
    internal IGrainContext? Current => accessor.GrainContext;

    internal Task RunAsync(Func<Task> operation)
    {
        if (ReferenceEquals(Current, context))
        {
            return operation();
        }
        var published = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Scheduler.QueueAction(() =>
        {
            var failures = new List<Exception>();
            Task? original = null;
            ServerFailureObserver.Observe(() => original = operation(), failures);
            if (failures.Count == 0)
            {
                published.SetResult(original!);
            }
            else
            {
                published.SetException(failures);
            }
        });
        return published.Task.Unwrap();
    }
}
