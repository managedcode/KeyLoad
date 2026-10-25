using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextIncrementalReplayPreparation
{
    internal static void Prepare(string root, string leaf, TextIncrementalIntent expected, ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> options, TextResourceOwnership? resources = null)
    {
        budget.Check();
        _ = TextIncrementalRoot.CheckRoot(root, expected.Scope.NodeId, options, budget);
        var path = Path.Combine(root, leaf);
        var owner = TextOwnerFiles.ReadOwner(Path.Combine(path, TextProtocol.OwnerFile),
            root, leaf, expected.Scope.NodeId, options);
        TextOwnedInventory.ValidateTrackedLayout(path, owner.OwnedPaths, budget,
            runtimeAllowMissing: true, executionOptions: options);
        if (File.Exists(Path.Combine(path, TextIncrementalProtocol.PendingIntentFile)))
        {
            throw TextErrors.Corrupt();
        }
        var actual = TextIncrementalMetadata.ReadIntent(path, options.Value.MaximumDiskBytes, budget);
        TextIncrementalIntentValidation.Require(actual, expected.Scope, expected.Consumer,
            expected.Generation, expected.Placement, budget);
        RequireOriginal(actual, expected, budget);
        var pending = Path.Combine(path, TextIncrementalProtocol.PendingManifestFile);
        if (File.Exists(pending))
        {
            TextFileIO.VerifyBoundedFile(pending, options);
            budget.Check();
            if (resources is null)
            {
                File.Delete(pending);
            }
            else
            {
                resources.DeleteOwnedFile(pending, () => File.Delete(pending));
            }
        }
        budget.Check();
    }

    private static void RequireOriginal(TextIncrementalIntent actual,
        TextIncrementalIntent expected, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(OrleansSerialization.Measure(expected));
        budget.ChargeBytes(OrleansSerialization.Measure(actual));
        var original = OrleansSerialization.Serialize(expected);
        var persisted = OrleansSerialization.Serialize(actual);
        budget.Check();
        if (!original.AsSpan().SequenceEqual(persisted))
        {
            throw TextErrors.Corrupt();
        }
    }
}
