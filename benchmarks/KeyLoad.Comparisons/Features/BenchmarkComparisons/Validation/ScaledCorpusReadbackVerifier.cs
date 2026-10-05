using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons;

internal static class ScaledCorpusReadbackVerifier
{
    private const string IdProperty = "id";
    private const string NumberProperty = "number";
    private const string PaddingProperty = "padding";
    private const string TextProperty = "text";
    internal static Task VerifyAsync(IComparisonSession session, IComparisonCorpus corpus, CancellationToken cancellationToken)
        => VerifyAsync(session.ReadCorpusAsync(cancellationToken), corpus, cancellationToken);

    internal static async Task VerifyAsync(IAsyncEnumerable<FoundDocument> records, IComparisonCorpus corpus,
        CancellationToken cancellationToken)
    {
        if (corpus.Settings is not ScaledComparisonProfile profile)
        {
            return;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[sizeof(int)];
        var count = 0;
        await foreach (var actual in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count >= profile.Documents)
            {
                throw new ComparisonFailureException("ScaledCorpusReadbackExtraRecord");
            }

            var expected = corpus.CreateDocument(count);
            if (!string.Equals(actual.Id, expected.Id, StringComparison.Ordinal))
            {
                throw new ComparisonFailureException("ScaledCorpusReadbackOrderOrIdentityMismatch");
            }

            var canonical = CanonicalJson(actual.Json, expected);
            Append(hash, length, actual.Id);
            Append(hash, length, canonical);
            count++;
        }

        if (count != profile.Documents)
        {
            throw new ComparisonFailureException("ScaledCorpusReadbackCountMismatch");
        }

        if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), corpus.Sha256, StringComparison.Ordinal))
        {
            throw new ComparisonFailureException("ScaledCorpusReadbackDigestMismatch");
        }
    }

    private static string CanonicalJson(string actualJson, BenchmarkDocument expected)
    {
        using var document = JsonDocument.Parse(actualJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4
            || !root.TryGetProperty(IdProperty, out var id) || id.GetString() != expected.Id
            || !root.TryGetProperty(NumberProperty, out var number) || number.GetInt32() != expected.Number
            || !root.TryGetProperty(TextProperty, out var text) || text.GetString() != "KeyLoad shared corpus"
            || !root.TryGetProperty(PaddingProperty, out var padding) || padding.GetString() is not { } paddingValue
            || paddingValue.Length != 1_024 - Encoding.UTF8.GetByteCount(ScaledComparisonCorpus.EmptyJson(expected.Id, expected.Number)))
        {
            throw new ComparisonFailureException("ScaledCorpusReadbackPayloadMismatch");
        }

        foreach (var character in paddingValue)
        {
            if (character != 'x')
            {
                throw new ComparisonFailureException("ScaledCorpusReadbackPayloadMismatch");
            }
        }

        return JsonSerializer.Serialize(new { id = expected.Id, number = expected.Number, text = "KeyLoad shared corpus", padding = paddingValue });
    }

    private static void Append(IncrementalHash hash, byte[] length, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
