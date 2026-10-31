using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextInventory
{
    internal static TextFile[] Capture(string generationPath, TextOwnedPath[] ownedPaths, IOptions<TextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null, TextOnlineGenerationPin? retained = null)
    {
        retained?.RequirePath(generationPath);
        TextOwnedInventory.ValidateTrackedLayout(generationPath, ownedPaths, budget,
            runtimeAllowMissing: false, executionOptions: executionOptions);
        return TextOwnedInventory.CaptureFiles(generationPath, ownedPaths, budget, executionOptions: executionOptions, retained: retained);
    }

    internal static void Verify(string generationPath, TextOwnedPath[] ownedPaths, TextFile[] expected, IOptions<TextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null, TextOnlineGenerationPin? retained = null)
    {
        const int IndexInitialValue = 0;

        budget?.Check();
        var actual = Capture(generationPath, ownedPaths, budget: budget, executionOptions: executionOptions, retained: retained);
        if (expected is null || actual.Length != expected.Length)
        {
            throw TextErrors.Corrupt();
        }
        for (var index = IndexInitialValue; index < actual.Length; index++)
        {
            // ZoneTree owns the content of these files and rewrites its metadata; only the owned file set is checked.
            if (actual[index].RelativePath != expected[index].RelativePath)
            {
                throw TextErrors.Corrupt();
            }
        }
    }

    internal static void ValidateTrackedLayout(string generationPath, TextOwnedPath[] ownedPaths, IOptions<TextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null, bool runtimeAllowMissing = false)
        => TextOwnedInventory.ValidateTrackedLayout(generationPath, ownedPaths, budget, runtimeAllowMissing, executionOptions: executionOptions);
}
