using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal readonly record struct PackedAnnAdmission(int Count, int Dimension, int VectorsPerBlock, int BlockCount,
    int BaseSlots, int UpperOffsetLength, int UpperSlots, long SumLevels, long RetainedBytes,
    long BuildScratchBytes)
{
    private const string InvalidOptions = "The packed ANN options are invalid.";
    private const string ResourceExceeded = "The packed ANN resource bound is exceeded.";
    private const int MaximumRecords = 5_000_000;
    private const long MaximumIndexBytes = 8_589_934_592;
    private const long MaximumScratchBytes = 268_435_456;
    private const int BlockBytes = 65_536;

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
            return Calculate(space, records, options, budget, count);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
    }

    private static PackedAnnAdmission Calculate(VectorSpace space, IReadOnlyList<VectorRecord> records,
        PackedAnnOptions options, AnnWorkBudget budget, int count)
    {
        var dimension = space.Dimension;
        var vectorsPerBlock = Math.Max(1, BlockBytes / checked(dimension * sizeof(float)));
        var blocks = DivideRoundUp(count, vectorsPerBlock);
        var baseSlots = ArrayLength(checked((long)count * options.Connections * 2));
        var offsetsLength = ArrayLength(checked((long)count + 1));
        var sumLevels = CalculateSumLevels(count, options, budget);
        var upperSlots = ArrayLength(checked(sumLevels * options.Connections));
        var retained = CalculateRetainedBytes(records, space, count, baseSlots, offsetsLength, upperSlots,
            vectorsPerBlock, blocks, dimension, budget);
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
        long total = 0;
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            budget.Check();
            total = checked(total + PackedAnnLevels.For(options.Seed, ordinal, options.Connections,
                options.MaxLevel, budget));
        }
        return total;
    }

    private static long CalculateRetainedBytes(IReadOnlyList<VectorRecord> records, VectorSpace space, int count,
        int baseSlots, int offsetsLength, int upperSlots, int vectorsPerBlock, int blocks, int dimension,
        AnnWorkBudget budget)
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
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            budget.Check();
            var id = records[ordinal].DocumentId;
            budget.Charge(checked(id.Length + 1L));
            bytes = checked(bytes + PackedAnnReservations.String(id.Length));
        }
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
        if (remainder > 0)
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
        var degree = count == 0 ? 0 : checked(options.Connections * 2);
        return PackedAnnReservations.BuildScratch(count, ef, degree);
    }

    private static int DivideRoundUp(int value, int divisor)
        => value == 0 ? 0 : checked((value - 1) / divisor + 1);

    private static int ArrayLength(long length)
    {
        if (length < 0 || length > Array.MaxLength)
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
        budget.Charge(checked((long)space.Id.Length + space.Model.Length + space.Version.Length + 3));
        JsonData.Identifier(space.Id);
        JsonData.Identifier(space.Model);
        JsonData.Identifier(space.Version);
        if (space.Dimension is < 1 or > 4_096 || !Enum.IsDefined(space.Metric))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidOptions);
        }
    }

    internal static void ValidateOptions(PackedAnnOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Connections is not (4 or 8 or 16 or 32 or 64)
            || options.EfConstruction < options.Connections || options.EfConstruction > 4_096
            || options.EfSearch is < 1 or > 4_096 || options.MaxLevel is < 1 or > 16
            || options.ExactThreshold is < 0 or > 4_096 || options.MaxRecords is < 1 or > MaximumRecords
            || options.MaxIndexBytes is < 1_024 or > MaximumIndexBytes
            || options.MaxScratchBytes is < 1_024 or > MaximumScratchBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidOptions);
        }
    }

    internal static void ValidateRecordCount(int count, PackedAnnOptions options)
    {
        if (count > options.MaxRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceExceeded);
        }
    }
}
