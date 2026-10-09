using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Compares every immutable admitted field without coupling native CLR reference graphs.</summary>
internal static class ClusterRestorePlanComparison
{
    private const string Invalid = "The supplied restore inputs differ from the original native operation plan.";

    internal static void Require(ClusterRestorePlan original, ClusterRestorePlan candidate)
    {
        if (original.Version != ClusterRestorePlan.CurrentVersion || original.OperationId != candidate.OperationId
            || original.CaptureId != candidate.CaptureId || original.DestinationPath != candidate.DestinationPath
            || original.StoragePolicy != candidate.StoragePolicy || original.DatabasePolicy != candidate.DatabasePolicy
            || original.Slots.IsDefault || candidate.Slots.IsDefault || !original.Slots.SequenceEqual(candidate.Slots)
            || original.SourceArchives.IsDefault || candidate.SourceArchives.IsDefault
            || original.SourceArchives.Length != candidate.SourceArchives.Length
            || ClusterRestoreMappingDigest.Compute(original.Mappings) != ClusterRestoreMappingDigest.Compute(candidate.Mappings))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        original.DatabasePolicy.Validate();
        foreach (var pair in original.SourceArchives.Zip(candidate.SourceArchives))
        {
            if (pair.First.Version != ClusterRestoreSource.CurrentVersion || pair.First.CanonicalPath != pair.Second.CanonicalPath
                || pair.First.EnvelopeDigest != pair.Second.EnvelopeDigest || pair.First.Files.IsDefault
                || pair.Second.Files.IsDefault || !pair.First.Files.SequenceEqual(pair.Second.Files))
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            ClusterRestorePlanVerification.RequireOriginalCut(pair.First.OriginalCut, pair.Second.OriginalCut);
        }
        ClusterRestoreOperatorSubjects.Require(original.OperatorSubjects, candidate.OperatorSubjects,
            original.SourceArchives.Select(source => source.OriginalCut).ToImmutableArray());
    }
}
