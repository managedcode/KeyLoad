namespace KeyLoad.Server.Features.Search;

internal sealed partial class TextCapturedRead
{
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(RuntimeJoin, failures);
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
