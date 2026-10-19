using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

/// <summary>Retires only the persisted original intent after its real canonical ACK is observed.</summary>
internal static class TextIncrementalCheckpointSettlement
{
    private const int EmptyMutations = 0;
    private const long PositivePositionBoundary = 0;

    internal static TextIncrementalManifest Complete(string generationPath, TextIncrementalIntent intent,
        TextIncrementalManifest manifest, ProjectionBatchResult acknowledged,
        TextSeedCapture fresh, ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> options, Action<TextFaultStage>? observer = null, TextResourceOwnership? resources = null)
    {
        budget.Check();
        var receipt = acknowledged.Receipt;
        if (receipt is null || receipt.CommandId != intent.CheckpointCommand.CommandId
            || receipt.Mutations.IsDefault || receipt.Mutations.Length != EmptyMutations
            || acknowledged.Checkpoint != intent.ThroughSequence
            || fresh.Checkpoint != intent.ThroughSequence
            || receipt.Token is null || receipt.Token.Incarnation != manifest.Scope.Incarnation
            || receipt.Token.AtomicPartitionId != manifest.Consumer.Partition.AtomicPartitionId
            || receipt.Token.Position <= PositivePositionBoundary
            || receipt.Token.Position > fresh.AppliedPosition
            || fresh.Incarnation != manifest.Scope.Incarnation
            || fresh.ReadGeneration != manifest.Scope.ReadGeneration
            || fresh.PrincipalId != manifest.Scope.PrincipalId
            || fresh.PolicyEpoch != manifest.Scope.PolicyEpoch
            || fresh.SchemaVersion != manifest.Scope.SchemaVersion
            || fresh.ResourceSha256 != manifest.ResourceSha256)
        {
            throw TextErrors.Corrupt();
        }
        if (manifest.ThroughSequence == fresh.UpperSequence)
        {
            TextIncrementalSourceValidation.CompleteCorpus(manifest, fresh, budget);
        }
        RequireOriginal(generationPath, intent, budget, options);
        var owner = TextOwnerFiles.ReadOwner(Path.Combine(generationPath, TextProtocol.OwnerFile),
            Path.GetDirectoryName(generationPath) ?? throw TextErrors.Ownership(),
            Path.GetFileName(generationPath), manifest.Scope.NodeId, options);
        TextInventory.Verify(generationPath, owner.OwnedPaths, manifest.Files, options, budget);
        budget.Check();
        observer?.Invoke(TextFaultStage.CanonicalCheckpointAcknowledged);
        TextIncrementalValidation.SettledRequest(intent.CheckpointCommand, manifest.Consumer, budget);
        var completed = manifest with
        {
            Bootstrap = manifest.Bootstrap && acknowledged.Checkpoint != intent.SourceUpperSequence,
            LastSettledCheckpointRequest = intent.CheckpointCommand
        };
        TextIncrementalMetadata.Publish(generationPath, completed,
            options.Value.MaximumDiskBytes, budget, options, resources);
        budget.Check();
        var retiredIntent = Path.Combine(generationPath, TextIncrementalProtocol.IntentFile);
        if (resources is null)
        {
            File.Delete(retiredIntent);
        }
        else
        {
            resources.DeleteOwnedFile(retiredIntent, () => File.Delete(retiredIntent));
        }
        observer?.Invoke(TextFaultStage.PendingIntentRetired);
        budget.Check();
        return completed;
    }

    private static void RequireOriginal(string generationPath, TextIncrementalIntent expected,
        ReadExecutionBudget budget, IOptions<TextExecutionOptions> options)
    {
        var actual = TextIncrementalMetadata.ReadIntent(generationPath,
            options.Value.MaximumDiskBytes, budget);
        budget.ChargeBytes(OrleansSerialization.Measure(expected));
        budget.ChargeBytes(OrleansSerialization.Measure(actual));
        var expectedBytes = OrleansSerialization.Serialize(expected);
        var actualBytes = OrleansSerialization.Serialize(actual);
        budget.Check();
        if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
        {
            throw TextErrors.Corrupt();
        }
    }
}
