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
    private readonly Dictionary<(int Number, int Depth), string[]> reachable = [];
    public BenchmarkDocument[] Documents { get; }
    public BenchmarkEdge[] Edges { get; }
    public int GraphVertexCount => Math.Min(options.GraphVertices, Documents.Length);
    public string Sha256 { get; }
    public ComparisonOptions Options => options;

    public BenchmarkDataset(ComparisonOptions options)
    {
        options.Validate();
        this.options = options;
        Documents = Enumerable.Range(0, options.Documents).Select(CreateDocument).ToArray();
        // Two disconnected components, each with directed cycles and deterministic fan-out.
        var split = (GraphVertexCount + 1) / 2;
        Edges = Enumerable.Range(0, GraphVertexCount).SelectMany(number =>
        {
            var first = number < split ? 0 : split;
            var count = number < split ? split : GraphVertexCount - split;
            return Enumerable.Range(1, Math.Min(options.GraphFanOut, count - 1)).Select(offset =>
            {
                var to = first + (number - first + offset) % count;
                return new BenchmarkEdge($"e{number:D6}-{to:D6}", Documents[number].Id, Documents[to].Id);
            });
        }).ToArray();
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
        foreach (var edge in Edges) hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(edge)));
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
        var count = scenario is Scenario.GraphNeighbors or Scenario.GraphTraverse ? GraphVertexCount : Documents.Length;
        var index = (int)((unchecked((uint)options.Seed) + (uint)operation * 2654435761u) % (uint)count);
        return Documents[index];
    }

    public string[] Reachable(BenchmarkDocument start, int depth)
    {
        if (reachable.TryGetValue((start.Number, depth), out var cached)) return cached;
        var adjacency = Edges.ToLookup(edge => edge.From, edge => edge.To, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id };
        var frontier = new Queue<(string Id, int Depth)>();
        frontier.Enqueue((start.Id, 0));
        while (frontier.TryDequeue(out var item))
            if (item.Depth < depth)
                foreach (var next in adjacency[item.Id])
                    if (visited.Add(next)) frontier.Enqueue((next, item.Depth + 1));
        visited.Remove(start.Id);
        cached = visited.Order(StringComparer.Ordinal).ToArray();
        reachable.Add((start.Number, depth), cached);
        return cached;
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
