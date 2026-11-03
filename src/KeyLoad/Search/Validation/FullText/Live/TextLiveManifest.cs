using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal static class TextLiveManifest
{
    internal static bool Matches(TextGeneration generation, TextManifest manifest, ReadExecutionBudget? budget = null)
    {
        const int IndexInitialValue = 0;

        var files = generation.Files;
        if (manifest.Scope != generation.Scope || generation.Corpus.Records.Count != manifest.PostingProof.DocumentCount
            || !generation.Corpus.RecordDigest.AsSpan().SequenceEqual(manifest.RecordDigest)
            || files.Length != manifest.Files.Length || !generation.Corpus.PostingProof.Matches(manifest.PostingProof))
        {
            return false;
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
