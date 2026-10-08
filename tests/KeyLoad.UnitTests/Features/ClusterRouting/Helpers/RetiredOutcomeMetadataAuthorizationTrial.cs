namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RetiredOutcomeMetadataAuthorizationTrial
{
    private const string DeniedPrincipalId = "movement-metadata-denied";
    private const long RevokedEpoch = 2;

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = ControlledPartitionMovementCorpus.SeedOperation(source.Database);
        var expected = NativeSerialization.Serialize(source.Database.ResolveOutcome(original).Get<CommitReceipt>());
        var authority = new ControlledPartitionMovementOutcomeAuthority(source, original);
        var denied = new PrincipalRecord(DeniedPrincipalId, ControlledPartitionMovementCorpus.Partition.TenantId, [], []);
        Configure(source, denied, token);
        var request = ControlledPartitionMovementCorpus.Seed();
        var deniedOperation = source.Database.CreateNativeOperation(OperationKind.Batch, request.CommandId, denied.Id,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));
        await DeniedFaultAsync(source, target, original, deniedOperation, ErrorCode.PermissionDenied, authority, token);
        Configure(source, denied with { Revoked = true, PolicyEpoch = RevokedEpoch }, token);
        await DeniedFaultAsync(source, target, original, deniedOperation, ErrorCode.Unauthenticated, authority, token);
        await AuthorizedFaultAsync(source, target, original, corruptOutcome: true, token);
        await AuthorizedFaultAsync(source, target, original, corruptOutcome: false, token);
        source.Reopen();
        target.Reopen();
        await ControlledPartitionMovementReceiptAssertions.ReplayAsync(source.Journal.Submit(original, token), expected);
        await authority.RemainsControlOwnedAsync(source, target);
    }

    private static Task DeniedFaultAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ReplicatedOperation original, ReplicatedOperation denied, ErrorCode expected,
        ControlledPartitionMovementOutcomeAuthority authority, CancellationToken token)
        => RetiredOutcomeMetadataFaultScope.WithFaultAsync(source, original, denied.PrincipalId, corruptOutcome: true, corruptLocator: true,
            () => RetiredPartitionOutcomeAuthorizationOracles.DeniedAsync(source, target, denied, expected, authority, token));

    private static Task AuthorizedFaultAsync(ControlledPartitionMovementNode source, ControlledPartitionMovementNode target,
        ReplicatedOperation original, bool corruptOutcome, CancellationToken token)
        => RetiredOutcomeMetadataFaultScope.WithFaultAsync(source, original, original.PrincipalId, corruptOutcome, corruptLocator: !corruptOutcome,
            () => RetiredOutcomeMetadataAuthorizedOracle.CorruptionAsync(source, target, original, token));

    private static void Configure(ControlledPartitionMovementNode source, PrincipalRecord principal, CancellationToken token)
    {
        var operation = source.Database.CreateNativeOperation(OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            PhysicalShardCatalogFixture.RootPrincipalId, source.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)));
        var result = source.Journal.Submit(operation, token);
        if (result.Error is { } code)
        { throw Errors.Fail(code, result.SafeDetail!); }
    }
}
