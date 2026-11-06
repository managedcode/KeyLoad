using P = KeyLoad.FunctionalTests.Authorization.NativePolicyEpochProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class NativePolicyEpochProvision
{
    internal static async Task SeedAsync(NativePolicyEpochState state, CancellationToken token)
    {
        state.Resource = new(P.Collection, ResourceKind.Collection, state.Partition.TransactionDomainId);
        await ResourceAsync(state, state.Resource, null, token);
        state.Reader = new(P.Reader, state.Partition.TenantId,
            [new(state.Partition.DatabaseId, P.Collection, Capability.DocumentsRead)], []);
        await PrincipalAsync(state, state.Reader, token);
        state.Seed = new(Guid.NewGuid(), state.Partition, [new PutDocument(P.Collection, P.Document, P.Original, 0)]);
        var before = NativeStoreAssertions.Applied(state.Store, state.Partition);
        state.SeedReceipt = state.Submit(OperationKind.Batch, state.Seed, token, state.Seed.CommandId).Get<CommitReceipt>();
        await NativePolicyEpochAssertions.ReceiptAsync(state, state.Seed, state.SeedReceipt, before, "putDocument", P.FirstRevision);
        state.SeedOutcome = state.Outcome(state.Seed);
        await NativePolicyEpochAssertions.ReadAsync(state, true, false, token);
        await NativePolicyEpochAssertions.ReadAsync(state, true, false, token);
    }

    internal static async Task ResourceAsync(NativePolicyEpochState state, ResourceDefinition expected,
        long? version, CancellationToken token)
    {
        var request = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, expected)
        {
            ExpectedSchemaVersion = version
        };
        var actual = state.Submit(OperationKind.ConfigureResource, request, token).Get<ResourceDefinition>();
        await NativePolicyEpochAssertions.EqualAsync(expected, actual);
        state.Resource = expected;
    }

    internal static async Task PrincipalAsync(NativePolicyEpochState state, PrincipalRecord expected, CancellationToken token)
    {
        var actual = state.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(expected), token).Get<PrincipalRecord>();
        await NativePolicyEpochAssertions.EqualAsync(expected, actual);
        state.Reader = expected;
    }

    internal static async Task HealthyAsync(NativePolicyEpochState state, CancellationToken token)
    {
        state.Healthy = new(Guid.NewGuid(), state.Partition,
            [new PatchDocument(P.Collection, P.Document, [new("/public", PatchKind.Set, "\"changed\"")], P.FirstRevision)]);
        var before = NativeStoreAssertions.Applied(state.Store, state.Partition);
        state.HealthyReceipt = state.Submit(OperationKind.Batch, state.Healthy, token, state.Healthy.CommandId).Get<CommitReceipt>();
        await NativePolicyEpochAssertions.ReceiptAsync(state, state.Healthy, state.HealthyReceipt,
            before, "patchDocument", P.UpdatedRevision);
        state.HealthyOutcome = state.Outcome(state.Healthy);
        await NativePolicyEpochAssertions.ReadAsync(state, true, true, token);
    }
}
