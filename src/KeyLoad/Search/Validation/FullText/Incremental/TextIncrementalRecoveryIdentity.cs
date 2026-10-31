using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Search;

namespace KeyLoad.Server.Search;

internal static class TextIncrementalRecoveryIdentity
{
    internal static void RequireBuild(TextIncrementalEnrollment enrollment,
        TextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        budget.Check();
        if (enrollment.BuildCommandId != request.CommandId)
        {
            throw TextErrors.Mismatch();
        }
        var actual = SHA256.HashData(OrleansSerialization.Serialize(request));
        budget.Check();
        if (!CryptographicOperations.FixedTimeEquals(enrollment.OriginalRequestSha256, actual))
        {
            throw TextErrors.Mismatch();
        }
    }

    internal static long RequireAdmittedUpper(TextIncrementalEnrollment enrollment,
        TextIndexMaintenanceRequest request, TextSeedCapture fresh,
        bool hasCompleteImage, ReadExecutionBudget budget)
    {
        budget.Check();
        if (fresh.ResourceSha256 != enrollment.ResourceSha256
            || fresh.UpperSequence < enrollment.SourceUpperSequence)
        {
            throw TextErrors.Mismatch();
        }
        if (request.Mode == TextIndexMaintenanceMode.Build)
        {
            RequireBuild(enrollment, request, budget);
            if (fresh.Checkpoint > fresh.UpperSequence)
            {
                throw TextErrors.Corrupt();
            }
            if (hasCompleteImage && fresh.Checkpoint > enrollment.SourceUpperSequence)
            {
                throw TextErrors.Mismatch();
            }
            if (!hasCompleteImage && fresh.UpperSequence != enrollment.SourceUpperSequence)
            {
                throw TextErrors.Mismatch();
            }
            return enrollment.SourceUpperSequence;
        }
        budget.Check();
        return fresh.UpperSequence;
    }

    internal static void RequireIntent(TextIncrementalEnrollment enrollment,
        TextIncrementalIntent intent, TextIncrementalManifest? published,
        ReadExecutionBudget budget)
    {
        budget.Check();
        if (intent.Consumer != enrollment.Consumer || intent.Generation != enrollment.Generation
            || intent.ResourceSha256 != enrollment.ResourceSha256
            || intent.SourceUpperSequence < enrollment.SourceUpperSequence
            || intent.Bootstrap && intent.SourceUpperSequence != enrollment.SourceUpperSequence
            || intent.ThroughSequence > intent.SourceUpperSequence
            || published is not null && (published.Consumer != intent.Consumer
                || published.Generation != intent.Generation
                || published.ResourceSha256 != intent.ResourceSha256))
        {
            throw TextErrors.Corrupt();
        }
        budget.Check();
    }
}
