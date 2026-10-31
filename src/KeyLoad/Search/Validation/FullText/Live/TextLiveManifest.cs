using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal static class TextLiveManifest
{
    internal static bool Matches(TextGeneration generation, TextManifest manifest, ReadExecutionBudget? budget = null)
    {
        const int IndexInitialValue = 0;

        var records = generation.Records;
        var files = generation.Files;
        if (manifest.Scope != generation.Scope || records.Count != manifest.Records.Length
            || files.Length != manifest.Files.Length)
        {
            return false;
        }
        for (var index = IndexInitialValue; index < records.Count; index++)
        {
            budget?.Check();
            if (records[index] != manifest.Records[index])
            {
                return false;
            }
        }
        for (var index = IndexInitialValue; index < files.Length; index++)
        {
            budget?.Check();
            var expected = files[index];
            var actual = manifest.Files[index];
            if (!StringComparer.Ordinal.Equals(expected.RelativePath, actual.RelativePath))
            {
                return false;
            }
        }
        return true;
    }
}
