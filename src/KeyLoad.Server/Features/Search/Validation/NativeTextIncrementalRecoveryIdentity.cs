using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalRecoveryIdentity
{
    internal static void RequireBuild(NativeTextIncrementalEnrollment enrollment,
        TextIndexMaintenanceRequest request, ReadExecutionBudget budget)
    {
        budget.Check();
        if (enrollment.BuildCommandId != request.CommandId)
        { throw NativeTextErrors.Mismatch(); }
        budget.ChargeBytes(NativeSerialization.Measure(request));
        budget.ChargeBytes(SHA256.HashSizeInBytes);
        var actual = SHA256.HashData(NativeSerialization.Serialize(request));
        budget.Check();
        if (!CryptographicOperations.FixedTimeEquals(enrollment.OriginalRequestSha256, actual))
        { throw NativeTextErrors.Mismatch(); }
    }

    internal static long RequireAdmittedUpper(NativeTextIncrementalEnrollment enrollment,
        TextIndexMaintenanceRequest request, NativeTextSeedCapture fresh,
        bool hasCompleteImage, ReadExecutionBudget budget)
    {
        budget.Check();
        if (fresh.ResourceSha256 != enrollment.ResourceSha256
            || fresh.UpperSequence < enrollment.SourceUpperSequence)
        { throw NativeTextErrors.Mismatch(); }
        if (request.Mode == TextIndexMaintenanceMode.Build)
        {
            RequireBuild(enrollment, request, budget);
            if (fresh.Checkpoint > fresh.UpperSequence)
            { throw NativeTextErrors.Corrupt(); }
            if (hasCompleteImage && fresh.Checkpoint > enrollment.SourceUpperSequence)
            { throw NativeTextErrors.Mismatch(); }
            if (!hasCompleteImage && fresh.UpperSequence != enrollment.SourceUpperSequence)
            { throw NativeTextErrors.Mismatch(); }
            return enrollment.SourceUpperSequence;
        }
        budget.Check();
        return fresh.UpperSequence;
    }

    internal static void RequireIntent(NativeTextIncrementalEnrollment enrollment,
        NativeTextIncrementalIntent intent, NativeTextIncrementalManifest? published,
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
        { throw NativeTextErrors.Corrupt(); }
        budget.Check();
    }
}
