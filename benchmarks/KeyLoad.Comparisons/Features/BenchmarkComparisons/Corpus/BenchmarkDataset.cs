using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.Comparisons;

/// <summary>Creates the deterministic document, vector, and graph corpus used by comparisons.</summary>
/// <remarks>Inference and exhaustive oracle work are outside measured requests.</remarks>
public sealed class BenchmarkDataset
{
    private const string MutationTextProperty = "text";
    private const string InitialMutationText = "KeyLoad initial value";
    private readonly ComparisonOptions options;
    private readonly Dictionary<int, ImmutableArray<FoundDocument>> neighbors = [];
    private readonly Dictionary<(int Number, int Depth), ImmutableArray<string>> reachable = [];
    /// <summary>Gets the generated benchmark documents.</summary>
    public ImmutableArray<BenchmarkDocument> Documents { get; }
    /// <summary>Gets the generated directed graph edges.</summary>
    public ImmutableArray<BenchmarkEdge> Edges { get; }
    /// <summary>Gets the number of documents represented as graph vertices.</summary>
    public int GraphVertexCount => Math.Min(options.GraphVertices, Documents.Length);
    /// <summary>Gets the lowercase hexadecimal SHA-256 hash of the generated corpus.</summary>
    public string Sha256 { get; }
    /// <summary>Gets the validated options used to create the corpus.</summary>
    public ComparisonOptions Options => options;

    /// <summary>Creates and hashes the deterministic corpus described by the supplied options.</summary>
    /// <param name="options">The workload options used to generate documents, vectors, and graph edges.</param>
    public BenchmarkDataset(ComparisonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        this.options = options;
        var documents = Enumerable.Range(0, options.Documents).Select(CreateDocument).ToArray();
        Documents = ImmutableCollectionsMarshal.AsImmutableArray(documents);
        // Two disconnected components, each with directed cycles and deterministic fan-out.
        var split = (GraphVertexCount + 1) / 2;
        var edges = Enumerable.Range(0, GraphVertexCount).SelectMany(number =>
        {
            var first = number < split ? 0 : split;
            var count = number < split ? split : GraphVertexCount - split;
            return Enumerable.Range(1, Math.Min(options.GraphFanOut, count - 1)).Select(offset =>
            {
                var to = first + (number - first + offset) % count;
                return new BenchmarkEdge($"e{number:D6}-{to:D6}", Documents[number].Id, Documents[to].Id);
            });
        }).ToArray();
        Edges = ImmutableCollectionsMarshal.AsImmutableArray(edges);
        Sha256 = BenchmarkCorpusHash.Compute(Documents, Edges);
    }

    /// <summary>Creates the deterministic document and vector for the specified number.</summary>
    /// <param name="number">The document number used to derive its identifier and vector.</param>
    /// <returns>The generated document, serialized payload, and vector.</returns>
    public BenchmarkDocument CreateDocument(int number)
    {
        var id = "d" + number.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var empty = JsonSerializer.Serialize(new { id, number, text = "KeyLoad shared corpus", padding = "" });
        var json = JsonSerializer.Serialize(new
        {
            id,
            number,
            text = "KeyLoad shared corpus",
            padding = new string('x', options.PayloadBytes - Encoding.UTF8.GetByteCount(empty))
        });
        var state = unchecked((uint)options.Seed ^ ((uint)number + 1) * 0x9e3779b9u);
        if (state == 0)
        {
            state = 1;
        }

        var vector = new float[options.Dimensions];
        for (var i = 0; i < vector.Length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            vector[i] = (state & 0xffffff) / 8388608f - 1f;
        }
        return new(number, id, json, ImmutableCollectionsMarshal.AsImmutableArray(vector));
    }

    /// <summary>Selects the corpus document used for one operation, generating unique write inputs when needed.</summary>
    /// <param name="scenario">The workload scenario selecting the input policy.</param>
    /// <param name="repetition">The zero-based measurement repetition.</param>
    /// <param name="operation">The operation index within the repetition.</param>
    /// <param name="warmup">Whether the operation belongs to the warmup phase.</param>
    /// <returns>The deterministic document selected or generated for the operation.</returns>
    public BenchmarkDocument Input(Scenario scenario, int repetition, int operation, bool warmup)
    {
        if (scenario is Scenario.DocumentWrite or Scenario.QueueCycle or Scenario.StreamAppend
            or Scenario.DocumentUpdate or Scenario.DocumentDelete)
        {
            var range = scenario switch { Scenario.DocumentUpdate => 1, Scenario.DocumentDelete => 2, _ => 0 };
            return CreateDocument(options.Documents + range * options.Repetitions * (options.Operations + options.Warmup)
                + repetition * (options.Operations + options.Warmup)
                + (warmup ? operation : options.Warmup + operation));
        }

        var count = scenario is Scenario.GraphNeighbors or Scenario.GraphTraverse ? GraphVertexCount : Documents.Length;
        var index = (int)((unchecked((uint)options.Seed) + (uint)operation * 2654435761u) % (uint)count);
        return Documents[index];
    }

    /// <summary>Creates the owned existing state required before timing an update or delete.</summary>
    /// <param name="scenario">The update or delete operation to prepare.</param>
    /// <param name="input">The exact measured operation input.</param>
    /// <returns>The initial document with the same identifier and payload length.</returns>
    public static BenchmarkDocument InitialMutationState(Scenario scenario, BenchmarkDocument input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (scenario == Scenario.DocumentDelete)
        {
            return input;
        }

        if (scenario != Scenario.DocumentUpdate)
        {
            throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var payload = JsonNode.Parse(input.Json)!.AsObject();
        payload[MutationTextProperty] = InitialMutationText;
        return input with { Json = payload.ToJsonString() };
    }

    /// <summary>Returns the identifiers reachable from a starting document within the specified edge depth.</summary>
    /// <param name="start">The starting graph document.</param>
    /// <param name="depth">The maximum number of edges to traverse.</param>
    /// <returns>The reachable identifiers in ordinal order, excluding the starting identifier.</returns>
    public ImmutableArray<string> Reachable(BenchmarkDocument start, int depth)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (reachable.TryGetValue((start.Number, depth), out var cached))
        {
            return cached;
        }

        var adjacency = Edges.ToLookup(edge => edge.From, edge => edge.To, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id };
        var frontier = new Queue<(string Id, int Depth)>();
        frontier.Enqueue((start.Id, 0));
        while (frontier.TryDequeue(out var item))
        {
            if (item.Depth >= depth)
            {
                continue;
            }

            foreach (var next in adjacency[item.Id])
            {
                if (!visited.Add(next))
                {
                    continue;
                }

                frontier.Enqueue((next, item.Depth + 1));
            }
        }

        visited.Remove(start.Id);
        cached = ImmutableCollectionsMarshal.AsImmutableArray(visited.Order(StringComparer.Ordinal).ToArray());
        reachable.Add((start.Number, depth), cached);
        return cached;
    }

    /// <summary>Computes the exact top-neighbor oracle for a query vector.</summary>
    /// <param name="query">The document whose vector is compared against the corpus.</param>
    /// <returns>The highest-scoring documents, ordered by descending cosine score and then identifier.</returns>
    public ImmutableArray<FoundDocument> ExactNeighbors(BenchmarkDocument query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!neighbors.TryGetValue(query.Number, out var result))
        {
            var owned = Documents.Select(document => (Document: document, Score: Cosine(query.Vector, document.Vector)))
                .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Document.Id, StringComparer.Ordinal)
                .Take(options.TopK).Select(candidate => new FoundDocument(candidate.Document.Id, candidate.Document.Json)).ToArray();
            result = ImmutableCollectionsMarshal.AsImmutableArray(owned);
            neighbors.Add(query.Number, result);
        }
        return result;
    }

    private static double Cosine(ImmutableArray<float> left, ImmutableArray<float> right)
    {
        double dot = 0, a = 0, b = 0;
        for (var i = 0; i < left.Length; i++)
        { dot += (double)left[i] * right[i]; a += (double)left[i] * left[i]; b += (double)right[i] * right[i]; }
        return dot / Math.Sqrt(a * b);
    }

    /// <summary>Checks whether a target result has the expected identifier and JSON value.</summary>
    /// <param name="actual">The document returned by the target, or <see langword="null"/>.</param>
    /// <param name="expected">The generated document expected from the target.</param>
    /// <returns><see langword="true"/> when the identifier and parsed JSON values match.</returns>
    public static bool SameDocument(FoundDocument? actual, BenchmarkDocument expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return actual is not null && actual.Id == expected.Id && SameJson(actual.Json, expected.Json);
    }

    /// <summary>Derives a stable event identifier from a document identifier.</summary>
    /// <param name="document">The document whose identifier is hashed.</param>
    /// <returns>A GUID formed from the first 16 bytes of the identifier's SHA-256 hash.</returns>
    public static Guid EventId(BenchmarkDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(SHA256.HashData(Encoding.UTF8.GetBytes(document.Id)).AsSpan(0, 16));
    }

    /// <summary>Checks whether a target event matches the deterministic first-revision event for a document.</summary>
    /// <param name="actual">The event returned by the target, or <see langword="null"/>.</param>
    /// <param name="expected">The document used to derive the expected event identifier and payload.</param>
    /// <returns><see langword="true"/> when the event identifier, revision, and JSON value match.</returns>
    public static bool SameEvent(FoundEvent? actual, BenchmarkDocument expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return actual is not null && actual.EventId == EventId(expected) && actual.Revision == 1 && SameJson(actual.Json, expected.Json);
    }

    /// <summary>Compares two JSON strings by their parsed JSON values.</summary>
    /// <param name="left">The first JSON document.</param>
    /// <param name="right">The second JSON document.</param>
    /// <returns><see langword="true"/> when the parsed JSON values are deeply equal.</returns>
    public static bool SameJson(string left, string right)
    {
        using var a = JsonDocument.Parse(left);
        using var b = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(a.RootElement, b.RootElement);
    }
}
