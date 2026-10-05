namespace KeyLoad.Comparisons;

internal interface IOpenLoopCancellationHealthSession
{
    Task<OpenLoopCancellationHealthRead> ReadActualAsync(BenchmarkDocument document,
        CancellationToken cancellationToken);
}

internal sealed record OpenLoopCancellationHealthRead(KeyLoad.EntityRef RequestedReference,
    KeyLoad.DocumentResult Actual);
