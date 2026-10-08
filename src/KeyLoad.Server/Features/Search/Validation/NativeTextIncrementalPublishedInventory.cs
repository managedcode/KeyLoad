using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalPublishedInventory
{
    internal static void Require(NativeTextIncrementalNativeOwner actual,
        NativeTextIncrementalManifest manifest, ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> options)
    {
        budget.Check();
        var original = NativeTextIncrementalMetadata.ReadManifest(actual.Path,
            options.Value.MaximumDiskBytes, budget);
        budget.ChargeBytes(NativeSerialization.Measure(original));
        budget.ChargeBytes(NativeSerialization.Measure(manifest));
        if (!NativeSerialization.Serialize(original).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(manifest)))
        { throw NativeTextErrors.Corrupt(); }
        var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(actual.Path, NativeTextProtocol.OwnerFile),
            actual.Root, actual.Leaf, manifest.Scope.NodeId, options);
        if (owner.Scope != manifest.Scope)
        { throw NativeTextErrors.Corrupt(); }
        NativeTextInventory.Verify(actual.Path, owner.OwnedPaths, manifest.Files, options, budget);
        budget.Check();
    }
}
