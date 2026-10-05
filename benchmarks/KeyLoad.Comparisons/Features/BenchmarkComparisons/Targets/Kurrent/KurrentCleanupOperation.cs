using KurrentDB.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class KurrentCleanupOperation(string[] streams, CancellationToken token, IOptions<ComparisonLifecycleOptions> options) : IDisposable
{
    private readonly KurrentCleanupState state = new(streams.Length, options);
    private readonly KurrentCleanupCancellation cancellation = new(token, options);
    private readonly ComparisonLifecycleOptions settings = options.Value;
    private Task[] workers = [];
    private Task deletion = Task.CompletedTask;

    internal async Task DeleteAsync(KurrentDBClient? writer)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (writer is not null)
            {
                workers = Enumerable.Range(0, Math.Min(settings.KurrentCleanupConcurrency, streams.Length))
                    .Select(_ => DeleteWorkerAsync(writer)).ToArray();
                deletion = Task.WhenAll(workers);
                await deletion.WaitAsync(cancellation.Deadline.Token);
            }
        }
        catch (Exception error)
        {
            state.RecordFailure(error, KurrentCleanupStage.Delete);
            state.Cancel(cancellation.Operations);
            throw;
        }
        finally
        {
            state.FinishDeletion(cancellation.Operations.IsCancellationRequested, cancellation.Deadline.IsCancellationRequested);
            cancellation.CloseAfter(Task.WhenAll(workers.Append(deletion).Append(state.CancellationCallbacks)));
        }
    }

    internal Task[] StartDisposals(IReadOnlyList<KurrentDBClient> nativeClients, IReadOnlyList<HttpClient> httpClients)
        => KurrentCleanupLifetime.StartDisposals(state, nativeClients, httpClients);

    internal async Task<KurrentCleanupDiagnostic> FinishAsync(Task[] disposals, Task writerDisposal)
    {
        try
        {
            await KurrentCleanupLifetime.DrainAsync(state, [.. workers, deletion], cancellation, disposals, writerDisposal);
        }
        finally
        {
            try
            {
                KurrentCleanupDiagnostics.WriteFinal(state.Snapshot());
            }
            finally
            {
                state.ThrowIfFailed();
            }
        }
        return state.Snapshot();
    }

    private async Task DeleteWorkerAsync(KurrentDBClient writer)
    {
        var operationToken = cancellation.Operations.Token;
        while (state.TrySubmit(operationToken, out var index, cancellation.Deadline.Token))
        {
            try
            {
                await writer.DeleteAsync(streams[index], StreamState.Any, cancellationToken: operationToken);
            }
            catch (Exception error)
            {
                state.CompleteDelete(error);
                state.Cancel(cancellation.Operations);
                throw;
            }
            state.CompleteDelete(null);
        }
    }

    public void Dispose() => cancellation.Dispose();
}
