using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Search;

internal static class TextIncrementalValidation
{
    private const long InitialRevisionBoundary = 0;
    private const int EmptyFiles = 0;
    private const ulong EmptyRecordId = 0;

    internal static void Manifest(TextIncrementalManifest manifest,
        TextProjectionScope scope, ProjectionConsumerRef consumer, long generation,
        PhysicalShardRecord placement, ReadExecutionBudget budget,
        IOptions<TextExecutionOptions> executionOptions)
    {
        budget.Check();
        if (manifest is null || manifest.FormatVersion != TextIncrementalProtocol.ManifestFormatVersion
            || manifest.Scope != scope || manifest.Consumer != consumer || manifest.Generation != generation
            || manifest.Placement is null || manifest.Placement.PhysicalShardId != placement.PhysicalShardId
            || manifest.Placement.Incarnation != placement.Incarnation
            || consumer.Partition != scope.Partition || generation < TextIncrementalProtocol.InitialGeneration
            || manifest.ThroughSequence < TextIncrementalProtocol.InitialSequence
            || manifest.AppliedPosition < TextIncrementalProtocol.InitialSequence
            || manifest.TokenizerVersion != TextProjectionProtocol.TokenizerVersion
            || manifest.HashVersion != TextProjectionProtocol.HashVersion
            || !TextIncrementalDigest.IsCanonical(manifest.ResourceSha256) || manifest.Files is null
            || manifest.Files.Length == EmptyFiles || manifest.Files.Length > executionOptions.Value.MaximumFiles)
        {
            throw TextErrors.Corrupt();
        }
        TextValidation.ValidateScope(scope, scope.NodeId);
        SettledRequest(manifest.LastSettledCheckpointRequest, consumer, budget);
        Records(manifest.Records, manifest.NextRecord, scope, budget);
        TextValidation.ValidateFiles(manifest.Files, executionOptions);
        budget.Check();
    }

    internal static void Records(TextIncrementalRecord[] records, ulong nextRecord,
        TextProjectionScope scope, ReadExecutionBudget budget)
    {
        if (records is null
            || nextRecord < TextIncrementalProtocol.InitialRecord)
        {
            throw TextErrors.Corrupt();
        }
        var references = new HashSet<EntityRef>();
        var previousId = EmptyRecordId;
        foreach (var record in records)
        {
            budget.ChargeBytes(OrleansSerialization.Measure(record));
            if (record is null || record.Id <= previousId || record.Id >= nextRecord
                || record.Reference is null || record.Reference.Partition != scope.Partition || record.Reference.Collection != scope.Collection
                || record.Revision <= InitialRevisionBoundary || !references.Add(record.Reference)
                || record.CanonicalSha256 is null || record.CanonicalSha256.Length != SHA256.HashSizeInBytes)
            {
                throw TextErrors.Corrupt();
            }
            JsonData.Identifier(record.Reference.Id);
            previousId = record.Id;
        }
        budget.Check();
    }

    internal static void SettledRequest(CommitProjectionBatchRequest? original,
        ProjectionConsumerRef consumer, ReadExecutionBudget budget)
    {
        if (original is null)
        {
            return;
        }
        budget.Check();
        budget.ChargeBytes(OrleansSerialization.Measure(original));
        if (original.CommandId == Guid.Empty || original.Consumer != consumer
            || string.IsNullOrEmpty(original.Token) || original.Effects.IsDefault || !original.Effects.IsEmpty)
        {
            throw TextErrors.Corrupt();
        }
        budget.Check();
    }

}
