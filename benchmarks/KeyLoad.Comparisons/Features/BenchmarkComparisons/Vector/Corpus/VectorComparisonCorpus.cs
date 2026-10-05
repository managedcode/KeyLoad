using System.Buffers.Binary;
using System.Security.Cryptography;

namespace KeyLoad.Comparisons;

/// <summary>Produces profile-bound vectors and the independent streaming exact-search oracle.</summary>
/// <param name="profile">The exact immutable profile.</param>
public sealed class VectorComparisonCorpus(VectorComparisonProfile profile)
{
    /// <summary>Gets the profile that defines this corpus.</summary>
    public VectorComparisonProfile Profile { get; } = profile ?? throw new ArgumentNullException(nameof(profile));

    /// <summary>Creates one deterministic record without retaining it in the corpus.</summary>
    /// <param name="number">The zero-based record number.</param>
    /// <param name="updated">Whether to generate the deterministic replacement vector.</param>
    /// <returns>The record with its exact payload and vector.</returns>
    public VectorDocument Create(int number, bool updated = false)
    {
        if ((uint)number >= (uint)Profile.RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        var vector = CreateEmbedding(number, updated);
        var id = VectorComparisonCorpusValues.VectorIdPrefix + number.ToString(VectorProfileTokens.NumberFormat, System.Globalization.CultureInfo.InvariantCulture);
        var prefix = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{VectorComparisonCorpusValues.CanonicalPayloadPrefix}{id}{VectorComparisonCorpusValues.CanonicalNumberPrefix}{number}{VectorComparisonCorpusValues.CanonicalPaddingPrefix}");
        var payload = prefix + new string(VectorComparisonCorpusValues.PayloadPaddingCharacter, Profile.PayloadBytes - prefix.Length - VectorComparisonCorpusValues.PayloadSuffixCharacterCount) + VectorComparisonCorpusValues.CanonicalPayloadSuffix;
        return new(number, id, vector, payload);
    }

    /// <summary>Streams the profile's records lazily.</summary>
    /// <returns>The complete lazy corpus stream.</returns>
    public IEnumerable<VectorDocument> StreamDocuments()
    {
        for (var i = VectorComparisonCorpusValues.FirstIndex; i < Profile.RecordCount; i++)
        {
            yield return Create(i);
        }
    }

    /// <summary>Creates one deterministic vector for a record number.</summary>
    /// <param name="number">The zero-based record number.</param>
    /// <param name="updated">Whether to generate the replacement embedding.</param>
    /// <returns>The normalized float32 vector.</returns>
    public float[] CreateEmbedding(int number, bool updated = false)
    {
        if ((uint)number >= (uint)Profile.RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        var seed = unchecked((ulong)(uint)Profile.Seed << VectorComparisonCorpusValues.SeedShift) ^ (uint)number ^ (updated ? VectorComparisonCorpusValues.UpdatedEmbeddingMask : VectorComparisonCorpusValues.UnchangedEmbeddingMask);
        return CreateUnitVector(seed, Profile.Dimensions);
    }

    /// <summary>Creates the 64 fixed query vectors.</summary>
    /// <returns>The query vectors in deterministic order.</returns>
    public IReadOnlyList<ReadOnlyMemory<float>> CreateQueries()
        => Enumerable.Range(VectorComparisonCorpusValues.FirstIndex, Profile.QueryVectorCount)
            .Select(index => (ReadOnlyMemory<float>)CreateUnitVector(VectorComparisonCorpusValues.SplitMixIncrement ^ (uint)index ^ (uint)Profile.Seed, Profile.Dimensions))
            .ToArray();

    /// <summary>Creates one deterministic replacement for a record outside the stable mixed-query set.</summary>
    /// <param name="ordinal">The zero-based update ordinal.</param>
    /// <returns>The record number, ID and replacement embedding.</returns>
    public VectorUpdate CreateUpdate(int ordinal)
    {
        if (Profile.UpdateCount == VectorComparisonCorpusValues.FirstIndex || (uint)ordinal >= (uint)Profile.UpdateCount)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        }

        var mutableSlots = Profile.RecordCount / VectorComparisonCorpusValues.MutableGroupWidth;
        var slot = (int)(((ulong)(uint)Profile.Seed + (ulong)(uint)ordinal * VectorComparisonCorpusValues.MixedUpdateStride) % (ulong)mutableSlots);
        var number = VectorComparisonCorpusValues.MutableRecordSuffix + slot * VectorComparisonCorpusValues.MutableGroupWidth;
        var id = VectorComparisonCorpusValues.VectorIdPrefix + number.ToString(VectorProfileTokens.NumberFormat, System.Globalization.CultureInfo.InvariantCulture);
        return new(number, id, CreateEmbedding(number, updated: true));
    }

    /// <summary>Finds exact cosine neighbors for one query.</summary>
    /// <param name="query">The finite nonzero query vector.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>The ordered exact neighbors.</returns>
    public IReadOnlyList<VectorNeighbor> ExactNeighbors(ReadOnlyMemory<float> query,
        CancellationToken cancellationToken = default)
        => ExactNeighborsBatch([query], cancellationToken)[VectorComparisonCorpusValues.FirstIndex];

    /// <summary>Scans the corpus once for a bounded set of exact reference queries.</summary>
    /// <param name="queries">At most the profile's query-vector count.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>Exact neighbors in query order.</returns>
    public IReadOnlyList<IReadOnlyList<VectorNeighbor>> ExactNeighborsBatch(
        IReadOnlyList<ReadOnlyMemory<float>> queries, CancellationToken cancellationToken = default)
        => VectorExactOracle.Compute(this, queries, cancellationToken);

    /// <summary>Returns the canonical eligibility predicate for a corpus number.</summary>
    /// <param name="number">The corpus number.</param>
    /// <returns>Whether the number can participate in the query mode.</returns>
    public bool Eligible(int number) => Profile.QueryMode switch
    {
        VectorQueryMode.Filtered => number % VectorComparisonCorpusValues.FilterModulo == VectorComparisonCorpusValues.FirstIndex,
        VectorQueryMode.Mixed => number % VectorComparisonCorpusValues.MutableGroupWidth != VectorComparisonCorpusValues.MutableRecordSuffix,
        _ => true
    };

    /// <summary>Computes the canonical SHA256 over float32 bits in little-endian dimension order.</summary>
    /// <param name="vector">The vector to hash.</param>
    /// <returns>Lowercase hexadecimal SHA256.</returns>
    public static string HashVector(ReadOnlySpan<float> vector)
    {
        Span<byte> bytes = stackalloc byte[vector.Length * sizeof(float)];
        for (var i = VectorComparisonCorpusValues.FirstIndex; i < vector.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(i * VectorComparisonCorpusValues.FloatByteCount, VectorComparisonCorpusValues.FloatByteCount), BitConverter.SingleToInt32Bits(vector[i]));
        }

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, digest);
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>Creates a deterministic normalized float32 unit vector.</summary>
    /// <param name="state">The stable SplitMix64 state.</param>
    /// <param name="dimensions">The number of dimensions.</param>
    /// <returns>The normalized vector.</returns>
    public static float[] CreateUnitVector(ulong state, int dimensions)
    {
        var values = new float[dimensions];
        FillUnitVector(state, values);
        return values;
    }

    internal void FillEmbedding(int number, Span<float> destination)
    {
        if (destination.Length != Profile.Dimensions)
        {
            throw new ArgumentException(VectorComparisonCorpusValues.EmbeddingDimensionsDoNotMatchProfile, nameof(destination));
        }

        var seed = unchecked((ulong)(uint)Profile.Seed << VectorComparisonCorpusValues.SeedShift) ^ (uint)number;
        FillUnitVector(seed, destination);
    }

    private static void FillUnitVector(ulong state, Span<float> values)
    {
        var sum = VectorComparisonCorpusValues.ZeroSquaredNorm;
        for (var i = VectorComparisonCorpusValues.FirstIndex; i < values.Length; i++)
        {
            state += VectorComparisonCorpusValues.SplitMixIncrement;
            var value = state;
            value = (value ^ (value >> VectorComparisonCorpusValues.FirstMixShift)) * VectorComparisonCorpusValues.SplitMixFirstMultiplier;
            value = (value ^ (value >> VectorComparisonCorpusValues.SecondMixShift)) * VectorComparisonCorpusValues.SplitMixSecondMultiplier;
            value ^= value >> VectorComparisonCorpusValues.FinalMixShift;
            var component = ((value >> VectorComparisonCorpusValues.FractionShift) / (float)(VectorComparisonCorpusValues.FractionLeadingBit << VectorComparisonCorpusValues.FractionBits)) * VectorComparisonCorpusValues.UnitVectorScale - VectorComparisonCorpusValues.UnitVectorOffset;
            values[i] = component;
            sum += component * component;
        }
        var norm = Math.Sqrt(sum);
        for (var i = VectorComparisonCorpusValues.FirstIndex; i < values.Length; i++)
        {
            values[i] = (float)(values[i] / norm);
        }
    }
}
