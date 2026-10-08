using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RetiredPartitionOutcomeAuthorizationTrial
{
    private const string RepairPrincipalId = "movement-policy-repair";
    private const string HealthyPrincipalId = "movement-policy-healthy";
    private const string WrongPrincipalId = "movement-policy-denied";
    private const long EpochStep = 1;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = ControlledPartitionMovementCorpus.SeedOperation(source.Database);
        var originalReceipt = NativeSerialization.Serialize(source.Database.ResolveOutcome(original).Get<CommitReceipt>());
        var authority = new ControlledPartitionMovementOutcomeAuthority(source, original);
        var root = source.Store.Read(view => source.Database.Principal(view, PhysicalShardCatalogFixture.RootPrincipalId,
            source.Database.EvaluationClock.GetUtcNow()));
        await ConfigureAsync(source, root with { Id = RepairPrincipalId }, root.Id, token);
        await ConfigureAsync(source, root with { Id = WrongPrincipalId, Grants = [], ClusterAdministrator = false }, root.Id, token);
        var originalPayload = NativeSerialization.Deserialize<NativeCommandPayload>(original.NativePayload.Span);
        var originalCommand = NativeSerialization.Deserialize<CommandRequest>(originalPayload.Value.Span);
        var changedSubject = source.Database.CreateNativeOperation(OperationKind.Batch, original.Id, WrongPrincipalId,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(
                ControlledPartitionMovementCorpus.Seed() with { OwnershipEpoch = originalCommand.OwnershipEpoch }));
        await RetiredPartitionOutcomeAuthorizationOracles.DeniedAsync(source, target, changedSubject,
            ErrorCode.PermissionDenied, authority, token);
        await ControlledPartitionMovementReceiptAssertions.ReplayAsync(source.Journal.Submit(original, token), originalReceipt);
        var revoked = root with { Revoked = true, PolicyEpoch = checked(root.PolicyEpoch + EpochStep) };
        await ConfigureAsync(source, revoked, RepairPrincipalId, token);
        await RetiredPartitionOutcomeAuthorizationOracles.DeniedAsync(source, target, original,
            ErrorCode.Unauthenticated, authority, token);
        var restored = root with { PolicyEpoch = checked(revoked.PolicyEpoch + EpochStep) };
        await ConfigureAsync(source, restored, RepairPrincipalId, token);
        var healthy = await ConfigureAsync(source, root with { Id = HealthyPrincipalId, ClusterAdministrator = false }, root.Id, token);
        await RetiredPartitionOutcomeAuthorizationOracles.DeniedAsync(source, target, original,
            ErrorCode.PermissionDenied, authority, token);
        source.Reopen();
        target.Reopen();
        await RetiredPartitionOutcomeAuthorizationOracles.DeniedAsync(source, target, original,
            ErrorCode.PermissionDenied, authority, token);
        var replay = source.Database.ResolveOutcome(healthy.Operation);
        await Assert.That(NativeSerialization.Serialize(replay.Get<PrincipalRecord>())
            .SequenceEqual(healthy.Bytes)).IsTrue();
        await authority.RemainsControlOwnedAsync(source, target);
    }

    private static Task<(ReplicatedOperation Operation, byte[] Bytes)> ConfigureAsync(ControlledPartitionMovementNode source,
        PrincipalRecord principal, string operatorId, CancellationToken cancellationToken)
    {
        var operation = source.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(), operatorId,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        var result = source.Journal.Submit(operation, cancellationToken);
        if (result.Error is { } code)
        { throw Errors.Fail(code, result.SafeDetail!); }
        return Task.FromResult((operation, NativeSerialization.Serialize(result.Get<PrincipalRecord>())));
    }
}
