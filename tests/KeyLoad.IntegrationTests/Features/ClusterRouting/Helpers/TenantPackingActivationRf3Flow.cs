using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPackingActivationRf3Flow
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers first, RequestCqrsRf3Callers second, TenantPackingActivationRf3State state,
        CancellationToken token)
    {
        await TenantPackingActivationRf3Assertions.SamePhysicalAsync(state.FirstPlacement, state.SecondPlacement);
        await Assert.That(state.FirstSeed.CommandId).IsEqualTo(state.SecondSeed.CommandId);
        await Assert.That(state.FirstReceipt.Token.AtomicPartitionId).IsNotEqualTo(state.SecondReceipt.Token.AtomicPartitionId);
        await TenantPackingActivationRf3Assertions.SecondAsync(administrator, second, state, token);
        var command = RequestCqrsPhaseFaultProvisioning.UpdateCommand(state.First, Guid.NewGuid());
        var discovery = await DiscoveryAsync(wave, token);
        var committed = await NativeActivationRf3HeldCall.ExecuteAsync(wave, first, state.First, command,
            useMcp: false, discovery, token);
        await TenantPackingActivationRf3Assertions.SecondAsync(administrator, second, state, token);
        await NativeActivationMigrationRf3Operation.VerifyAsync(wave, administrator, first, state.First, command,
            committed.Witness, committed.Receipt, discovery, token);
        await TenantPackingActivationRf3Assertions.SecondAsync(administrator, second, state, token);
        await TenantPackingActivationRf3Assertions.ReplayAsync(first, state.FirstSeed, state.FirstReceipt, token);
        await RequestCqrsPhaseFaultAssertions.VerifyDocumentAsync(first, state.First,
            RequestCqrsRf3Protocol.DocumentJson, 3, token);
        await RequestCqrsFaultReplayContinuation.VerifyAsync(second, administrator.Sdk,
            new(state.Second.Partition, RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId),
            state.SecondSeed, state.SecondReceipt, RequestCqrsRf3Protocol.ChangedDocumentJson, 1,
            RequestCqrsRf3Protocol.DocumentJson, token);
        await TenantPackingActivationRf3Assertions.ReplayAsync(first, command, committed.Receipt, token);
        await SqlRf3Protocol.EqualAsync(state.SecondPlacement,
            await TenantPackingActivationRf3Admission.ReadAsync(administrator, state.Second.Partition, token));
    }

    private static async Task<ReplicaSiloDiscovery[]> DiscoveryAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        var actual = new List<ReplicaSiloDiscovery>();
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            actual.Add(await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(wave.Application, node,
                wave.Profile, token));
        }
        return [.. actual];
    }
}
