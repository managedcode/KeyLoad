using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Search;

namespace KeyLoad.Server.Search;

internal static class TextIncrementalSourceValidation
{
    internal static void Authority(TextIncrementalManifest manifest,
        TextIndexMaintenanceRequest request, TextSeedCapture current, ReadExecutionBudget budget)
    {
        budget.Check();
        var scope = manifest.Scope;
        if (manifest.Consumer != request.Consumer || manifest.Generation != request.IndexGeneration
            || scope.NodeId != request.NodeId || scope.Partition != request.Consumer.Partition
            || scope.Collection != request.Collection || scope.Field != request.Field
            || scope.Incarnation != current.Incarnation || scope.DataEpoch != current.DataEpoch
            || scope.ReadGeneration != current.ReadGeneration
            || scope.PrincipalId != current.PrincipalId || scope.PolicyEpoch != current.PolicyEpoch
            || scope.SchemaVersion != current.SchemaVersion || scope.Position > current.Position
            || manifest.AppliedPosition > current.AppliedPosition
            || manifest.ThroughSequence > current.UpperSequence
            || manifest.ResourceSha256 != current.ResourceSha256
            || manifest.Placement.PhysicalShardId != request.Placement.PhysicalShardId
            || manifest.Placement.Incarnation != request.Placement.Incarnation)
        {
            throw TextErrors.Mismatch();
        }
        budget.Check();
    }

    internal static void CompleteCorpus(TextIncrementalManifest manifest,
        TextSeedCapture current, ReadExecutionBudget budget)
    {
        budget.Check();
        if (manifest.ThroughSequence != current.UpperSequence
            || manifest.Records.Length != current.Documents.Length)
        {
            throw TextErrors.Mismatch();
        }
        budget.ChargeBytes(checked((long)manifest.Records.Length * TextIncrementalSourceProtocol.MapSlotBytes));
        var records = manifest.Records.ToDictionary(record => record.Reference);
        foreach (var document in current.Documents)
        {
            budget.Check();
            if (!records.Remove(document.Reference, out var retained)
                || retained.Revision != document.Revision || retained.Deleted != document.Deleted)
            {
                throw TextErrors.Mismatch();
            }
            var actual = TextIncrementalRevision.Digest(document, budget);
            if (retained.CanonicalSha256 is null || retained.CanonicalSha256.Length != SHA256.HashSizeInBytes
                || !CryptographicOperations.FixedTimeEquals(retained.CanonicalSha256, actual))
            {
                throw TextErrors.Mismatch();
            }
        }
        if (records.Count != TextIncrementalSourceProtocol.EmptyRecords)
        {
            throw TextErrors.Mismatch();
        }
        budget.Check();
    }
}
