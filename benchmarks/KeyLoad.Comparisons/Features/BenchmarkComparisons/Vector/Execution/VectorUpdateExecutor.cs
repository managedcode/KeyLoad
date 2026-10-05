using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Comparisons;

internal sealed class VectorUpdateExecutor(VectorComparisonProfile profile, NativeComparisonExecutionOptions execution)
{
    internal async Task RunAsync(IVectorComparisonTarget target, VectorComparisonCorpus corpus,
        VectorWorkloadObservations measured, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            for (var ordinal = VectorUpdateExecutorValues.FirstIndex; ordinal < profile.UpdateCount; ordinal++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var update = corpus.CreateUpdate(ordinal);
                measured.UpdateAttempts++;
                await UpdateAsync(target, update, cancellationToken).ConfigureAwait(false);
                var actual = await ReadAsync(target, update.Id, cancellationToken).ConfigureAwait(false);
                if (actual is null || actual.Id != update.Id || actual.Number != update.Number
                    || actual.Dimensions != profile.Dimensions
                    || actual.VectorSha256 != VectorComparisonCorpus.HashVector(update.Embedding.Span)
                    || actual.PayloadSha256 != Convert.ToHexStringLower(SHA256.HashData(
                        Encoding.UTF8.GetBytes(corpus.Create(update.Number).Payload))))
                {
                    throw new InvalidDataException(VectorUpdateExecutorValues.AnAcknowledgedEmbeddingUpdateFailedNative);
                }
                measured.UpdateSuccesses++;
                if (Volatile.Read(ref measured.QueryActive) != VectorUpdateExecutorValues.FirstIndex)
                {
                    measured.UpdatesDuringQueries++;
                }
            }
        }
        finally
        {
            timer.Stop();
            measured.UpdateSeconds = timer.Elapsed.TotalSeconds;
            Volatile.Write(ref measured.UpdateActive, VectorUpdateExecutorValues.FirstIndex);
        }
    }
    private async Task UpdateAsync(IVectorComparisonTarget target, VectorUpdate update, CancellationToken cancellationToken)
    {
        using var deadline = VectorOperationDeadline.Create(execution, cancellationToken);
        await target.UpdateAsync(update, deadline.Token).ConfigureAwait(false);
    }

    private async Task<VectorReadback?> ReadAsync(IVectorComparisonTarget target, string id, CancellationToken cancellationToken)
    {
        using var deadline = VectorOperationDeadline.Create(execution, cancellationToken);
        return await target.ReadAsync(id, deadline.Token).ConfigureAwait(false);
    }
}
