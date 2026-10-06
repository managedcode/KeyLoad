using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons;

internal static class ScaledCorpusReadbackVerifier
{
    private const string ExpectedDocumentText = "KeyLoad shared corpus";

    private const string IdProperty = "id";
    private const string NumberProperty = "number";
    private const string PaddingProperty = "padding";
    private const string TextProperty = "text";
    internal static Task VerifyAsync(IComparisonSession session, IComparisonCorpus corpus, CancellationToken cancellationToken)
        => VerifyAsync(session.ReadCorpusAsync(cancellationToken), corpus, cancellationToken);

    internal static async Task VerifyAsync(IAsyncEnumerable<FoundDocument> records, IComparisonCorpus corpus,
        CancellationToken cancellationToken)
    {
        const string ScaledCorpusReadbackCountMismatchDetail = "ScaledCorpusReadbackCountMismatch";
        const string ScaledCorpusReadbackDigestMismatchDetail = "ScaledCorpusReadbackDigestMismatch";

        const int FirstElementIndex = 0;
        const string ScaledCorpusReadbackExtraRecordDetail = "ScaledCorpusReadbackExtraRecord";
        const string ScaledCorpusReadbackOrderOrIdentityMismatchDetail = "ScaledCorpusReadbackOrderOrIdentityMismatch";

        if (corpus.Settings is not ScaledComparisonProfile profile)
        {
            return;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[sizeof(int)];
        var count = FirstElementIndex;
        await foreach (var actual in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count >= profile.Documents)
            {
                throw new ComparisonFailureException(ScaledCorpusReadbackExtraRecordDetail);
            }

            var expected = corpus.CreateDocument(count);
            if (!string.Equals(actual.Id, expected.Id, StringComparison.Ordinal))
            {
                throw new ComparisonFailureException(ScaledCorpusReadbackOrderOrIdentityMismatchDetail);
            }

            var canonical = CanonicalJson(actual.Json, expected);
            Append(hash, length, actual.Id);
            Append(hash, length, canonical);
            count++;
        }

        if (count != profile.Documents)
        {
            throw new ComparisonFailureException(ScaledCorpusReadbackCountMismatchDetail);
        }

        if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), corpus.Sha256, StringComparison.Ordinal))
        {
            throw new ComparisonFailureException(ScaledCorpusReadbackDigestMismatchDetail);
        }
    }

    private static string CanonicalJson(string actualJson, BenchmarkDocument expected)
    {
        const int CorpusDocumentPropertyCount = 4;
        const int FrozenCorpusPayloadBytes = 1_024;
        const string ScaledCorpusReadbackPayloadMismatchDetail = "ScaledCorpusReadbackPayloadMismatch";
        const char PayloadPaddingCharacter = 'x';

        using var document = JsonDocument.Parse(actualJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != CorpusDocumentPropertyCount
            || !root.TryGetProperty(IdProperty, out var id) || id.GetString() != expected.Id
            || !root.TryGetProperty(NumberProperty, out var number) || number.GetInt32() != expected.Number
            || !root.TryGetProperty(TextProperty, out var text) || text.GetString() != ExpectedDocumentText
            || !root.TryGetProperty(PaddingProperty, out var padding) || padding.GetString() is not { } paddingValue
            || paddingValue.Length != FrozenCorpusPayloadBytes - Encoding.UTF8.GetByteCount(ScaledComparisonCorpus.EmptyJson(expected.Id, expected.Number)))
        {
            throw new ComparisonFailureException(ScaledCorpusReadbackPayloadMismatchDetail);
        }

        foreach (var character in paddingValue)
        {
            if (character != PayloadPaddingCharacter)
            {
                throw new ComparisonFailureException(ScaledCorpusReadbackPayloadMismatchDetail);
            }
        }

        return JsonSerializer.Serialize(new { id = expected.Id, number = expected.Number, text = ExpectedDocumentText, padding = paddingValue });
    }

    private static void Append(IncrementalHash hash, byte[] length, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
