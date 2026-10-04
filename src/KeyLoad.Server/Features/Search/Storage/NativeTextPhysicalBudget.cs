using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextPhysicalBudget
{
    internal const int MaximumGenerations = 3;

    internal static void Check(string root, NativeTextGenerationSlot? first, NativeTextGenerationSlot? second,
        NativeTextGenerationSlot? third, ReadExecutionBudget? budget)
    {
        var files = 0;
        long bytes = 0;
        Accumulate(root, first, ref files, ref bytes, budget);
        Accumulate(root, second, ref files, ref bytes, budget);
        Accumulate(root, third, ref files, ref bytes, budget);
    }

    private static void Accumulate(string root, NativeTextGenerationSlot? slot, ref int files, ref long bytes,
        ReadExecutionBudget? budget)
    {
        if (slot is null)
        {
            return;
        }
        var generation = slot.Generation;
        if (generation is null && !slot.PhysicalOwnerCreated)
        {
            return;
        }
        var path = generation?.Path ?? Path.Combine(root, slot.Leaf ?? throw NativeTextErrors.Corrupt());
        if (!Directory.Exists(path))
        {
            throw NativeTextErrors.Corrupt();
        }
        budget?.Check();
        var measured = NativeTextFileIO.MeasureRegularFiles(path, NativeTextProtocol.MaximumFiles,
            NativeTextProtocol.MaximumDiskBytes, budget);
        if (measured.Files > NativeTextProtocol.MaximumFiles - files
            || measured.Bytes > NativeTextProtocol.MaximumDiskBytes - bytes)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        files += measured.Files;
        bytes += measured.Bytes;
    }
}
