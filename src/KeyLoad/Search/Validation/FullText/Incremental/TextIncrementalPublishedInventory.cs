using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextIncrementalPublishedInventory
{
    internal static void Require(TextIncrementalOwner actual,
        TextIncrementalManifest manifest, ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> options, TextOnlineGenerationPin? retained = null)
    {
        budget.Check();
        var original = TextIncrementalMetadata.ReadManifest(actual.Path,
            options.Value.MaximumDiskBytes, budget);
        budget.ChargeBytes(OrleansSerialization.Measure(original));
        budget.ChargeBytes(OrleansSerialization.Measure(manifest));
        if (!OrleansSerialization.Serialize(original).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(manifest)))
        {
            throw TextErrors.Corrupt();
        }
        var owner = TextOwnerFiles.ReadOwner(Path.Combine(actual.Path, TextProtocol.OwnerFile),
            actual.Root, actual.Leaf, manifest.Scope.NodeId, options);
        if (owner.Scope != manifest.Scope)
        {
            throw TextErrors.Corrupt();
        }
        TextInventory.Verify(actual.Path, owner.OwnedPaths, manifest.Files, options, budget, retained);
        budget.Check();
    }
}
