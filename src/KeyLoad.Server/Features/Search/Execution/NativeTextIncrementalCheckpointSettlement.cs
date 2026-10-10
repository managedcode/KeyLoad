using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

/// <summary>Retires only the persisted original intent after its real canonical ACK is observed.</summary>
internal static class NativeTextIncrementalCheckpointSettlement
{
    private const int EmptyMutations = 0;
    private const long PositivePositionBoundary = 0;

    internal static NativeTextIncrementalManifest Complete(string generationPath, NativeTextIncrementalIntent intent,
        NativeTextIncrementalManifest manifest, ProjectionBatchResult acknowledged,
        NativeTextSeedCapture fresh, ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> options, Action<NativeTextFaultStage>? observer = null, NativeTextResourceOwnership? resources = null)
    {
        budget.Check();
        var receipt = acknowledged.Receipt;
        if (receipt is null || receipt.CommandId != intent.CheckpointCommand.CommandId
            || receipt.Mutations.IsDefault || receipt.Mutations.Length != EmptyMutations
            || acknowledged.Checkpoint != intent.ThroughSequence
            || fresh.Checkpoint != intent.ThroughSequence
            || receipt.Token is null || receipt.Token.Incarnation != manifest.Scope.Incarnation
            || receipt.Token.AtomicPartitionId != manifest.Consumer.Partition.AtomicPartitionId
            || receipt.Token.OwnershipEpoch != manifest.Placement.PlacementEpoch
            || receipt.Token.Position <= PositivePositionBoundary
            || receipt.Token.Position > fresh.AppliedPosition
            || fresh.Incarnation != manifest.Scope.Incarnation
            || fresh.ReadGeneration != manifest.Scope.ReadGeneration
            || fresh.PrincipalId != manifest.Scope.PrincipalId
            || fresh.PolicyEpoch != manifest.Scope.PolicyEpoch
            || fresh.SchemaVersion != manifest.Scope.SchemaVersion
            || fresh.ResourceSha256 != manifest.ResourceSha256)
        { throw NativeTextErrors.Corrupt(); }
        if (manifest.ThroughSequence == fresh.UpperSequence)
        { NativeTextIncrementalSourceValidation.CompleteCorpus(manifest, fresh, budget); }
        RequireOriginal(generationPath, intent, budget, options);
        var owner = NativeTextOwnerFiles.ReadOwner(Path.Combine(generationPath, NativeTextProtocol.OwnerFile),
            Path.GetDirectoryName(generationPath) ?? throw NativeTextErrors.Ownership(),
            Path.GetFileName(generationPath), manifest.Scope.NodeId, options);
        NativeTextInventory.Verify(generationPath, owner.OwnedPaths, manifest.Files, options, budget);
        budget.Check();
        observer?.Invoke(NativeTextFaultStage.CanonicalCheckpointAcknowledged);
        NativeTextIncrementalValidation.SettledRequest(intent.CheckpointCommand, manifest.Consumer, budget);
        var completed = manifest with
        {
            Bootstrap = manifest.Bootstrap && acknowledged.Checkpoint != intent.SourceUpperSequence,
            LastSettledCheckpointRequest = intent.CheckpointCommand
        };
        NativeTextIncrementalMetadata.Publish(generationPath, completed,
            options.Value.MaximumDiskBytes, budget, options, resources);
        budget.Check();
        var retiredIntent = Path.Combine(generationPath, NativeTextIncrementalProtocol.IntentFile);
        if (resources is null)
        { File.Delete(retiredIntent); }
        else
        { resources.DeleteOwnedFile(retiredIntent, () => File.Delete(retiredIntent)); }
        observer?.Invoke(NativeTextFaultStage.PendingIntentRetired);
        budget.Check();
        return completed;
    }

    private static void RequireOriginal(string generationPath, NativeTextIncrementalIntent expected,
        ReadExecutionBudget budget, IOptions<NativeTextExecutionOptions> options)
    {
        var actual = NativeTextIncrementalMetadata.ReadIntent(generationPath,
            options.Value.MaximumDiskBytes, budget);
        budget.ChargeBytes(NativeSerialization.Measure(expected));
        budget.ChargeBytes(NativeSerialization.Measure(actual));
        var expectedBytes = NativeSerialization.Serialize(expected);
        var actualBytes = NativeSerialization.Serialize(actual);
        budget.Check();
        if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
        { throw NativeTextErrors.Corrupt(); }
    }
}
