using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons;

/// <summary>Independent complete native ordered readback oracle, excluding verification from measured timings.</summary>
public static class DocumentComparisonOracle
{
    /// <summary>Checks every actual record against the full independently generated final state.</summary>
    public static async Task<DocumentReadbackEvidence> VerifyAsync(IAsyncEnumerable<FoundDocument> actual,
        IEnumerable<BenchmarkDocument> expected, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        using var expectation = expected.GetEnumerator();
        using var expectedHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var actualHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = DocumentMeasurementValues.NoObservedItems;
        await foreach (var row in actual.WithCancellation(token).ConfigureAwait(false))
        {
            if (!expectation.MoveNext())
            {
                throw new ComparisonFailureException(DocumentProtocolText.DocumentReadbackExtraRecord);
            }

            var value = expectation.Current;
            if (row.Id != value.Id || !BenchmarkDataset.SameJson(row.Json, value.Json))
            {
                throw new ComparisonFailureException(DocumentProtocolText.DocumentReadbackIdentityOrContentMismatch);
            }
            // Native JSON objects may reorder whitespace/properties; compare exact complete semantic body before canonical digesting.
            Append(expectedHash, value.Id);
            Append(expectedHash, CanonicalBody(value.Json));
            Append(actualHash, row.Id);
            Append(actualHash, CanonicalBody(row.Json));
            count++;
        }
        if (expectation.MoveNext())
        {
            throw new ComparisonFailureException(DocumentProtocolText.DocumentReadbackMissingRecord);
        }

        var expectedDigest = Convert.ToHexStringLower(expectedHash.GetHashAndReset());
        var actualDigest = Convert.ToHexStringLower(actualHash.GetHashAndReset());
        if (actualDigest != expectedDigest)
        {
            throw new ComparisonFailureException(DocumentProtocolText.DocumentReadbackDigestMismatch);
        }

        return new(count, expectedDigest, actualDigest);
    }
    internal static string Digest(IEnumerable<BenchmarkDocument> documents)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var document in documents)
        { Append(hash, document.Id); Append(hash, CanonicalBody(document.Json)); }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    private static string CanonicalBody(string json)
    {
        using var body = JsonDocument.Parse(json);
        var root = body.RootElement;
        return JsonSerializer.Serialize(new
        {
            id = root.GetProperty(DocumentProtocolText.IdProperty).GetString(),
            number = root.GetProperty(DocumentProtocolText.NumberProperty).GetInt32(),
            text = root.GetProperty(DocumentProtocolText.TextProperty).GetString(),
            padding = root.GetProperty(DocumentProtocolText.PaddingProperty).GetString()
        });
    }
    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
/// <summary>Actual fully enumerated cardinality and compared complete-state digests.</summary>
public sealed record DocumentReadbackEvidence(int Records, string ExpectedSha256, string ActualSha256);
