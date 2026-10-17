using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PartitionControlDocumentReadFrameV2? TryCaptureControlledDocumentReadV2(string principalId,
        EntityRef reference, CommitToken? minimumToken, DateTimeOffset expiry, ReadExecutionBudget work)
    {
        work.Check();
        var result = Store.Read<PartitionControlDocumentReadFrameV2?>(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, principalId, EvaluationClock.GetUtcNow());
            Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
            if (PartitionMovePublishedPlacementStorage.Read(charged, reference.Partition) is null)
            {
                return null;
            }
            var frame = CaptureControlledDocumentReadView(charged, principalId, reference, minimumToken, expiry, Guid.NewGuid());
            var coverage = PartitionControlLifetimeCoverage.Capture(charged, reference.Partition, [frame.Resource], Limits.MaxBatchBytes);
            work.CaptureAuthenticatedResourceLifetimes(this, charged, coverage, [frame.Resource]);
            return new(frame, coverage);
        });
        work.CheckResult(result);
        return result;
    }

    internal void ValidateControlledDocumentReadV2(PartitionControlDocumentReadFrameV2 original, ReadExecutionBudget work)
    {
        work.Check();
        var current = Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var frame = CaptureControlledDocumentReadView(charged, original.Original.Principal.Id,
                original.Original.Reference, original.Original.MinimumToken, original.Original.ExpiresAt, original.Original.QueryId);
            var coverage = PartitionControlLifetimeCoverage.Capture(charged, frame.Reference.Partition, [frame.Resource], Limits.MaxBatchBytes);
            work.CaptureAuthenticatedResourceLifetimes(this, charged, coverage, [frame.Resource]);
            return new PartitionControlDocumentReadFrameV2(frame, coverage);
        });
        if (!NativeSerialization.Serialize(current).AsSpan().SequenceEqual(NativeSerialization.Serialize(original)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch);
        }
        work.Check();
    }
}
