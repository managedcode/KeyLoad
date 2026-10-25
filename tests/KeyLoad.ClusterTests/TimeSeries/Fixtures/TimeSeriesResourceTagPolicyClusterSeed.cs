using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.Search;
namespace KeyLoad.ClusterTests.TimeSeries;

internal static class TimeSeriesResourceTagPolicyClusterSeed
{
    internal const string FirstId = "policy-first";
    internal const string SecondId = "policy-second";
    internal const string ChangedGrant = "time-series.private.next-read";

    internal static async Task<TimeSeriesResourceTagPolicyClusterState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var partition = new PartitionRef("series-resource-policy-" + Guid.NewGuid().ToString("N"),
            TimeSeriesClusterScenario.Database, TimeSeriesClusterScenario.TransactionDomain, Guid.NewGuid().ToString("N"));
        var scenario = new TimeSeriesClusterScenario(partition);
        var definition = new ResourceDefinition(TimeSeriesClusterScenario.Set, ResourceKind.TimeSeries,
            partition.TransactionDomainId)
        {
            FieldPolicies = [new(TimeSeriesClusterScenario.SecretField, TimeSeriesClusterScenario.SecretClassification,
            TimeSeriesClusterScenario.SecretGrant, TimeSeriesClusterScenario.SecretGrant)]
        };
        var createId = Guid.NewGuid();
        await SqlClusterProtocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigureResourceAsync(createId,
                new(partition.TenantId, partition.DatabaseId, definition), token)));
        var opened = await TimeSeriesResourceChunkPolicyClusterEnrollment.OpenAsync(administrator, partition, token);
        var append = new CommandRequest(Guid.NewGuid(), partition,
            [new AppendSamples(TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series,
                [TimeSeriesClusterScenario.Data(FirstId, TimeSeriesClusterScenario.Start, 3d),
                    TimeSeriesClusterScenario.Data(SecondId, TimeSeriesClusterScenario.Start.AddTicks(1), 7d)],
                TimeSeriesClusterScenario.PrivateTags)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(append, token));
        await Assert.That(receipt.CommandId).IsEqualTo(append.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await SqlClusterProtocol.EqualAsync(new[] { new MutationReceipt("appendSamples", TimeSeriesClusterScenario.Set,
            TimeSeriesClusterScenario.Series, 2L) }, receipt.Mutations.ToArray());
        var sealedWindow = await TimeSeriesResourceChunkPolicyClusterEnrollment.SealAsync(administrator, partition, opened.WindowId, token);
        var principal = new PrincipalRecord("series-resource-reader-" + Guid.NewGuid().ToString("N"),
            partition.TenantId, [new(partition.DatabaseId, TimeSeriesClusterScenario.Set, Capability.SeriesRead)],
            [TimeSeriesClusterScenario.SecretGrant]);
        var identity = await TextClusterScenario.ConfigureIdentityAsync(fixture, principal, token);
        await SqlClusterProtocol.EqualAsync(principal, identity.Principal);
        var state = new TimeSeriesResourceTagPolicyClusterState(scenario, identity, definition, append, receipt, createId, opened.WindowId,
            [(opened.Request, opened.Receipt), sealedWindow])
        {
            Outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(partition, token))
        };
        return state;
    }
}
