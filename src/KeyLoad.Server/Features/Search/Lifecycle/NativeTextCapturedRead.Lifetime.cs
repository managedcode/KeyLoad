namespace KeyLoad.Server.Features.Search;

internal sealed partial class NativeTextCapturedRead
{
    public void Dispose()
    {
        if (disposed)
        { return; }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(JoinNative, failures);
        ServerFailureObserver.Observe(OriginalProjection.Dispose, failures);
        if (failures.Count == NoFailures)
        {
            ServerFailureObserver.Observe(() => captured?.Dispose(), failures);
            ServerFailureObserver.Observe(builder.ClearUntransferred, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        disposed = true;
    }
}
