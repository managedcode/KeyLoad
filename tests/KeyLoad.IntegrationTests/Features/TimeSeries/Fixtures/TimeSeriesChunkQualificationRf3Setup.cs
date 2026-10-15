using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class TimeSeriesChunkQualificationRf3Setup
{
    private const long SeedOutboxCount = 5L;
    internal static async Task<TimeSeriesChunkQualificationRf3State> RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers root, CancellationToken token)
    {
        var first = new PartitionRef(TimeSeriesChunkQualificationRf3Data.TenantPrefix + Guid.NewGuid().ToString(TimeSeriesChunkQualificationRf3Data.GuidFormat), TimeSeriesChunkQualificationRf3Data.DatabaseName, TimeSeriesChunkQualificationRf3Data.DomainName, Guid.NewGuid().ToString(TimeSeriesChunkQualificationRf3Data.GuidFormat));
        var second = first with
        {
            PartitionKey = Guid.NewGuid().ToString(TimeSeriesChunkQualificationRf3Data.GuidFormat)
        };
        PartitionRef[] partitions = [first, second];
        await Assert.That(second.AtomicPartitionId).IsNotEqualTo(first.AtomicPartitionId);
        var definition = new ResourceDefinition(TimeSeriesChunkQualificationRf3Data.Set, ResourceKind.TimeSeries, first.TransactionDomainId)
        {
            FieldPolicies = [new(TimeSeriesChunkQualificationRf3Data.PrivatePath, TimeSeriesChunkQualificationRf3Data.RestrictedClass, TimeSeriesChunkQualificationRf3Data.ReadGrant, TimeSeriesChunkQualificationRf3Data.UseGrant)]
        };
        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
            Guid.NewGuid(), new(first.TenantId, first.DatabaseId, definition), token)));
        var reader = await McpPersistedIdentity.CreateAsync(fixture, first, TimeSeriesChunkQualificationRf3Data.Set, Capability.SeriesRead, token);
        var state = new TimeSeriesChunkQualificationRf3State(partitions, Guid.NewGuid(), reader, fixture.AdminKey);
        for (var index = 0; index < partitions.Length; index++)
        {
            await CommitAsync(root, state, TimeSeriesChunkQualificationRf3Data.Seed(partitions[index], state.Window, index == TimeSeriesChunkQualificationRf3Data.SiblingScope),
                TimeSeriesChunkQualificationRf3Data.SeedReceipts, token);
            state.Outboxes[index] = await ReadOutboxAsync(root, state, index, token);
        }
        return state;
    }
    internal static async Task CommitAsync(RequestCqrsRf3Callers root, TimeSeriesChunkQualificationRf3State state,
        CommandRequest command, ImmutableArray<MutationReceipt> mutations, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(command, token));
        state.Originals.Add((command, actual));
        var owner = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(TimeSeriesChunkQualificationRf3Data.PlacementVersion, command.Partition), token));
        await SqlRf3Protocol.EqualAsync(command.Partition, owner.Partition);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(command.CommandId,
            new(owner.Incarnation, command.Partition.AtomicPartitionId, actual.Token.Position, owner.PlacementEpoch), mutations,
            DurabilityProfile.QuorumProcessDurable), actual);
    }
    internal static async Task PolicyAsync(RequestCqrsRf3Callers root, TimeSeriesChunkQualificationRf3State state,
        long epoch, bool allow, bool use, CancellationToken token)
    {
        var expected = state.Reader.Principal with
        {
            PolicyEpoch = epoch,
            Grants = allow ? [new(state.Partitions[TimeSeriesChunkQualificationRf3Data.PrimaryScope].DatabaseId, TimeSeriesChunkQualificationRf3Data.Set, Capability.SeriesRead)] : [],
            FieldGrants = use ? [TimeSeriesChunkQualificationRf3Data.UseGrant] : []
        };
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token)));
    }
    internal static async Task<OutboxStatus> ReadOutboxAsync(RequestCqrsRf3Callers root, TimeSeriesChunkQualificationRf3State state,
        int index, CancellationToken token)
    {
        var actual = await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partitions[index], token);
        var count = index == TimeSeriesChunkQualificationRf3Data.SiblingScope ? SeedOutboxCount : SeedOutboxCount + state.Purged + (state.Phase == TimeSeriesChunkQualificationRf3Data.DroppedPhase ? 1L : 0L);
        await Assert.That(actual.Head.Tail).IsEqualTo(count);
        await Assert.That(actual.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(actual.Head.StoredRecords).IsEqualTo(count);
        await Assert.That(actual.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(actual.Consumers).IsEmpty();
        return actual;
    }
}
