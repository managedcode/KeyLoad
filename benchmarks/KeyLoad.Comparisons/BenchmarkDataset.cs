using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons;

/// <summary>Fixed PRNG and float32 corpus. Inference and exhaustive oracle work are outside measured requests.</summary>
public sealed class BenchmarkDataset
{
    private readonly ComparisonOptions options;
    private readonly Dictionary<int, FoundDocument[]> neighbors = [];
    public BenchmarkDocument[] Documents { get; }
    public string Sha256 { get; }
    public ComparisonOptions Options => options;

    public BenchmarkDataset(ComparisonOptions options)
    {
        options.Validate();
        this.options = options;
        Documents = Enumerable.Range(0, options.Documents).Select(CreateDocument).ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var document in Documents)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(document.Json));
            foreach (var value in document.Vector)
            {
                BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
                hash.AppendData(bytes);
            }
        }
        Sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public BenchmarkDocument CreateDocument(int number)
    {
        var id = "d" + number.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var empty = JsonSerializer.Serialize(new { id, number, text = "KeyLoad shared corpus", padding = "" });
        var json = JsonSerializer.Serialize(new { id, number, text = "KeyLoad shared corpus",
            padding = new string('x', options.PayloadBytes - Encoding.UTF8.GetByteCount(empty)) });
        var state = unchecked((uint)options.Seed ^ ((uint)number + 1) * 0x9e3779b9u);
        if (state == 0) state = 1;
        var vector = new float[options.Dimensions];
        for (var i = 0; i < vector.Length; i++)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            vector[i] = (state & 0xffffff) / 8388608f - 1f;
        }
        return new(number, id, json, vector);
    }

    public BenchmarkDocument Input(Scenario scenario, int repetition, int operation, bool warmup)
    {
        if (scenario is Scenario.DocumentWrite or Scenario.QueueCycle)
            return CreateDocument(options.Documents + repetition * (options.Operations + options.Warmup)
                + (warmup ? operation : options.Warmup + operation));
        var index = (int)((unchecked((uint)options.Seed) + (uint)operation * 2654435761u) % (uint)Documents.Length);
        return Documents[index];
    }

    public FoundDocument[] ExactNeighbors(BenchmarkDocument query)
    {
        if (!neighbors.TryGetValue(query.Number, out var result))
        {
            result = Documents.Select(document => (Document: document, Score: Cosine(query.Vector, document.Vector)))
                .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Document.Id, StringComparer.Ordinal)
                .Take(options.TopK).Select(candidate => new FoundDocument(candidate.Document.Id, candidate.Document.Json)).ToArray();
            neighbors.Add(query.Number, result);
        }
        return result;
    }

    private static double Cosine(float[] left, float[] right)
    {
        double dot = 0, a = 0, b = 0;
        for (var i = 0; i < left.Length; i++) { dot += (double)left[i] * right[i]; a += (double)left[i] * left[i]; b += (double)right[i] * right[i]; }
        return dot / Math.Sqrt(a * b);
    }

    public static bool SameDocument(FoundDocument? actual, BenchmarkDocument expected)
        => actual is not null && actual.Id == expected.Id && SameJson(actual.Json, expected.Json);
    public static bool SameJson(string left, string right)
    {
        using var a = JsonDocument.Parse(left); using var b = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(a.RootElement, b.RootElement);
    }
}
