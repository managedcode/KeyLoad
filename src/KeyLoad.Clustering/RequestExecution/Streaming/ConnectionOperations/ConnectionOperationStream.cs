using System.Runtime.CompilerServices;

namespace KeyLoad.Clustering;

/// <summary>Joins the original execution stream before releasing its connection admission; closing admission does not cancel it.</summary>
internal static class ConnectionOperationStream
{
    internal static async IAsyncEnumerable<T> Run<T>(
        ConnectionOperationOwner owner, Guid requestId,
        Func<CancellationToken, IAsyncEnumerable<T>> createStream,
        CancellationToken cancellationToken, [EnumeratorCancellation] CancellationToken enumerationToken = default)
    {
        using var admitted = owner.Acquire(requestId);
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, enumerationToken);
        await foreach (var item in createStream(execution.Token).WithCancellation(execution.Token).ConfigureAwait(true))
        {
            yield return item;
        }
    }
}
