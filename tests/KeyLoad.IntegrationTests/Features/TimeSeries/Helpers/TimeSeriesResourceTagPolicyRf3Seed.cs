using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.Search;
namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesResourceTagPolicyRf3Seed
{
    internal const string FirstId = "policy-first";
    internal const string SecondId = "policy-second";
    internal const string ChangedGrant = "time-series.private.next-read";

    internal static async Task<TimeSeriesResourceTagPolicyRf3State> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var partition = new PartitionRef("series-resource-policy-" + Guid.NewGuid().ToString("N"),
            TimeSeriesRf3Scenario.Database, TimeSeriesRf3Scenario.TransactionDomain, Guid.NewGuid().ToString("N"));
        var scenario = new TimeSeriesRf3Scenario(partition);
        var definition = new ResourceDefinition(TimeSeriesRf3Scenario.Set, ResourceKind.TimeSeries,
            partition.TransactionDomainId)
        {
            FieldPolicies = [new(TimeSeriesRf3Scenario.SecretField, TimeSeriesRf3Scenario.SecretClassification,
            TimeSeriesRf3Scenario.SecretGrant, TimeSeriesRf3Scenario.SecretGrant)]
        };
        var createId = Guid.NewGuid();
        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigureResourceAsync(createId,
                new(partition.TenantId, partition.DatabaseId, definition), token)));
        var placement = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ReadAtomicPartitionPlacementAsync(new(1, partition), token));
        var opened = await TimeSeriesResourceChunkPolicyRf3Enrollment.OpenAsync(administrator, partition, token);
        var append = new CommandRequest(Guid.NewGuid(), partition,
            [new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                [TimeSeriesRf3Scenario.Data(FirstId, TimeSeriesRf3Scenario.Start, 3d),
                    TimeSeriesRf3Scenario.Data(SecondId, TimeSeriesRf3Scenario.Start.AddTicks(1), 7d)],
                TimeSeriesRf3Scenario.PrivateTags)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(append, token));
        await Assert.That(receipt.CommandId).IsEqualTo(append.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(new[] { new MutationReceipt("appendSamples", TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, 2L) }, receipt.Mutations.ToArray());
        var sealedWindow = await TimeSeriesResourceChunkPolicyRf3Enrollment.SealAsync(administrator, partition, opened.WindowId, token);
        var principal = new PrincipalRecord("series-resource-reader-" + Guid.NewGuid().ToString("N"),
            partition.TenantId, [new(partition.DatabaseId, TimeSeriesRf3Scenario.Set, Capability.SeriesRead)],
            [TimeSeriesRf3Scenario.SecretGrant]);
        var identity = await NativeTextRf3Scenario.ConfigureIdentityAsync(fixture, principal, token);
        await SqlRf3Protocol.EqualAsync(principal, identity.Principal);
        var state = new TimeSeriesResourceTagPolicyRf3State(scenario, identity, definition, placement, append, receipt, createId, opened.WindowId,
            [(opened.Request, opened.Receipt), sealedWindow])
        {
            Outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(partition, token))
        };
        return state;
    }
}
