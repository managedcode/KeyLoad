using System.Buffers.Binary;
using System.Collections;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons;

/// <summary>Generates a scaled corpus by index without retaining per-record objects.</summary>
public sealed class ScaledComparisonCorpus : IComparisonCorpus
{
    private const string DocumentNumberFormat = "D9";

    private readonly ScaledComparisonProfile profile;
    private readonly DocumentView documents;
    private readonly EmptyEdgeView edges = new();

    /// <summary>Creates a lazy scaled corpus and its ordered digest.</summary>
    /// <param name="profile">One of the exact accepted scaled profiles.</param>
    public ScaledComparisonCorpus(ScaledComparisonProfile profile)
    {
        this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        documents = new DocumentView(this);
        Sha256 = ComputeDigest();
    }

    /// <inheritdoc />
    public IComparisonSettings Settings => profile;
    /// <inheritdoc />
    public IReadOnlyList<BenchmarkDocument> Documents => documents;
    /// <inheritdoc />
    public IReadOnlyList<BenchmarkEdge> Edges => edges;
    /// <inheritdoc />
    public int GraphVertexCount => 0;
    /// <inheritdoc />
    public string Sha256 { get; }

    /// <inheritdoc />
    public BenchmarkDocument CreateDocument(int number)
    {
        if ((uint)number >= (uint)profile.Documents)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        var id = Id(number);
        var empty = EmptyJson(id, number);
        var paddingLength = profile.PayloadBytes - Encoding.UTF8.GetByteCount(empty);
        var json = JsonSerializer.Serialize(new { id, number, text = "KeyLoad shared corpus", padding = new string('x', paddingLength) });
        if (Encoding.UTF8.GetByteCount(json) != profile.PayloadBytes)
        {
            throw new InvalidOperationException("Scaled corpus payload size mismatch.");
        }

        return new(number, id, json, ImmutableArray<float>.Empty);
    }

    /// <inheritdoc />
    public BenchmarkDocument Input(Scenario scenario, int repetition, int operation, bool warmup)
    {
        if (repetition != 0)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(repetition, 0);
        }
        if (operation < 0 || operation >= (warmup ? profile.Warmup : profile.Operations))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        if (scenario == Scenario.PointRead)
        {
            var selected = unchecked((uint)profile.Seed + (uint)operation * 2654435761u) % (uint)profile.Documents;
            return CreateDocument((int)selected);
        }

        var block = scenario switch
        {
            Scenario.DocumentWrite => 0,
            Scenario.DocumentUpdate => 1,
            Scenario.DocumentDelete => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var ordinal = warmup ? operation : profile.Warmup + operation;
        return CreateMutationDocument(checked(profile.Documents + block * (profile.Operations + profile.Warmup) + ordinal));
    }

    /// <summary>Creates one deterministic document in the disjoint mutation identity range.</summary>
    /// <param name="number">The full numeric identity, including the corpus offset.</param>
    /// <returns>The generated mutation input with the frozen payload size.</returns>
    public BenchmarkDocument CreateMutationDocument(int number)
    {
        if (number < profile.Documents || number >= checked(profile.Documents + 3 * (profile.Operations + profile.Warmup)))
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        var id = Id(number);
        var empty = EmptyJson(id, number);
        var json = JsonSerializer.Serialize(new { id, number, text = "KeyLoad shared corpus", padding = new string('x', profile.PayloadBytes - Encoding.UTF8.GetByteCount(empty)) });
        if (Encoding.UTF8.GetByteCount(json) != profile.PayloadBytes)
        {
            throw new InvalidOperationException("Scaled mutation payload size mismatch.");
        }
        return new(number, id, json, ImmutableArray<float>.Empty);
    }

    private string ComputeDigest()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var length = new byte[sizeof(int)];
        for (var number = 0; number < profile.Documents; number++)
        {
            var document = CreateDocument(number);
            Append(hash, length, document.Id);
            Append(hash, length, document.Json);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, byte[] length, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    internal static string EmptyJson(string id, int number) => JsonSerializer.Serialize(new { id, number, text = "KeyLoad shared corpus", padding = string.Empty });

    internal static string Id(int number) => "d" + number.ToString(DocumentNumberFormat, System.Globalization.CultureInfo.InvariantCulture);

    private sealed class DocumentView(ScaledComparisonCorpus owner) : IReadOnlyList<BenchmarkDocument>
    {
        public int Count => owner.profile.Documents;
        public BenchmarkDocument this[int index] => owner.CreateDocument(index);
        public IEnumerator<BenchmarkDocument> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return owner.CreateDocument(index);
            }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class EmptyEdgeView : IReadOnlyList<BenchmarkEdge>
    {
        public int Count => 0;
        public BenchmarkEdge this[int index] => throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<BenchmarkEdge> GetEnumerator() => Enumerable.Empty<BenchmarkEdge>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
