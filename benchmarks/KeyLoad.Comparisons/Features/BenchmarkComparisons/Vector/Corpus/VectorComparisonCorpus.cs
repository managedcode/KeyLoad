using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Comparisons;

/// <summary>Produces profile-bound vectors and the independent streaming exact-search oracle.</summary>
public sealed class VectorComparisonCorpus(VectorComparisonProfile profile)
{
    public VectorComparisonProfile Profile { get; } = profile ?? throw new ArgumentNullException(nameof(profile));

    public VectorDocument Create(int number, bool updated = false)
    {
        if ((uint)number >= (uint)Profile.RecordCount) throw new ArgumentOutOfRangeException(nameof(number));
        var vector = CreateEmbedding(number, updated);
        var id = "v" + number.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var prefix = Encoding.ASCII.GetBytes($"{{\"id\":\"{id}\",\"number\":{number},\"padding\":\"");
        var payload = Encoding.ASCII.GetString(prefix) + new string('x', Profile.PayloadBytes - prefix.Length - 2) + "\"}";
        return new(number, id, vector, payload);
    }

    public IEnumerable<VectorDocument> StreamDocuments()
    {
        for (var i = 0; i < Profile.RecordCount; i++) yield return Create(i);
    }

    public float[] CreateEmbedding(int number, bool updated = false)
    {
        if ((uint)number >= (uint)Profile.RecordCount) throw new ArgumentOutOfRangeException(nameof(number));
        var seed = unchecked((ulong)(uint)Profile.Seed << 32) ^ (uint)number ^ (updated ? 0xd1b54a32d192ed03UL : 0UL);
        return CreateUnitVector(seed, Profile.Dimensions);
    }

    public IReadOnlyList<ReadOnlyMemory<float>> CreateQueries()
        => Enumerable.Range(0, Profile.QueryVectorCount)
            .Select(index => (ReadOnlyMemory<float>)CreateUnitVector(0x9e3779b97f4a7c15UL ^ (uint)index ^ (uint)Profile.Seed, Profile.Dimensions))
            .ToArray();

    public VectorUpdate CreateUpdate(int ordinal)
    {
        if (Profile.UpdateCount == 0 || (uint)ordinal >= (uint)Profile.UpdateCount)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        var mutableSlots = Profile.RecordCount / 10;
        var slot = (int)((long)ordinal * (mutableSlots - 1) / (Profile.UpdateCount - 1));
        var number = 9 + slot * 10;
        var document = Create(number, updated: true);
        return new(number, document.Id, document.Embedding);
    }

    /// <summary>Computes exact cosine neighbors while retaining only top K for one query.</summary>
    public IReadOnlyList<VectorNeighbor> ExactNeighbors(ReadOnlyMemory<float> query,
        CancellationToken cancellationToken = default)
    {
        if (query.Length != Profile.Dimensions) throw new ArgumentException("Query dimensions do not match profile.", nameof(query));
        var top = new PriorityQueue<VectorNeighbor, (double Similarity, int ReverseNumber)>();
        Span<float> vector = stackalloc float[Profile.Dimensions];
        var queryNorm = 0d;
        foreach (var component in query.Span) queryNorm += component * component;
        for (var number = 0; number < Profile.RecordCount; number++)
        {
            if ((number & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (!Eligible(number)) continue;
            FillEmbedding(number, vector);
            var dot = 0d;
            var vectorNorm = 0d;
            for (var dimension = 0; dimension < vector.Length; dimension++)
            {
                dot += vector[dimension] * query.Span[dimension];
                vectorNorm += vector[dimension] * vector[dimension];
            }
            var cosine = dot / Math.Sqrt(vectorNorm * queryNorm);
            // PriorityQueue's minimum is the worst item: lower similarity and then later id.
            var priority = (cosine, -number);
            if (top.Count < Profile.TopK) top.Enqueue(ToNeighbor(number, cosine), priority);
            else if (top.TryPeek(out _, out var worst) && priority.CompareTo(worst) > 0)
            { top.Dequeue(); top.Enqueue(ToNeighbor(number, cosine), priority); }
        }
        return top.UnorderedItems.Select(item => item.Element)
            .OrderBy(item => item.Distance).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    public bool Eligible(int number) => Profile.QueryMode switch
    {
        VectorQueryMode.Filtered => number % 100 == 0,
        VectorQueryMode.Mixed => number % 10 != 9,
        _ => true
    };

    public static string HashVector(ReadOnlySpan<float> vector)
    {
        Span<byte> bytes = stackalloc byte[vector.Length * sizeof(float)];
        for (var i = 0; i < vector.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(i * 4, 4), BitConverter.SingleToInt32Bits(vector[i]));
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, digest);
        return Convert.ToHexStringLower(digest);
    }

    private static VectorNeighbor ToNeighbor(int number, double cosine)
        => new("v" + number.ToString("D9", System.Globalization.CultureInfo.InvariantCulture), 1d - cosine);

    public static float[] CreateUnitVector(ulong state, int dimensions)
    {
        var values = new float[dimensions];
        FillUnitVector(state, values);
        return values;
    }

    private void FillEmbedding(int number, Span<float> destination)
    {
        if (destination.Length != Profile.Dimensions) throw new ArgumentException("Embedding dimensions do not match profile.", nameof(destination));
        var seed = unchecked((ulong)(uint)Profile.Seed << 32) ^ (uint)number;
        FillUnitVector(seed, destination);
    }

    private static void FillUnitVector(ulong state, Span<float> values)
    {
        var sum = 0d;
        for (var i = 0; i < values.Length; i++)
        {
            state += 0x9e3779b97f4a7c15UL;
            var value = state;
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
            value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
            value ^= value >> 31;
            var component = ((value >> 40) / (float)(1 << 24)) * 2f - 1f;
            values[i] = component;
            sum += component * component;
        }
        var norm = Math.Sqrt(sum);
        for (var i = 0; i < values.Length; i++) values[i] = (float)(values[i] / norm);
    }
}
