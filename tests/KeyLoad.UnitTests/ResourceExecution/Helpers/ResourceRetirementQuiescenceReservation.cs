using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceReservation
{
    private const string Actor = "quiescence-reservation-revoked";

    internal static async Task ReserveAsync(ResourceRetirementQuiescenceState state, CancellationToken token)
    {
        foreach (var receiver in state.Control.Directory.Owners)
        {
            var owner = receiver.Owner.PhysicalShardId == state.Source.PhysicalOwner.PhysicalShardId ? state.Source : state.Target;
            var context = new ResourceRetirementAttemptContextV1(ResourceRetirementAttemptPurpose.OwnerReservation,
                null, state.Control, receiver);
            var attempt = await ResourceRetirementQuiescenceAttempt.PrepareAsync(state, context, Zero, null, token);
            await ResourceRetirementQuiescenceAttempt.UnknownAsync(state, owner, context, attempt, token);
            if (ReferenceEquals(owner, state.Target))
            {
                attempt = await FailedAttemptAsync(state, owner, context, attempt, token);
            }
            var original = ResourceRetirementQuiescenceAttempt.Issue(owner, context, attempt, Root);
            var persisted = await ResourceRetirementQuiescencePrepared.PersistAsync(state, owner, context, attempt, original, token);
            var recovered = ResourceRetirementQuiescencePrepared.Read(owner, context, attempt, token);
            await ResourceRetirementQuiescenceAssertions.EqualAsync(persisted, recovered);
            var result = owner.Journal.Submit(recovered.Original ?? throw new InvalidOperationException(Invalid), token);
            await Assert.That(result.Error).IsNull();
            state.Originals.Add((owner, recovered.Original!, result));
            await ResourceRetirementQuiescenceReservationAssertions.RecordAsync(state, owner, context, attempt, result, token);
        }
    }

    private static async Task<ResourceRetirementAttemptV1> FailedAttemptAsync(ResourceRetirementQuiescenceState state,
        ControlledPartitionMovementNode owner, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, CancellationToken token)
    {
        var principal = new PrincipalRecord(Actor, state.Request.TenantId,
            [new(state.Request.DatabaseId, state.Request.ResourceName, Capability.DocumentsRead)], [])
        {
            ClusterAdministrator = true
        };
        await ConfigureAsync(state, owner, principal, token);
        var original = ResourceRetirementQuiescenceAttempt.Issue(owner, context, attempt, Actor);
        var persisted = await ResourceRetirementQuiescencePrepared.PersistAsync(state, owner, context, attempt, original, token);
        await ResourceRetirementQuiescenceAttempt.BlockAdvanceAsync(state, context, attempt, token);
        await ConfigureAsync(state, owner, principal with
        {
            Revoked = true,
            PolicyEpoch = principal.PolicyEpoch + One
        }, token);
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(owner);
        owner.Reopen();
        await before.AssertUnchangedAsync(owner);
        var recovered = ResourceRetirementQuiescencePrepared.Read(owner, context, attempt, token);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(persisted, recovered);
        var denied = await ResourceRetirementQuiescenceReservationFailures.SubmitAsync(state, owner,
            recovered.Original ?? throw new InvalidOperationException(Invalid), ErrorCode.Unauthenticated, null, token);
        var closed = await ResourceRetirementQuiescencePrepared.CloseAsync(state, owner, recovered, denied, token);
        var next = await ResourceRetirementQuiescenceAttempt.PrepareAsync(state, context, attempt.Ordinal, closed, token);
        var previous = state.Source.Database.ReadResourceRetirementAttempt(Root, context, attempt.AttemptId,
            ResourceRetirementQuiescencePhases.Work(state.Source, token));
        await ResourceRetirementQuiescenceAssertions.EqualAsync(attempt with
        {
            Stage = ResourceRetirementAttemptStage.Failed,
            OriginalFingerprint = closed.Fingerprint,
            Error = denied.Error,
            SafeDetail = denied.SafeDetail
        }, previous);
        return next;
    }

    private static async Task ConfigureAsync(ResourceRetirementQuiescenceState state, ControlledPartitionMovementNode owner,
        PrincipalRecord principal, CancellationToken token)
    {
        var original = owner.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), Root,
            owner.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        var result = owner.Journal.Submit(original, token);
        await Assert.That(result.Error).IsNull();
        await ResourceRetirementQuiescenceAssertions.EqualAsync(principal, result.Get<PrincipalRecord>());
        state.Originals.Add((owner, original, result));
    }
}
