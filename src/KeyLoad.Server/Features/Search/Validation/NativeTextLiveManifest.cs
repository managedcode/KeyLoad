namespace KeyLoad.Server.Features.Search;

internal static class NativeTextLiveManifest
{
    internal static bool Matches(NativeTextGeneration generation, NativeTextManifest manifest)
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
            if (records[index] != manifest.Records[index])
            {
                return false;
            }
        }
        for (var index = IndexInitialValue; index < files.Length; index++)
        {
            var expected = files[index];
            var actual = manifest.Files[index];
            if (!StringComparer.Ordinal.Equals(expected.RelativePath, actual.RelativePath)
                || expected.Length != actual.Length || expected.Sha256 is null || actual.Sha256 is null
                || !expected.Sha256.AsSpan().SequenceEqual(actual.Sha256))
            {
                return false;
            }
        }
        return true;
    }
}
