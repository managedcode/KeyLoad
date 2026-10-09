using System.Runtime.CompilerServices;

namespace KeyLoad.Orleans;

/// <summary>Joins the original bounded native stream before releasing its connection admission.</summary>
internal static class NativeConnectionOperationStream
{
    internal static async IAsyncEnumerable<T> Run<T>(
        NativeConnectionOperationOwner owner, Guid requestId,
        Func<CancellationToken, IAsyncEnumerable<T>> createStream,
        CancellationToken cancellationToken, [EnumeratorCancellation] CancellationToken enumerationToken = default)
    {
        using var admitted = owner.Acquire(requestId);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, enumerationToken, admitted.ShutdownToken);
        await foreach (var item in createStream(execution.Token).WithCancellation(execution.Token).ConfigureAwait(true))
        { yield return item; }
    }
}
