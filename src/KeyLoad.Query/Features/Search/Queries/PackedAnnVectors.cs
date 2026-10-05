namespace KeyLoad.Query.Features.Search;

internal sealed class PackedAnnVectors
{
    private const int MaximumDimension = 4_096;
    private const string InvalidVectors = "The packed ANN vectors are invalid.";
    private const string InvalidCopiedVector = "The copied ANN vector contains a nonfinite component.";
    private readonly float[][] blocks;
    private readonly int vectorsPerBlock;
    private readonly int dimension;

    private PackedAnnVectors(float[][] blocks, int count, int dimension, int vectorsPerBlock)
    {
        this.blocks = blocks;
        Count = count;
        this.vectorsPerBlock = vectorsPerBlock;
        this.dimension = dimension;
    }

    internal int Count { get; }
    internal int Dimension => dimension;

    internal ReadOnlySpan<float> Span(int ordinal)
    {
        ValidateOrdinal(ordinal);
        return blocks[ordinal / vectorsPerBlock].AsSpan(ordinal % vectorsPerBlock * dimension, dimension);
    }

    internal ReadOnlyMemory<float> Memory(int ordinal)
    {
        ValidateOrdinal(ordinal);
        return new(blocks[ordinal / vectorsPerBlock], ordinal % vectorsPerBlock * dimension, dimension);
    }

    internal static PackedAnnVectors Copy(IReadOnlyList<VectorRecord> records, PackedAnnAdmission layout, AnnWorkBudget budget)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(budget);
        if (records.Count != layout.Count || layout.Dimension is < 1 or > MaximumDimension)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVectors);
        }
        var components = checked((long)layout.Count * layout.Dimension);
        budget.Charge(checked(components * 2));
        var blocks = AllocateBlocks(layout, budget);
        var total = components;
        for (long vectorOffset = 0; vectorOffset < total; vectorOffset += layout.Dimension)
        {
            budget.Check();
            var ordinal = checked((int)(vectorOffset / layout.Dimension));
            var record = records[ordinal];
            if (record is null || record.Values.IsDefault || record.Values.Length != layout.Dimension)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidVectors);
            }
            var source = record.Values.AsSpan();
            var block = blocks[ordinal / layout.VectorsPerBlock];
            var target = block.AsSpan(ordinal % layout.VectorsPerBlock * layout.Dimension, layout.Dimension);
            source.CopyTo(target);
            for (var component = 0; component < target.Length; component++)
            {
                if ((component & 255) == 0)
                {
                    budget.Check();
                }
                if (!float.IsFinite(target[component]))
                {
                    throw Errors.Fail(ErrorCode.Validation, InvalidCopiedVector);
                }
            }
        }
        return new(blocks, layout.Count, layout.Dimension, layout.VectorsPerBlock);
    }

    private void ValidateOrdinal(int ordinal)
    {
        if (dimension is < 1 or > MaximumDimension || (uint)ordinal >= (uint)Count)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVectors);
        }
    }

    private static float[][] AllocateBlocks(PackedAnnAdmission layout, AnnWorkBudget budget)
    {
        budget.Charge(layout.BlockCount);
        var blocks = new float[layout.BlockCount][];
        var remaining = layout.Count;
        for (var index = 0; index < blocks.Length; index++)
        {
            budget.Charge(1);
            var vectors = Math.Min(layout.VectorsPerBlock, remaining);
            blocks[index] = new float[checked(vectors * layout.Dimension)];
            remaining -= vectors;
        }
        return blocks;
    }
}
