using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextInventory
{
    internal static NativeTextFile[] Capture(string generationPath, NativeTextOwnedPath[] ownedPaths, IOptions<NativeTextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null)
    {
        NativeTextOwnedInventory.ValidateTrackedLayout(generationPath, ownedPaths, budget,
            allowMissingNative: false, executionOptions: executionOptions);
        return NativeTextOwnedInventory.CaptureFiles(generationPath, ownedPaths, budget, executionOptions: executionOptions);
    }

    internal static void Verify(string generationPath, NativeTextOwnedPath[] ownedPaths, NativeTextFile[] expected, IOptions<NativeTextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null)
    {
        const int IndexInitialValue = 0;

        budget?.Check();
        var actual = Capture(generationPath, ownedPaths, budget: budget, executionOptions: executionOptions);
        if (expected is null || actual.Length != expected.Length)
        {
            throw NativeTextErrors.Corrupt();
        }
        for (var index = IndexInitialValue; index < actual.Length; index++)
        {
            if (actual[index].RelativePath != expected[index].RelativePath || actual[index].Length != expected[index].Length
                || expected[index].Sha256 is null || expected[index].Sha256.Length != SHA256.HashSizeInBytes
                || !CryptographicOperations.FixedTimeEquals(actual[index].Sha256, expected[index].Sha256))
            {
                throw NativeTextErrors.Corrupt();
            }
        }
    }

    internal static void ValidateTrackedLayout(string generationPath, NativeTextOwnedPath[] ownedPaths, IOptions<NativeTextExecutionOptions> executionOptions, ReadExecutionBudget? budget = null, bool allowMissingNative = false)
        => NativeTextOwnedInventory.ValidateTrackedLayout(generationPath, ownedPaths, budget, allowMissingNative, executionOptions: executionOptions);
}
