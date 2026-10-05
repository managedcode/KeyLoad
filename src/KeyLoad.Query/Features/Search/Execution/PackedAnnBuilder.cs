using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class PackedAnnBuilder
{
    private const string InvalidRecords = "The packed ANN source records are invalid or unordered.";
    private const string LevelsChanged = "The deterministic ANN levels changed during admitted construction.";

    internal static PackedAnnState Build(VectorSpace space, IReadOnlyList<VectorRecord> records,
        PackedAnnOptions options, AnnWorkBudget budget)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        PackedAnnAdmission.ValidateOptions(options);
        PackedAnnAdmission.ValidateRecordCount(records.Count, options);
        PackedAnnAdmission.ValidateSpace(space, budget);
        ValidateRecords(space, records, budget);
        var plan = PackedAnnAdmission.Create(space, records, options, budget);
        budget.Charge(checked((long)plan.BaseSlots + plan.UpperSlots));
        var ownedSpace = CopySpace(space, budget);
        var ids = CopyIds(records, budget);
        ValidateOwnedIds(ids, budget);
        var revisions = CopyRevisions(records, budget);
        budget.Charge(checked((long)plan.Count * 2 + 1));
        var levels = new byte[plan.Count];
        var upperOffsets = new int[plan.UpperOffsetLength];
        FillLevelsAndOffsets(plan, options, levels, upperOffsets, budget);
        var graph = new PackedAnnGraph(plan.Count, options.Connections, levels, upperOffsets,
            plan.BaseSlots, plan.UpperSlots);
        var vectors = PackedAnnVectors.Copy(records, plan, budget);
        var entryPoint = -1;
        var maximumLevel = -1;
        var scratch = new PackedAnnBuildScratch(plan.Count, options, budget);
        for (var ordinal = 0; ordinal < plan.Count; ordinal++)
        {
            budget.Check();
            var level = levels[ordinal];
            PackedAnnConstruction.Insert(graph, vectors, ownedSpace, ordinal, level,
                ref entryPoint, ref maximumLevel, scratch, budget);
        }
        return new(ownedSpace, ids, revisions, levels, graph, vectors, entryPoint, maximumLevel, options,
            plan.RetainedBytes, plan.BuildScratchBytes);
    }

    private static void FillLevelsAndOffsets(PackedAnnAdmission plan, PackedAnnOptions options,
        byte[] levels, int[] upperOffsets, AnnWorkBudget budget)
    {
        long sumLevels = 0;
        long edgeOffset = 0;
        for (var ordinal = 0; ordinal < levels.Length; ordinal++)
        {
            budget.Charge(1);
            var level = PackedAnnLevels.For(options.Seed, ordinal, options.Connections, options.MaxLevel, budget);
            levels[ordinal] = checked((byte)level);
            sumLevels = checked(sumLevels + level);
            edgeOffset = checked(edgeOffset + (long)level * options.Connections);
            upperOffsets[ordinal + 1] = checked((int)edgeOffset);
        }
        if (sumLevels != plan.SumLevels || edgeOffset != plan.UpperSlots)
        {
            throw new InvalidOperationException(LevelsChanged);
        }
    }

    private static void ValidateRecords(VectorSpace space, IReadOnlyList<VectorRecord> records, AnnWorkBudget budget)
    {
        string? previousId = null;
        string? field = null;
        for (var ordinal = 0; ordinal < records.Count; ordinal++)
        {
            budget.Check();
            var record = records[ordinal] ?? throw Errors.Fail(ErrorCode.Validation, InvalidRecords);
            budget.Charge(checked((long)space.Id.Length + space.Model.Length + space.Version.Length + 3));
            if (record.Values.IsDefault || record.Space != space || record.DocumentRevision <= 0
                || record.Values.Length != space.Dimension)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRecords);
            }
            CheckIdentifier(record.DocumentId, budget);
            CheckIdentifier(record.Field, budget);
            if (previousId is not null && StringComparer.Ordinal.Compare(previousId, record.DocumentId) >= 0
                || field is not null && !StringComparer.Ordinal.Equals(field, record.Field))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRecords);
            }
            field = record.Field;
            previousId = record.DocumentId;
            ValidateValues(record.Values.AsSpan(), budget);
        }
    }

    private static void ValidateValues(ReadOnlySpan<float> values, AnnWorkBudget budget)
    {
        for (var index = 0; index < values.Length; index++)
        {
            budget.Charge(1);
            if (!float.IsFinite(values[index]))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRecords);
            }
        }
    }

    private static void CheckIdentifier(string value, AnnWorkBudget budget)
    {
        if (value is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRecords);
        }
        budget.Charge(checked(value.Length + 1L));
        JsonData.Identifier(value);
    }

    private static VectorSpace CopySpace(VectorSpace space, AnnWorkBudget budget)
    {
        ChargeSpace(space, budget);
        return space with { Id = new(space.Id.AsSpan()), Model = new(space.Model.AsSpan()), Version = new(space.Version.AsSpan()) };
    }

    private static void ChargeSpace(VectorSpace space, AnnWorkBudget budget)
    {
        CheckIdentifier(space.Id, budget);
        CheckIdentifier(space.Model, budget);
        CheckIdentifier(space.Version, budget);
    }

    private static string[] CopyIds(IReadOnlyList<VectorRecord> records, AnnWorkBudget budget)
    {
        for (var ordinal = 0; ordinal < records.Count; ordinal++)
        {
            budget.Charge(checked(records[ordinal].DocumentId.Length + 1L));
        }
        var ids = new string[records.Count];
        for (var ordinal = 0; ordinal < ids.Length; ordinal++)
        {
            budget.Check();
            ids[ordinal] = new(records[ordinal].DocumentId.AsSpan());
        }
        return ids;
    }

    private static void ValidateOwnedIds(string[] ids, AnnWorkBudget budget)
    {
        for (var ordinal = 1; ordinal < ids.Length; ordinal++)
        {
            budget.Charge(checked((long)ids[ordinal - 1].Length + ids[ordinal].Length));
            if (StringComparer.Ordinal.Compare(ids[ordinal - 1], ids[ordinal]) >= 0)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRecords);
            }
        }
    }

    private static long[] CopyRevisions(IReadOnlyList<VectorRecord> records, AnnWorkBudget budget)
    {
        budget.Charge(records.Count);
        var revisions = new long[records.Count];
        for (var ordinal = 0; ordinal < revisions.Length; ordinal++)
        {
            budget.Check();
            revisions[ordinal] = records[ordinal].DocumentRevision;
        }
        return revisions;
    }
}
