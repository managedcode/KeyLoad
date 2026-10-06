using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Creates the deterministic document, vector, and graph corpus used by comparisons.</summary>
/// <remarks>Inference and exhaustive oracle work are outside measured requests.</remarks>
public sealed class BenchmarkDataset : IComparisonCorpus
{
    private const string EdgeIdentityPrefix = "e";
    private const string EdgeOrdinalFormat = "D6";
    private const string IdentitySeparator = "-";

    private const string DocumentNumberFormat = "D9";

    private const string MutationTextProperty = "text";
    private const string InitialMutationText = "KeyLoad initial value";
    private readonly ComparisonOptions options;
    internal IOptions<ComparisonOptions> ExecutionOptions { get; }
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
    IComparisonSettings IComparisonCorpus.Settings => options;
    IReadOnlyList<BenchmarkDocument> IComparisonCorpus.Documents => Documents;
    IReadOnlyList<BenchmarkEdge> IComparisonCorpus.Edges => Edges;

    /// <summary>Creates and hashes the deterministic corpus described by the supplied options.</summary>
    /// <param name="workloadOptions">The workload options used to generate documents, vectors, and graph edges.</param>
    public BenchmarkDataset(IOptions<ComparisonOptions> workloadOptions)
    {
        const int FirstElementIndex = 0;
        const int AdjacentElementOffset = 1;
        const int DisconnectedGraphComponentCount = 2;
        const int NoObservedItems = 0;
        const int FirstNeighborOffset = 1;

        ArgumentNullException.ThrowIfNull(workloadOptions);
        options = workloadOptions.Value;
        options.Validate();
        ExecutionOptions = workloadOptions;
        var documents = Enumerable.Range(FirstElementIndex, options.Documents).Select(CreateDocument).ToArray();
        Documents = ImmutableCollectionsMarshal.AsImmutableArray(documents);
        // Two disconnected components, each with directed cycles and deterministic fan-out.
        var split = (GraphVertexCount + AdjacentElementOffset) / DisconnectedGraphComponentCount;
        var edges = Enumerable.Range(FirstElementIndex, GraphVertexCount).SelectMany(number =>
        {
            var first = number < split ? NoObservedItems : split;
            var count = number < split ? split : GraphVertexCount - split;
            return Enumerable.Range(FirstNeighborOffset, Math.Min(options.GraphFanOut, count - FirstNeighborOffset)).Select(offset =>
            {
                var to = first + (number - first + offset) % count;
                return new BenchmarkEdge($"{EdgeIdentityPrefix}{(number).ToString(EdgeOrdinalFormat, global::System.Globalization.CultureInfo.CurrentCulture)}{IdentitySeparator}{(to).ToString(EdgeOrdinalFormat, global::System.Globalization.CultureInfo.CurrentCulture)}", Documents[number].Id, Documents[to].Id);
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
        const int FinalXorShift = 5;

        const string DocumentIdentityPrefix = "d";
        const string SharedCorpusText = "KeyLoad shared corpus";
        const string EmptyText = "";
        const char PayloadPaddingCharacter = 'x';
        const int AdjacentElementOffset = 1;
        const uint SeedMixMultiplierUint = 0x9e3779b9u;
        const int UninitializedRandomState = 0;
        const int NonzeroRandomSeed = 1;
        const int FirstElementIndex = 0;
        const int FirstXorShift = 13;
        const int ReverseXorShift = 17;
        const int VectorFractionMask = 0xffffff;
        const float VectorFractionDenominatorFloat = 8388608f;
        const float VectorCoordinateOffsetFloat = 1f;

        var id = DocumentIdentityPrefix + number.ToString(DocumentNumberFormat, System.Globalization.CultureInfo.InvariantCulture);
        var empty = JsonSerializer.Serialize(new { id, number, text = SharedCorpusText, padding = EmptyText });
        var json = JsonSerializer.Serialize(new
        {
            id,
            number,
            text = SharedCorpusText,
            padding = new string(PayloadPaddingCharacter, options.PayloadBytes - Encoding.UTF8.GetByteCount(empty))
        });
        var state = unchecked((uint)options.Seed ^ ((uint)number + AdjacentElementOffset) * SeedMixMultiplierUint);
        if (state == UninitializedRandomState)
        {
            state = NonzeroRandomSeed;
        }

        var vector = new float[options.Dimensions];
        for (var i = FirstElementIndex; i < vector.Length; i++)
        {
            state ^= state << FirstXorShift;
            state ^= state >> ReverseXorShift;
            state ^= state << FinalXorShift;
            vector[i] = (state & VectorFractionMask) / VectorFractionDenominatorFloat - VectorCoordinateOffsetFloat;
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
        const int UpdateIdentityBlock = 1;
        const int DeleteIdentityBlock = 2;
        const int NoObservedItems = 0;
        const uint DeterministicReadStrideUint = 2654435761u;

        if (scenario is Scenario.DocumentWrite or Scenario.QueueCycle or Scenario.StreamAppend
            or Scenario.DocumentUpdate or Scenario.DocumentDelete)
        {
            var range = scenario switch { Scenario.DocumentUpdate => UpdateIdentityBlock, Scenario.DocumentDelete => DeleteIdentityBlock, _ => NoObservedItems };
            return CreateDocument(options.Documents + range * options.Repetitions * (options.Operations + options.Warmup)
                + repetition * (options.Operations + options.Warmup)
                + (warmup ? operation : options.Warmup + operation));
        }

        var count = scenario is Scenario.GraphNeighbors or Scenario.GraphTraverse ? GraphVertexCount : Documents.Length;
        var index = (int)((unchecked((uint)options.Seed) + (uint)operation * DeterministicReadStrideUint) % (uint)count);
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
        const int NoObservedItems = 0;
        const int AdjacentElementOffset = 1;

        ArgumentNullException.ThrowIfNull(start);
        if (reachable.TryGetValue((start.Number, depth), out var cached))
        {
            return cached;
        }

        var adjacency = Edges.ToLookup(edge => edge.From, edge => edge.To, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id };
        var frontier = new Queue<(string Id, int Depth)>();
        frontier.Enqueue((start.Id, NoObservedItems));
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

                frontier.Enqueue((next, item.Depth + AdjacentElementOffset));
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
            var owned = Documents.Select(document => (Document: document, Score: BenchmarkCosineOracle.Score(query.Vector, document.Vector)))
                .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.Document.Id, StringComparer.Ordinal)
                .Take(options.TopK).Select(candidate => new FoundDocument(candidate.Document.Id, candidate.Document.Json)).ToArray();
            result = ImmutableCollectionsMarshal.AsImmutableArray(owned);
            neighbors.Add(query.Number, result);
        }
        return result;
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
        const int FirstElementIndex = 0;
        const int GuidDigestBytes = 16;

        ArgumentNullException.ThrowIfNull(document);
        return new(SHA256.HashData(Encoding.UTF8.GetBytes(document.Id)).AsSpan(FirstElementIndex, GuidDigestBytes));
    }

    /// <summary>Checks whether a target event matches the deterministic first-revision event for a document.</summary>
    /// <param name="actual">The event returned by the target, or <see langword="null"/>.</param>
    /// <param name="expected">The document used to derive the expected event identifier and payload.</param>
    /// <returns><see langword="true"/> when the event identifier, revision, and JSON value match.</returns>
    public static bool SameEvent(FoundEvent? actual, BenchmarkDocument expected)
    {
        const int FirstEventRevision = 1;

        ArgumentNullException.ThrowIfNull(expected);
        return actual is not null && actual.EventId == EventId(expected) && actual.Revision == FirstEventRevision && SameJson(actual.Json, expected.Json);
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
