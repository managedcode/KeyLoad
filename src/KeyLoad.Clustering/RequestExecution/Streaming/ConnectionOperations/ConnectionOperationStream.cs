using System.Runtime.CompilerServices;

namespace KeyLoad.Clustering;

/// <summary>Joins the original bounded execution stream before releasing its connection admission.</summary>
internal static class ConnectionOperationStream
{
    internal static async IAsyncEnumerable<T> Run<T>(
        ConnectionOperationOwner owner, Guid requestId,
        Func<CancellationToken, IAsyncEnumerable<T>> createStream,
        CancellationToken cancellationToken, [EnumeratorCancellation] CancellationToken enumerationToken = default)
    {
        using var admitted = owner.Acquire(requestId);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, enumerationToken, admitted.ShutdownToken);
        await foreach (var item in createStream(execution.Token).WithCancellation(execution.Token).ConfigureAwait(true))
        {
            yield return item;
        }
    }
}
