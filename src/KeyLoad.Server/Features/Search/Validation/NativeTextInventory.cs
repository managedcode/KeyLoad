using System.Security.Cryptography;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextInventory
{
    internal static NativeTextFile[] Capture(string generationPath, NativeTextOwnedPath[] ownedPaths,
        ReadExecutionBudget? budget = null)
    {
        NativeTextOwnedInventory.ValidateTrackedLayout(generationPath, ownedPaths, budget,
            allowMissingNative: false);
        return NativeTextOwnedInventory.CaptureFiles(generationPath, ownedPaths, budget);
    }

    internal static void Verify(string generationPath, NativeTextOwnedPath[] ownedPaths, NativeTextFile[] expected,
        ReadExecutionBudget? budget = null)
    {
        budget?.Check();
        var actual = Capture(generationPath, ownedPaths, budget);
        if (expected is null || actual.Length != expected.Length)
        {
            throw NativeTextErrors.Corrupt();
        }
        for (var index = 0; index < actual.Length; index++)
        {
            if (actual[index].RelativePath != expected[index].RelativePath || actual[index].Length != expected[index].Length
                || expected[index].Sha256 is null || expected[index].Sha256.Length != SHA256.HashSizeInBytes
                || !CryptographicOperations.FixedTimeEquals(actual[index].Sha256, expected[index].Sha256))
            {
                throw NativeTextErrors.Corrupt();
            }
        }
    }

    internal static void ValidateTrackedLayout(string generationPath, NativeTextOwnedPath[] ownedPaths,
        ReadExecutionBudget? budget = null, bool allowMissingNative = false)
        => NativeTextOwnedInventory.ValidateTrackedLayout(generationPath, ownedPaths, budget, allowMissingNative);
}
