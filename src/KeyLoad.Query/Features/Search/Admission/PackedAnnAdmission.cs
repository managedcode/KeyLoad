using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal readonly record struct PackedAnnAdmission(int Count, int Dimension, int VectorsPerBlock, int BlockCount,
    int BaseSlots, int UpperOffsetLength, int UpperSlots, long SumLevels, long RetainedBytes,
    long BuildScratchBytes)
{
    private const int MinimumNonEmptyCapacity = 1;
    private const int BaseLevelDegreeMultiplier = 2;
    private const int AdjacentElementOffset = 1;
    private const int InitialSequence = 0;
    private const int FirstElementIndex = 0;
    private const int EmptyElementCount = 0;
    private const int VectorSpaceIdentityPartCount = 3;
    private const int MinimumVectorDimension = 1;
    private const int MaximumVectorDimension = 4_096;

    private const string InvalidOptions = "The packed ANN options are invalid.";
    private const string ResourceExceeded = "The packed ANN resource bound is exceeded.";

    internal static PackedAnnAdmission Create(VectorSpace space, IReadOnlyList<VectorRecord> records,
        PackedAnnOptions options, AnnWorkBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ValidateOptions(options);
        ValidateSpace(space, budget);
        ArgumentNullException.ThrowIfNull(records);
        var count = records.Count;
        ValidateRecordCount(count, options);
        try
        {
            return Calculate(space, records, null, options, budget, count);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
    }

    internal static PackedAnnAdmission CreateRestored(VectorSpace space, string[] ids,
        PackedAnnOptions options, AnnWorkBudget budget)
    {
        ValidateOptions(options);
        ValidateSpace(space, budget);
        ValidateRecordCount(ids.Length, options);
        try
        {
            return Calculate(space, null, ids, options, budget, ids.Length);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
    }

    private static PackedAnnAdmission Calculate(VectorSpace space, IReadOnlyList<VectorRecord>? records, string[]? ids,
        PackedAnnOptions options, AnnWorkBudget budget, int count)
    {
        var dimension = space.Dimension;
        var vectorsPerBlock = Math.Max(MinimumNonEmptyCapacity, options.VectorBlockBytes / checked(dimension * sizeof(float)));
        var blocks = DivideRoundUp(count, vectorsPerBlock);
        var baseSlots = ArrayLength(checked((long)count * options.Connections * BaseLevelDegreeMultiplier));
        var offsetsLength = ArrayLength(checked((long)count + AdjacentElementOffset));
        var sumLevels = CalculateSumLevels(count, options, budget);
        var upperSlots = ArrayLength(checked(sumLevels * options.Connections));
        var identityBytes = records is not null ? PackedAnnIdentityReservations.FromRecords(records, budget)
            : PackedAnnIdentityReservations.FromIds(ids!, budget);
        var retained = CalculateRetainedBytes(identityBytes, space, count, baseSlots, offsetsLength, upperSlots,
            vectorsPerBlock, blocks, dimension);
        var buildScratch = CalculateBuildScratchBytes(count, options);
        if (retained > options.MaxIndexBytes || buildScratch > options.MaxScratchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
        return new(count, dimension, vectorsPerBlock, blocks, baseSlots, offsetsLength, upperSlots,
            sumLevels, retained, buildScratch);
    }

    private static long CalculateSumLevels(int count, PackedAnnOptions options, AnnWorkBudget budget)
    {
        long total = InitialSequence;
        for (var ordinal = FirstElementIndex; ordinal < count; ordinal++)
        {
            budget.Check();
            total = checked(total + PackedAnnLevels.For(options.Seed, ordinal, options.Connections,
                options.MaxLevel, budget));
        }
        return total;
    }

    private static long CalculateRetainedBytes(long identityBytes, VectorSpace space, int count,
        int baseSlots, int offsetsLength, int upperSlots, int vectorsPerBlock, int blocks, int dimension)
    {
        var bytes = PackedAnnReservations.FixedRetainedBytes;
        bytes = AddArray(bytes, sizeof(long), count);
        bytes = AddArray(bytes, sizeof(byte), count);
        bytes = AddArray(bytes, sizeof(int), baseSlots);
        bytes = AddArray(bytes, sizeof(int), offsetsLength);
        bytes = AddArray(bytes, sizeof(int), upperSlots);
        bytes = AddArray(bytes, sizeof(long), blocks);
        bytes = AddVectorBlocks(bytes, count, vectorsPerBlock, dimension);
        bytes = AddArray(bytes, sizeof(long), count);
        bytes = checked(bytes + identityBytes);
        bytes = checked(bytes + PackedAnnReservations.String(space.Id.Length)
            + PackedAnnReservations.String(space.Model.Length) + PackedAnnReservations.String(space.Version.Length));
        return bytes;
    }

    private static long AddVectorBlocks(long bytes, int count, int vectorsPerBlock, int dimension)
    {
        var fullBlocks = count / vectorsPerBlock;
        var remainder = count % vectorsPerBlock;
        var fullComponents = checked((long)vectorsPerBlock * dimension);
        ArrayLength(fullComponents);
        bytes = checked(bytes + fullBlocks * PackedAnnReservations.Array(sizeof(float), fullComponents));
        if (remainder > EmptyElementCount)
        {
            var components = checked((long)remainder * dimension);
            ArrayLength(components);
            bytes = checked(bytes + PackedAnnReservations.Array(sizeof(float), components));
        }
        return bytes;
    }

    private static long AddArray(long bytes, int width, long length)
    {
        ArrayLength(length);
        return checked(bytes + PackedAnnReservations.Array(width, length));
    }

    private static long CalculateBuildScratchBytes(int count, PackedAnnOptions options)
    {
        var ef = Math.Min(count, options.EfConstruction);
        var degree = count == EmptyElementCount ? EmptyElementCount : checked(options.Connections * BaseLevelDegreeMultiplier);
        return PackedAnnReservations.BuildScratch(count, ef, degree);
    }

    private static int DivideRoundUp(int value, int divisor)
        => value == EmptyElementCount ? EmptyElementCount : checked((value - AdjacentElementOffset) / divisor + AdjacentElementOffset);

    private static int ArrayLength(long length)
    {
        if (length < InitialSequence || length > Array.MaxLength)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
        return checked((int)length);
    }

    internal static void ValidateSpace(VectorSpace space, AnnWorkBudget budget)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(budget);
        if (space.Id is null || space.Model is null || space.Version is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidOptions);
        }
        budget.Charge(checked((long)space.Id.Length + space.Model.Length + space.Version.Length + VectorSpaceIdentityPartCount));
        JsonData.Identifier(space.Id);
        JsonData.Identifier(space.Model);
        JsonData.Identifier(space.Version);
        if (space.Dimension is < MinimumVectorDimension or > MaximumVectorDimension || !Enum.IsDefined(space.Metric))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidOptions);
        }
    }

    internal static void ValidateOptions(PackedAnnOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
    }

    internal static void ValidateRecordCount(int count, PackedAnnOptions options)
    {
        if (count > options.MaxRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
    }
}
