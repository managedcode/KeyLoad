namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeBlobWireOwnedFileScope
{
    internal static T Run<T>(IDisposable original, Func<T> action)
    {
        var failures = new List<Exception>();
        T result = default!;
        ServerFailureObserver.Observe(() => result = action(), failures);
        ServerFailureObserver.Observe(original.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    internal static void Run(IDisposable original, Action action)
        => Run(original, () => { action(); return true; });
}
