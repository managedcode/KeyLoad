using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class ControlledBlobOutcomeLifetimeNativeRefusals
{
    private const long EpochStep = 1;
    private const int MinimalGrant = 1;

    internal static async Task ExecuteAsync(ControlledBlobOutcomeLifetimeNativeState state,
        ControlledBlobOutcomeLifetimeNativeExecution execution, CancellationToken token)
    {
        foreach (var proof in state.Proofs)
        {
            var frame = ControlledBlobOutcomeLifetimeNativeReads.Capture(state, proof,
                ControlledBlobOutcomeLifetimeNativeReads.Work(state.Source, token));
            await RefuseAsync(state, frame with
            {
                Coverage = null!
            }, ErrorCode.Corruption, token);
            var partition = ControlledPartitionMovementCorpus.Partition;
            var foreign = state.Source.Store.Read(view => ResourceLifetimeCatalog.Read(view, partition.TenantId,
                partition.DatabaseId, ControlledPartitionMovementCorpus.Collection,
                state.Source.Database.Resource(view, partition, ControlledPartitionMovementCorpus.Collection, ResourceKind.Collection)))
                ?? throw new InvalidOperationException("The genuine other-resource lifetime is absent.");
            await RefuseAsync(state, frame with
            {
                Coverage = new ResourceLifetimeCoverageV1(partition, [foreign])
            },
                ErrorCode.Corruption, token);
            await ControlledBlobOutcomeLifetimeNativeReads.HealthyAsync(state, proof, token);
        }
        await LimitsAsync(state, token);
        await SourcePolicyAsync(state, execution, token);
    }

    private static async Task RefuseAsync(ControlledBlobOutcomeLifetimeNativeState state,
        ControlledBlobReadFrameV2 frame, ErrorCode code, CancellationToken token)
    {
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var work = ControlledBlobOutcomeLifetimeNativeReads.Work(state.Target, token);
        var grant = work.CreateReadGrant(ControlledBlobOutcomeLifetimeNativeReads.MaximumBytes,
            state.Target.Database.Limits.MaxScanRecords);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => state.Target.Database.ReadControlledBlobOutcomeV2(
            "root", frame, work, grant, token));
        await Assert.That(error.Code).IsEqualTo(code);
        await ControlledBlobOutcomeLifetimeNativeAssertions.UnchangedAsync(state, source, target);
    }

    private static async Task LimitsAsync(ControlledBlobOutcomeLifetimeNativeState state, CancellationToken token)
    {
        var frame = ControlledBlobOutcomeLifetimeNativeReads.Capture(state, state.Proofs.First(),
            ControlledBlobOutcomeLifetimeNativeReads.Work(state.Source, token));
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var work = ControlledBlobOutcomeLifetimeNativeReads.Work(state.Target, token);
        var grant = work.CreateReadGrant(MinimalGrant, MinimalGrant);
        var exceeded = Assert.ThrowsExactly<KeyLoadException>(() => state.Target.Database.ReadControlledBlobOutcomeV2(
            "root", frame, work, grant, token));
        await Assert.That(exceeded.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        var cancelledWork = ControlledBlobOutcomeLifetimeNativeReads.Work(state.Target, cancelled.Token);
        var cancelledGrant = cancelledWork.CreateReadGrant(ControlledBlobOutcomeLifetimeNativeReads.MaximumBytes,
            state.Target.Database.Limits.MaxScanRecords);
        await cancelled.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => state.Target.Database.ReadControlledBlobOutcomeV2(
            "root", frame, cancelledWork, cancelledGrant, cancelled.Token));
        await ControlledBlobOutcomeLifetimeNativeAssertions.UnchangedAsync(state, source, target);
        await ControlledBlobOutcomeLifetimeNativeReads.HealthyAsync(state, state.Proofs.First(), token);
    }

    private static async Task SourcePolicyAsync(ControlledBlobOutcomeLifetimeNativeState state,
        ControlledBlobOutcomeLifetimeNativeExecution execution, CancellationToken token)
    {
        const string repairId = "outcome-lifetime-repair-administrator";
        var principal = state.Source.Store.Read(view => state.Source.Database.Principal(view, "root",
            state.Source.Database.EvaluationClock.GetUtcNow()));
        var repair = new PrincipalRecord(repairId, principal.TenantId, [], []) { ClusterAdministrator = true };
        await ConfigureAsync(state.Source, "root", repair, token);
        var rows = ControlledBlobOutcomeLifetimeNativeAssertions.OriginalRows(state);
        var work = ControlledBlobOutcomeLifetimeNativeReads.Work(state.Source, token);
        var frame = ControlledBlobOutcomeLifetimeNativeReads.Capture(state, state.Proofs.First(), work);
        var targetWork = ControlledBlobOutcomeLifetimeNativeReads.Work(state.Target, token);
        var grant = targetWork.CreateReadGrant(ControlledBlobOutcomeLifetimeNativeReads.MaximumBytes,
            state.Target.Database.Limits.MaxScanRecords);
        await Assert.That(state.Target.Database.ReadControlledBlobOutcomeV2("root", frame, targetWork, grant, token)).IsNull();
        var demoted = principal with
        {
            ClusterAdministrator = false,
            Grants = [],
            FieldGrants = [],
            PolicyEpoch = checked(principal.PolicyEpoch + EpochStep)
        };
        await ConfigureAsync(state.Source, repairId, demoted, token);
        await RefuseNewCommandAsync(state, token);
        await OldDeniedAsync(state, frame, work, token);
        var repaired = principal with
        {
            PolicyEpoch = checked(demoted.PolicyEpoch + EpochStep)
        };
        await ConfigureAsync(state.Source, repairId, repaired, token);
        await OldDeniedAsync(state, frame, work, token);
        var fresh = await ControlledBlobOutcomeLifetimeNativeCommands.RepairedBeginAsync(execution, token);
        await Assert.That(state.Proofs.Any(proof => proof.Original.Id == fresh.Original.Id)).IsFalse();
        await ControlledBlobOutcomeLifetimeNativeReads.HealthyAsync(state, fresh, token);
        await ControlledBlobOutcomeLifetimeNativeAssertions.OriginalRowsUnchangedAsync(state, rows);
        await OldDeniedAsync(state, frame, work, token);
    }

    private static async Task OldDeniedAsync(ControlledBlobOutcomeLifetimeNativeState state,
        ControlledBlobReadFrameV2 original, global::KeyLoad.Core.ReadExecutionBudget work, CancellationToken token)
    {
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => state.Source.Database.ValidateControlledBlobOutcomeV2(original, work));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        foreach (var proof in state.Proofs)
        {
            var denied = Assert.ThrowsExactly<KeyLoadException>(() => ControlledBlobOutcomeLifetimeNativeReads.Capture(state, proof,
                ControlledBlobOutcomeLifetimeNativeReads.Work(state.Source, token)));
            await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
        }
        await ControlledBlobOutcomeLifetimeNativeAssertions.UnchangedAsync(state, before, target);
    }

    private static async Task RefuseNewCommandAsync(ControlledBlobOutcomeLifetimeNativeState state, CancellationToken token)
    {
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var request = new BeginBlobUploadRequest(Guid.NewGuid(), new(ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementBlobSeed.Resource, "outcome-lifetime-demoted"), Guid.NewGuid(), 4, 0);
        var original = state.Source.Database.CreateNativeOperation(OperationKind.BeginBlobUpload, request.CommandId, "root",
            state.Source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        var error = Assert.ThrowsExactly<KeyLoadException>(() => state.Source.Database.TryCaptureControlledDocumentCommand(
            "root", original, ControlledBlobOutcomeLifetimeNativeReads.Work(state.Source, token)));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await ControlledBlobOutcomeLifetimeNativeAssertions.UnchangedAsync(state, source, target);
    }

    private static async Task ConfigureAsync(ControlledPartitionMovementNode source, string administrator,
        PrincipalRecord principal, CancellationToken token)
    {
        var original = source.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), administrator,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        var result = source.Journal.Submit(original, token);
        await Assert.That(result.Error).IsNull();
        await ControlledBlobOutcomeLifetimeNativeAssertions.EqualAsync(principal, result.Get<PrincipalRecord>());
    }
}
