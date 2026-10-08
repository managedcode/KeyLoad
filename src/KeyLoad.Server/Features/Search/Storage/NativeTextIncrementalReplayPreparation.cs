using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalReplayPreparation
{
    internal static void Prepare(string root, string leaf, NativeTextIncrementalIntent expected,
        int maximumRecords, int maximumChanges, ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> options)
    {
        budget.Check();
        _ = NativeTextIncrementalRoot.CheckRoot(root, expected.Scope.NodeId, options, budget);
        var path = Path.Combine(root, leaf);
        var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(path, NativeTextProtocol.OwnerFile),
            root, leaf, expected.Scope.NodeId, options);
        NativeTextOwnedInventory.ValidateTrackedLayout(path, owner.OwnedPaths, budget,
            allowMissingNative: true, executionOptions: options);
        if (File.Exists(Path.Combine(path, NativeTextIncrementalProtocol.PendingIntentFile)))
        { throw NativeTextErrors.Corrupt(); }
        var actual = NativeTextIncrementalMetadata.ReadIntent(path, options.Value.MaximumDiskBytes, budget);
        NativeTextIncrementalIntentValidation.Require(actual, expected.Scope, expected.Consumer,
            expected.Generation, expected.Placement, maximumRecords, maximumChanges, budget);
        RequireOriginal(actual, expected, budget);
        var pending = Path.Combine(path, NativeTextIncrementalProtocol.PendingManifestFile);
        if (File.Exists(pending))
        {
            NativeTextFileIO.VerifyBoundedFile(pending, options);
            budget.Check();
            File.Delete(pending);
        }
        budget.Check();
    }

    private static void RequireOriginal(NativeTextIncrementalIntent actual,
        NativeTextIncrementalIntent expected, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(NativeSerialization.Measure(expected));
        budget.ChargeBytes(NativeSerialization.Measure(actual));
        var original = NativeSerialization.Serialize(expected);
        var persisted = NativeSerialization.Serialize(actual);
        budget.Check();
        if (!original.AsSpan().SequenceEqual(persisted))
        { throw NativeTextErrors.Corrupt(); }
    }
}
