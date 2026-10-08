using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalSourceValidation
{
    internal static void Authority(NativeTextIncrementalManifest manifest,
        TextIndexMaintenanceRequest request, NativeTextSeedCapture current, ReadExecutionBudget budget)
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
            || manifest.Placement.Incarnation != request.Placement.Incarnation
            || manifest.Placement.PlacementEpoch != request.Placement.PlacementEpoch
            || !manifest.Placement.VoterIds.SequenceEqual(request.Placement.VoterIds, StringComparer.Ordinal))
        { throw NativeTextErrors.Mismatch(); }
        budget.Check();
    }

    internal static void CompleteCorpus(NativeTextIncrementalManifest manifest,
        NativeTextSeedCapture current, ReadExecutionBudget budget)
    {
        budget.Check();
        if (manifest.ThroughSequence != current.UpperSequence
            || manifest.Records.Length != current.Documents.Length)
        { throw NativeTextErrors.Mismatch(); }
        budget.ChargeBytes(checked((long)manifest.Records.Length * NativeTextIncrementalSourceProtocol.MapSlotBytes));
        var records = manifest.Records.ToDictionary(record => record.Reference);
        foreach (var document in current.Documents)
        {
            budget.Check();
            if (!records.Remove(document.Reference, out var retained)
                || retained.Revision != document.Revision || retained.Deleted != document.Deleted)
            { throw NativeTextErrors.Mismatch(); }
            var actual = NativeTextIncrementalRevision.Digest(document, budget);
            if (retained.CanonicalSha256 is null || retained.CanonicalSha256.Length != SHA256.HashSizeInBytes
                || !CryptographicOperations.FixedTimeEquals(retained.CanonicalSha256, actual))
            { throw NativeTextErrors.Mismatch(); }
        }
        if (records.Count != NativeTextIncrementalSourceProtocol.EmptyRecords)
        { throw NativeTextErrors.Mismatch(); }
        budget.Check();
    }
}
