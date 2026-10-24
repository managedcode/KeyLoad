using System.Security.Cryptography;
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
            if (actual[index].RelativePath != expected[index].RelativePath || actual[index].Length != expected[index].Length
                || expected[index].Sha256 is null || expected[index].Sha256.Length != SHA256.HashSizeInBytes
                || !CryptographicOperations.FixedTimeEquals(actual[index].Sha256, expected[index].Sha256))
            {
                throw TextErrors.Corrupt();
            }
        }
    }

    internal static void ValidateTrackedLayout(string generationPath, TextOwnedPath[] ownedPaths, IOptions<TextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null, bool runtimeAllowMissing = false)
        => TextOwnedInventory.ValidateTrackedLayout(generationPath, ownedPaths, budget, runtimeAllowMissing, executionOptions: executionOptions);
}
