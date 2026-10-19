using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesChunkQualificationClusterSetup
{
    private const long SeedOutboxCount = 5L;
    internal static async Task<TimeSeriesChunkQualificationClusterState> RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers root, CancellationToken token)
    {
        var first = new PartitionRef(TimeSeriesChunkQualificationClusterData.TenantPrefix + Guid.NewGuid().ToString(TimeSeriesChunkQualificationClusterData.GuidFormat), TimeSeriesChunkQualificationClusterData.DatabaseName, TimeSeriesChunkQualificationClusterData.DomainName, Guid.NewGuid().ToString(TimeSeriesChunkQualificationClusterData.GuidFormat));
        var second = first with
        {
            PartitionKey = Guid.NewGuid().ToString(TimeSeriesChunkQualificationClusterData.GuidFormat)
        };
        PartitionRef[] partitions = [first, second];
        await Assert.That(second.AtomicPartitionId).IsNotEqualTo(first.AtomicPartitionId);
        var definition = new ResourceDefinition(TimeSeriesChunkQualificationClusterData.Set, ResourceKind.TimeSeries, first.TransactionDomainId)
        {
            FieldPolicies = [new(TimeSeriesChunkQualificationClusterData.PrivatePath, TimeSeriesChunkQualificationClusterData.RestrictedClass, TimeSeriesChunkQualificationClusterData.ReadGrant, TimeSeriesChunkQualificationClusterData.UseGrant)]
        };
        await SqlClusterProtocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
            Guid.NewGuid(), new(first.TenantId, first.DatabaseId, definition), token)));
        var reader = await McpPersistedIdentity.CreateAsync(fixture, first, TimeSeriesChunkQualificationClusterData.Set, Capability.SeriesRead, token);
        var state = new TimeSeriesChunkQualificationClusterState(partitions, Guid.NewGuid(), reader, fixture.AdminKey);
        for (var index = 0; index < partitions.Length; index++)
        {
            await CommitAsync(root, state, TimeSeriesChunkQualificationClusterData.Seed(partitions[index], state.Window, index == TimeSeriesChunkQualificationClusterData.SiblingScope),
                TimeSeriesChunkQualificationClusterData.SeedReceipts, token);
            state.Outboxes[index] = await ReadOutboxAsync(root, state, index, token);
        }
        return state;
    }
    internal static async Task CommitAsync(RequestCqrsClusterCallers root, TimeSeriesChunkQualificationClusterState state,
        CommandRequest command, ImmutableArray<MutationReceipt> mutations, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        state.Originals.Add((command, actual));
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(actual.Token.Incarnation, command.Partition.AtomicPartitionId, actual.Token.Position), mutations,
            DurabilityProfile.ProcessDurable, 3), actual);
    }
    internal static async Task PolicyAsync(RequestCqrsClusterCallers root, TimeSeriesChunkQualificationClusterState state,
        long epoch, bool allow, bool use, CancellationToken token)
    {
        var expected = state.Reader.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allow ? [new(state.Partitions[TimeSeriesChunkQualificationClusterData.PrimaryScope].DatabaseId, TimeSeriesChunkQualificationClusterData.Set, Capability.SeriesRead)] : [],
            FieldGrants = use ? [TimeSeriesChunkQualificationClusterData.UseGrant] : []
        };
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
    internal static async Task<OutboxStatus> ReadOutboxAsync(RequestCqrsClusterCallers root, TimeSeriesChunkQualificationClusterState state,
        int index, CancellationToken token)
    {
        var actual = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partitions[index], token);
        var count = index == TimeSeriesChunkQualificationClusterData.SiblingScope ? SeedOutboxCount : SeedOutboxCount + state.Purged + (state.Phase == TimeSeriesChunkQualificationClusterData.DroppedPhase ? 1L : 0L);
        await Assert.That(actual.Head.Tail).IsEqualTo(count);
        await Assert.That(actual.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(actual.Head.StoredRecords).IsEqualTo(count);
        await Assert.That(actual.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(actual.Consumers).IsEmpty();
        return actual;
    }
}
