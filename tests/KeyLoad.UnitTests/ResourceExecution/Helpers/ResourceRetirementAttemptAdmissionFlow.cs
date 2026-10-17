using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementAttemptAdmissionFlow
{
    private const string Root = "root";

    internal static async Task ExecuteAsync(ResourceRetirementPreparationValidationState state, CancellationToken token)
    {
        var source = state.Nodes[SourceIndex];
        var control = await RetiringAsync(state, token);
        var context = new ResourceRetirementAttemptContextV1(ResourceRetirementAttemptPurpose.SealBarrier,
            control, null, null);
        var denied = new PrincipalRecord(ResourceRetirementAttemptAdmissionProtocol.Denied, control.Request.TenantId, [], []);
        var deniedOperation = source.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), Root,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(denied)));
        await Assert.That(source.Journal.Submit(deniedOperation, token).Error).IsNull();
        var now = source.Database.EvaluationClock.GetUtcNow();
        var attemptId = Guid.NewGuid();
        var body = new ResourceRetirementPrepareAttemptBodyV1(context, attemptId, Zero,
            now.AddSeconds(source.Database.Limits.QueryDeadlineSeconds));
        var original = Issue(source, body, now);
        var expectedPosition = source.Journal.Log.State.LastIndex + One;
        var result = source.Journal.Submit(original, token);
        await Assert.That(result.Error).IsNull();
        var prepared = result.Get<ResourceRetirementPhaseResultV1>();
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(new ResourceRetirementPhaseReceiptV1(ResourceRetirementPreparationValidationData.Version,
            control.Request.RetirementId, ResourceRetirementPhase.PrepareAttempt, original.Id, control.Lifetime,
            source.PhysicalOwner, expectedPosition, control.IntentDigest), prepared.Receipt);
        var expected = new ResourceRetirementAttemptV1(attemptId, One, body.ExpiresAt, prepared.Receipt,
            ResourceRetirementAttemptStage.Prepared, Root, control.PolicyEpoch,
            ResourceRetirementIdentity.AttemptCommandId(control.Request, ResourceRetirementPhase.SealBarrier,
                attemptId, source.PhysicalOwner.PhysicalShardId), null, null, null, null);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(expected, prepared.State!.SealAttempt!);
        await ResourceRetirementAttemptAdmissionAssertions.OriginalAsync(source, context, original, result, expected, token);
        var replacement = Issue(source, body with
        {
            AttemptId = Guid.NewGuid(),
            ExpectedOrdinal = One
        }, now);
        await Assert.That(source.Journal.Submit(replacement, token).Error).IsEqualTo(ErrorCode.Conflict);
        var expired = Issue(source, body with
        {
            AttemptId = Guid.NewGuid(),
            ExpectedOrdinal = One,
            ExpiresAt = now
        }, now);
        await Assert.That(source.Journal.Submit(expired, token).Error).IsEqualTo(ErrorCode.Validation);
        await ResourceRetirementAttemptAdmissionAssertions.OriginalAsync(source, context, original, result, expected, token);
    }

    private static async Task<ResourceRetirementControlV1> RetiringAsync(
        ResourceRetirementPreparationValidationState state, CancellationToken token)
    {
        var request = state.Preparation.Control.Request;
        var body = new ResourceRetirementBeginBodyV1(request)
        {
            Preparation = state.Preparation.Control,
            OwnerStatuses = state.Statuses
        };
        ResourceRetirementControlV1? control = null;
        for (var index = Zero; index < Voters; index++)
        {
            var result = await ResourceRetirementPreparationValidationSeed.PhaseAsync(state.Nodes[index], request,
                ResourceRetirementPhase.Begin, body, request.RetirementId, token);
            var actual = result.State?.Control ?? throw new InvalidOperationException(Invalid);
            control ??= actual;
            await ResourceRetirementPreparationValidationAssertions.EqualAsync(control, actual);
        }
        foreach (var receiver in control!.Directory.Owners)
        {
            ResourceRetirementOwnerFenceV1? fence = null;
            foreach (var owner in state.Nodes.Where(node => node.PhysicalOwner.PhysicalShardId == receiver.Owner.PhysicalShardId))
            {
                var id = ResourceRetirementIdentity.PhaseCommandId(request, ResourceRetirementPhase.FenceOwner,
                    receiver.Owner.PhysicalShardId);
                var result = await ResourceRetirementPreparationValidationSeed.PhaseAsync(owner, request,
                    ResourceRetirementPhase.FenceOwner, new ResourceRetirementFenceBodyV1(control, receiver), id, token);
                var actual = result.Fence ?? throw new InvalidOperationException(Invalid);
                fence ??= actual;
                await ResourceRetirementPreparationValidationAssertions.EqualAsync(fence, actual);
            }
            var recordedId = ResourceRetirementIdentity.PhaseCommandId(request, ResourceRetirementPhase.RecordOwnerReceipt,
                receiver.Owner.PhysicalShardId);
            _ = await ResourceRetirementPreparationValidationSeed.PhaseAsync(state.Nodes[SourceIndex], request,
                ResourceRetirementPhase.RecordOwnerReceipt, new ResourceRetirementReceiptBodyV1(control, fence!.Receipt),
                recordedId, token);
        }
        return control;
    }

    private static ReplicatedOperation Issue(ControlledPartitionMovementNode source,
        ResourceRetirementPrepareAttemptBodyV1 body, DateTimeOffset now)
    {
        var id = ResourceRetirementIdentity.AttemptCommandId(body.Context, ResourceRetirementPhase.PrepareAttempt, body.AttemptId);
        var phase = new ResourceRetirementPhaseCommandV1(ResourceRetirementPreparationValidationData.Version, body.Context.Retirement!.Request.RetirementId,
            ResourceRetirementPhase.PrepareAttempt, NativeSerialization.Serialize(body));
        return source.Database.CreateResourceRetirementOperation(id, Root, now, phase);
    }
}
