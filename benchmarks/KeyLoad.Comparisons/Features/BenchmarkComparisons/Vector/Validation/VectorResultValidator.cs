using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

internal sealed class VectorResultValidator(VectorComparisonProfile profile, IOptions<NativeComparisonExecutionOptions> executionOptions, TimeProvider? provider = null)
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    internal async Task ValidateReadbackAsync(IVectorComparisonTarget target, VectorComparisonCorpus corpus,
        CancellationToken cancellationToken)
    {
        var expectedNumber = VectorResultValidatorValues.FirstIndex;
        await foreach (var actual in target.ReadbackAsync(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (actual.Number != expectedNumber || actual.Dimensions != profile.Dimensions)
            {
                throw new InvalidDataException(VectorResultValidatorValues.NativeVectorReadbackHasAnUnexpected);
            }

            var expected = corpus.Create(expectedNumber++);
            if (actual.Id != expected.Id || actual.VectorSha256 != VectorComparisonCorpus.HashVector(expected.Embedding.Span)
                || actual.PayloadSha256 != Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(expected.Payload))))
            {
                throw new InvalidDataException(VectorResultValidatorValues.NativeVectorReadbackDiffersFromCanonical);
            }
        }
        if (expectedNumber != profile.RecordCount)
        {
            throw new InvalidDataException($"{VectorResultValidatorValues.NativeReadbackReturned}{expectedNumber}{VectorResultValidatorValues.RecordsExpected}{profile.RecordCount}{VectorResultValidatorValues.SentencePeriod}");
        }
    }

    internal async Task ValidateQueryAsync(IVectorComparisonTarget target, VectorComparisonCorpus corpus,
        ReadOnlyMemory<float> query, IReadOnlyList<VectorNeighbor> expected, CancellationToken cancellationToken)
    {
        using var deadline = VectorOperationDeadline.Create(executionOptions.Value, cancellationToken: cancellationToken, timeProvider: timeProvider);
        VectorResponseValidator.CalculateRecall(corpus,
            await target.SearchAsync(query, profile.TopK, profile.QueryMode, deadline.Token).ConfigureAwait(false), expected);
    }

}
