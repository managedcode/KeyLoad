namespace KeyLoad.Query.Search;

internal sealed class PackedAnnVectors
{
    private const int MinimumPositiveCount = 1;
    private const int CopyAndValidateWorkPerComponent = 2;
    private const int FirstElementIndex = 0;
    private const int BudgetCheckRemainder = 0;
    private const int SingleWorkUnit = 1;

    private const int MaximumDimension = 4_096;
    private const string InvalidVectors = "The packed ANN vectors are invalid.";
    private const string InvalidCopiedVector = "The copied ANN vector contains a nonfinite component.";
    private readonly float[][] blocks;
    private readonly int vectorsPerBlock;
    private readonly int dimension;
    private readonly double[] squaredNorms;

    private PackedAnnVectors(float[][] blocks, double[] squaredNorms, int count, int dimension, int vectorsPerBlock)
    {
        this.blocks = blocks;
        Count = count;
        this.vectorsPerBlock = vectorsPerBlock;
        this.dimension = dimension;
        this.squaredNorms = squaredNorms;
    }

    internal int Count
    {
        get;
    }
    internal int Dimension => dimension;

    internal double SquaredNorm(int ordinal)
    {
        ValidateOrdinal(ordinal);
        if (squaredNorms.Length != Count)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVectors);
        }
        return squaredNorms[ordinal];
    }

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

    internal static PackedAnnVectors Copy(IReadOnlyList<VectorRecord> records, PackedAnnAdmission layout, AnnWorkBudget budget, int budgetCheckInterval)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(budget);
        if (records.Count != layout.Count || layout.Dimension is < MinimumPositiveCount or > MaximumDimension)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVectors);
        }
        var components = checked((long)layout.Count * layout.Dimension);
        budget.Charge(checked(components * CopyAndValidateWorkPerComponent));
        var blocks = AllocateBlocks(layout, budget);
        var squaredNorms = AllocateNorms(layout, budget);
        var total = components;
        for (long vectorOffset = FirstElementIndex; vectorOffset < total; vectorOffset += layout.Dimension)
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
            for (var component = FirstElementIndex; component < target.Length; component++)
            {
                if (component % budgetCheckInterval == BudgetCheckRemainder)
                {
                    budget.Check();
                }
                if (!float.IsFinite(target[component]))
                {
                    throw Errors.Fail(ErrorCode.Validation, InvalidCopiedVector);
                }
            }
            if (squaredNorms.Length != 0)
            {
                budget.Charge(target.Length);
                squaredNorms[ordinal] = SimilarityMetricMath.Norm(target);
            }
        }
        return new(blocks, squaredNorms, layout.Count, layout.Dimension, layout.VectorsPerBlock);
    }

    internal static PackedAnnVectors Restore(PackedAnnAdmission layout, AnnWorkBudget budget)
        => new(AllocateBlocks(layout, budget), AllocateNorms(layout, budget), layout.Count, layout.Dimension, layout.VectorsPerBlock);

    internal void RestoreNode(int ordinal, ReadOnlySpan<float> source, AnnWorkBudget budget)
    {
        ValidateOrdinal(ordinal);
        if (source.Length != dimension)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidVectors);
        }
        budget.Charge(source.Length);
        var target = blocks[ordinal / vectorsPerBlock].AsSpan(ordinal % vectorsPerBlock * dimension, dimension);
        source.CopyTo(target);
        if (squaredNorms.Length != 0)
        {
            budget.Charge(source.Length);
            squaredNorms[ordinal] = SimilarityMetricMath.Norm(target);
        }
    }

    private static double[] AllocateNorms(PackedAnnAdmission layout, AnnWorkBudget budget)
    {
        if (layout.Metric != DistanceMetric.Cosine)
        {
            return [];
        }
        budget.Charge(layout.Count);
        return new double[layout.Count];
    }

    private void ValidateOrdinal(int ordinal)
    {
        if (dimension is < MinimumPositiveCount or > MaximumDimension || (uint)ordinal >= (uint)Count)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVectors);
        }
    }

    private static float[][] AllocateBlocks(PackedAnnAdmission layout, AnnWorkBudget budget)
    {
        budget.Charge(layout.BlockCount);
        var blocks = new float[layout.BlockCount][];
        var remaining = layout.Count;
        for (var index = FirstElementIndex; index < blocks.Length; index++)
        {
            budget.Charge(SingleWorkUnit);
            var vectors = Math.Min(layout.VectorsPerBlock, remaining);
            blocks[index] = new float[checked(vectors * layout.Dimension)];
            remaining -= vectors;
        }
        return blocks;
    }
}
