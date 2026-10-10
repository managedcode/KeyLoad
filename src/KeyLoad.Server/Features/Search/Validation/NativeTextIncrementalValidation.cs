using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalValidation
{
    private const long InitialRevisionBoundary = 0;
    private const int EmptyFiles = 0;
    private const ulong EmptyRecordId = 0;

    internal static void Manifest(NativeTextIncrementalManifest manifest,
        TextProjectionScope scope, ProjectionConsumerRef consumer, long generation,
        PhysicalShardRecord placement, int maximumRecords, ReadExecutionBudget budget,
        IOptions<NativeTextExecutionOptions> executionOptions)
    {
        budget.Check();
        if (manifest is null || manifest.FormatVersion != NativeTextIncrementalProtocol.ManifestFormatVersion
            || manifest.Scope != scope || manifest.Consumer != consumer || manifest.Generation != generation
            || manifest.Placement is null || manifest.Placement.PhysicalShardId != placement.PhysicalShardId
            || manifest.Placement.Incarnation != placement.Incarnation
            || manifest.Placement.PlacementEpoch != placement.PlacementEpoch
            || !manifest.Placement.VoterIds.SequenceEqual(placement.VoterIds, StringComparer.Ordinal)
            || consumer.Partition != scope.Partition || generation < NativeTextIncrementalProtocol.InitialGeneration
            || manifest.ThroughSequence < NativeTextIncrementalProtocol.InitialSequence
            || manifest.AppliedPosition < NativeTextIncrementalProtocol.InitialSequence
            || manifest.TokenizerVersion != TextProjectionProtocol.TokenizerVersion
            || manifest.HashVersion != TextProjectionProtocol.HashVersion
            || !NativeTextIncrementalDigest.IsCanonical(manifest.ResourceSha256) || manifest.Files is null
            || manifest.Files.Length == EmptyFiles || manifest.Files.Length > executionOptions.Value.MaximumFiles)
        { throw NativeTextErrors.Corrupt(); }
        NativeTextValidation.ValidateScope(scope, scope.NodeId);
        SettledRequest(manifest.LastSettledCheckpointRequest, consumer, budget);
        Records(manifest.Records, manifest.NextRecord, scope, maximumRecords, budget);
        NativeTextValidation.ValidateFiles(manifest.Files, executionOptions);
        budget.Check();
    }

    internal static void Records(NativeTextIncrementalRecord[] records, ulong nextRecord,
        TextProjectionScope scope, int maximumRecords, ReadExecutionBudget budget)
    {
        if (records is null || records.Length > maximumRecords
            || nextRecord < NativeTextIncrementalProtocol.InitialRecord)
        { throw NativeTextErrors.Corrupt(); }
        var references = new HashSet<EntityRef>();
        var previousId = EmptyRecordId;
        foreach (var record in records)
        {
            budget.ChargeBytes(NativeSerialization.Measure(record));
            if (record is null || record.Id <= previousId || record.Id >= nextRecord
                || record.Reference is null || record.Reference.Partition != scope.Partition || record.Reference.Collection != scope.Collection
                || record.Revision <= InitialRevisionBoundary || !references.Add(record.Reference)
                || record.CanonicalSha256 is null || record.CanonicalSha256.Length != SHA256.HashSizeInBytes)
            { throw NativeTextErrors.Corrupt(); }
            JsonData.Identifier(record.Reference.Id);
            previousId = record.Id;
        }
        budget.Check();
    }

    internal static void SettledRequest(CommitProjectionBatchRequest? original,
        ProjectionConsumerRef consumer, ReadExecutionBudget budget)
    {
        if (original is null)
        { return; }
        budget.Check();
        budget.ChargeBytes(NativeSerialization.Measure(original));
        if (original.CommandId == Guid.Empty || original.Consumer != consumer
            || string.IsNullOrEmpty(original.Token) || original.Effects.IsDefault || !original.Effects.IsEmpty)
        { throw NativeTextErrors.Corrupt(); }
        budget.Check();
    }

}
