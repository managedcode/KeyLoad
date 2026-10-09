using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Changes genuine persisted policy while an acknowledged cancellation has not been observed.</summary>
internal static class PartitionMovementParentPolicyRestoreTrial
{
    private const string RepairPrincipalId = "parent-observation-policy-repair";
    private const string DeniedPrincipalId = "parent-observation-policy-denied";

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        PartitionMovementParentNativeFixture fixture, PartitionMoveRequest request, Guid phaseId)
    {
        var root = source.Store.Read(view => source.Database.Principal(view, PhysicalShardCatalogFixture.RootPrincipalId,
            source.Database.EvaluationClock.GetUtcNow()));
        await ConfigureAsync(source, root with { Id = RepairPrincipalId }, root.Id);
        await ConfigureAsync(source, root with { Id = DeniedPrincipalId, ClusterAdministrator = false }, root.Id);
        await DeniedReadAsync(source, target, () => fixture.Read(request, phaseId, DeniedPrincipalId), ErrorCode.PermissionDenied);
        await DeniedReadAsync(source, target, () => fixture.Read(request, phaseId, RepairPrincipalId), ErrorCode.PermissionDenied);
        await ConfigureAsync(source, root with { Revoked = true, PolicyEpoch = checked(root.PolicyEpoch + 1) }, RepairPrincipalId);
        source.Reopen();
        await DeniedReadAsync(source, target, () => fixture.Read(request, phaseId), ErrorCode.Unauthenticated);
        await ConfigureAsync(source, root with { PolicyEpoch = checked(root.PolicyEpoch + 2) }, RepairPrincipalId);
        source.Reopen();
        await DeniedOriginalUserOutcomeAsync(source, target);
        await PartitionMovementParentIssuanceFaultTrial.ExecuteAsync(source, target, fixture, request, phaseId, DeniedPrincipalId);
        var cold = fixture.Read(request, phaseId);
        await Assert.That(cold.Pending!.OriginalIssuancePolicyEpoch).IsEqualTo(root.PolicyEpoch);
        await Assert.That(cold.Pending.Cancellation!.CancellationPolicyEpoch).IsEqualTo(root.PolicyEpoch);
        await Assert.That(cold.CancellationOutcome).IsNotNull();
        await Assert.That(cold.Header!.TerminalResult).IsNull();
        await Assert.That(cold.Header.PendingOriginalPhaseCommandId).IsEqualTo(phaseId);
        await Assert.That(source.Store.Read(view => PartitionMoveParentStorage.Counter(view,
            PartitionMoveParentKeys.DatabaseActive(request.Partition)))).IsEqualTo(1L);
    }

    internal static async Task DeniedReadAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, Action read, ErrorCode expected)
    {
        var sourceState = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        var targetState = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        var denied = Assert.Throws<KeyLoadException>(read);
        await Assert.That(denied.Code).IsEqualTo(expected);
        await sourceState.AssertUnchangedAsync(source);
        await targetState.AssertUnchangedAsync(target);
    }

    private const string ChangedPolicyDiagnostic = "The principal policy changed since this command was evaluated.";

    private static async Task DeniedOriginalUserOutcomeAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target)
    {
        var sourceState = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        var targetState = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        var denied = source.Database.ResolveOutcome(ControlledPartitionMovementCorpus.SeedOperation(source.Database));
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(denied.Json).IsNull();
        await Assert.That(denied.SafeDetail).IsEqualTo(ChangedPolicyDiagnostic);
        await sourceState.AssertUnchangedAsync(source);
        await targetState.AssertUnchangedAsync(target);
    }

    private static async Task ConfigureAsync(ControlledPartitionMovementNode source, PrincipalRecord principal, string operatorId)
    {
        var operation = source.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), operatorId,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        var result = source.Journal.Submit(operation, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(result.Get<PrincipalRecord>())
            .SequenceEqual(NativeSerialization.Serialize(principal))).IsTrue();
    }
}
