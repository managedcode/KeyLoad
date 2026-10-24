using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextPhysicalBudget
{
    internal static void Check(string root, TextGenerationSlot? first, TextGenerationSlot? second, TextGenerationSlot? third, ReadExecutionBudget? budget, IOptions<TextExecutionOptions> executionOptions)
    {
        const int FilesInitialValue = 0;
        const int BytesInitialValue = 0;

        var files = FilesInitialValue;
        long bytes = BytesInitialValue;
        Accumulate(root, first, ref files, ref bytes, budget, executionOptions: executionOptions);
        Accumulate(root, second, ref files, ref bytes, budget, executionOptions: executionOptions);
        Accumulate(root, third, ref files, ref bytes, budget, executionOptions: executionOptions);
    }

    private static void Accumulate(string root, TextGenerationSlot? slot, ref int files, ref long bytes, ReadExecutionBudget? budget, IOptions<TextExecutionOptions> executionOptions)
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
        var path = generation?.Path ?? Path.Combine(root, slot.Leaf ?? throw TextErrors.Corrupt());
        if (!Directory.Exists(path))
        {
            throw TextErrors.Corrupt();
        }
        budget?.Check();
        var measured = TextFileIO.MeasureRegularFiles(path, executionOptions.Value.MaximumFiles,
            executionOptions.Value.MaximumDiskBytes, budget: budget, executionOptions: executionOptions);
        if (measured.Files > executionOptions.Value.MaximumFiles - files
            || measured.Bytes > executionOptions.Value.MaximumDiskBytes - bytes)
        {
            throw TextErrors.BoundExceeded();
        }
        files += measured.Files;
        bytes += measured.Bytes;
    }
}
