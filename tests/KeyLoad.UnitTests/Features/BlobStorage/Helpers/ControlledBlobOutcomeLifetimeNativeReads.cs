using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.UnitTests.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class ControlledBlobOutcomeLifetimeNativeReads
{
    internal const string PrincipalId = "root";
    internal const int MaximumBytes = NativeControlledLifetimePayloadBoundary.MaximumBytes;

    internal static ReadExecutionBudget Work(ControlledPartitionMovementNode owner, CancellationToken token)
        => new(Options.Create(owner.Database.Limits), owner.Database.EvaluationClock, token);

    internal static ControlledBlobReadFrameV2 Capture(ControlledBlobOutcomeLifetimeNativeState state,
        ControlledBlobOutcomeLifetimeNativeProof proof, ReadExecutionBudget work)
        => state.Source.Database.TryCaptureControlledBlobOutcomeV2(PrincipalId, proof.Original,
            state.Source.Database.EvaluationClock.GetUtcNow().AddSeconds(state.Source.Database.Limits.QueryDeadlineSeconds), work)
            ?? throw new InvalidOperationException("The genuine published Blob outcome frame is absent.");

    internal static async Task HealthyAsync(ControlledBlobOutcomeLifetimeNativeState state,
        ControlledBlobOutcomeLifetimeNativeProof proof, CancellationToken token)
    {
        var sourceBefore = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var targetBefore = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var parent = Work(state.Source, token);
        var frame = Capture(state, proof, parent);
        var originalOutcome = frame.Original.OriginalOutcome
            ?? throw new InvalidOperationException("The genuine original Blob outcome is absent.");
        await ControlledBlobOutcomeLifetimeNativeAssertions.EqualAsync(proof.Expected, originalOutcome.Result);
        var original = frame.Original.Original
            ?? throw new InvalidOperationException("The authenticated original Blob command is absent.");
        await ControlledBlobOutcomeLifetimeNativeAssertions.EqualAsync(proof.Original, original);
        global::KeyLoad.Core.Features.ClusterRouting.Validation.PartitionControlLifetimeCoverage.Require(
            frame.Coverage, frame.Original.Control.Partition, [frame.Original.Resource], MaximumBytes);
        var technical = state.Target.Store.Read(view => state.Target.Database.Principal(view, PrincipalId,
            state.Target.Database.EvaluationClock.GetUtcNow()));
        await Assert.That(technical.PolicyEpoch).IsNotEqualTo(frame.Original.Principal.PolicyEpoch);
        await Assert.That(technical.ClusterAdministrator).IsTrue();
        var request = new ControlledBlobReadRequestV2(frame, MaximumBytes,
            state.Target.Database.Limits.MaxScanRecords, MaximumBytes);
        await Assert.That(NativeSerialization.Serialize(request).Length).IsLessThanOrEqualTo(MaximumBytes);
        var originalCall = new RemoteControlledBlobCall(RemoteDocumentProtocol.Version, proof.Original.Id,
            Guid.NewGuid().ToString("N"), state.Source.PhysicalOwner.VoterIds.First(), state.CallerAddress,
            state.Source.PhysicalOwner, state.Destination, new ControlledBlobReadRequest(frame.Original,
                request.MaximumReadBytes, request.MaximumExaminedRecords, request.MaximumResultBytes), MaximumBytes);
        var wrapper = new RemoteControlledBlobOutcomeCallV2(originalCall, frame.Coverage);
        await Assert.That(RemoteDocumentWire.Encode(wrapper, MaximumBytes).Length).IsLessThanOrEqualTo(MaximumBytes);
        var parentGrant = parent.CreateReadGrant(MaximumBytes, request.MaximumExaminedRecords);
        var work = Work(state.Target, token);
        var grant = work.CreateReadGrant(MaximumBytes, request.MaximumExaminedRecords);
        var actual = state.Target.Database.ReadControlledBlobOutcomeV2(PrincipalId, frame, work, grant, token);
        await Assert.That(actual).IsNull();
        await Assert.That(grant.ReadBytes).IsGreaterThan(0L);
        await Assert.That(grant.ReadBytes).IsLessThanOrEqualTo((long)MaximumBytes);
        await Assert.That(grant.ExaminedRecords).IsLessThanOrEqualTo(request.MaximumExaminedRecords);
        parent.ImportReadGrant(parentGrant, grant.ReadBytes, grant.ExaminedRecords);
        state.Source.Database.ValidateControlledBlobOutcomeV2(frame, parent);
        parent.Check();
        await ControlledBlobOutcomeLifetimeNativeAssertions.UnchangedAsync(state, sourceBefore, targetBefore);
    }
}
