namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Announces the same original post-active bootstrap without treating pending publication as failure.</summary>
internal sealed class OrleansNodeStartupWait
{
    private readonly TaskCompletionSource<Task?> publication = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Publish(Task original) => publication.TrySetResult(original);

    internal void Close() => publication.TrySetResult(null);

    internal async Task WaitAsync(CancellationToken cancellationToken)
    {
        var original = await publication.Task.WaitAsync(cancellationToken).ConfigureAwait(false)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable);
        var failures = new List<Exception>();
        var awaited = original.WaitAsync(cancellationToken);
        await ServerFailureObserver.ObserveAsync(() => awaited, failures).ConfigureAwait(false);
        if (awaited.IsFaulted && original.Exception is { } fault)
        {
            foreach (var failure in fault.InnerExceptions)
            {
                if (!failures.Contains(failure, ReferenceEqualityComparer.Instance))
                {
                    failures.Add(failure);
                }
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
