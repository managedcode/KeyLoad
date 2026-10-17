using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.UnitTests.Features.ClusterReplication;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeSeed
{
    private static readonly TimeSpan ElectionPoll = TimeSpan.FromMilliseconds(250);

    internal static async Task<NativeLocalChildLifetimeSeeded> CreateAsync(
        RequestCqrsCohortRuntimeFixture fixture, CancellationToken token)
    {
        var leader = await LeaderAsync(fixture, token);
        await NativeLocalChildLifetimeReferences.CatalogsAsync(fixture, token);
        var principal = new PrincipalRecord(K.OtherSubject, P.Partition.TenantId,
            [new(P.Partition.DatabaseId, P.Collection, Capability.Query | Capability.DocumentsRead)], []);
        _ = (await SubmitAsync(leader, OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            new ConfigurePrincipalRequest(principal), token)).Get<PrincipalRecord>();
        var request = new CommandRequest(Guid.NewGuid(), P.Partition,
            [new PutDocument(P.Collection, K.FirstId, K.FirstJson, K.Empty),
                new PutDocument(P.Collection, K.SecondId, K.SecondJson, K.Empty)]);
        var index = checked(leader.Journal.Log.State.LastIndex + K.NextIndex);
        var operation = leader.Journal.Database.CreateNativeOperation(OperationKind.Batch, request.CommandId,
            P.Root, TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(request));
        var receipt = (await leader.Journal.Consensus.SubmitAsync(operation, token)).Get<CommitReceipt>();
        var physical = leader.Journal.Database.Store.Read(PhysicalShardCatalogRecordSerialization.Read)?.DefaultShard
            ?? throw new InvalidOperationException(K.Missing);
        var expected = new CommitReceipt(request.CommandId,
            new(physical.Incarnation, P.Partition.AtomicPartitionId, index, physical.PlacementEpoch),
            [new(K.MutationKind, P.Collection, K.FirstId, K.Revision),
                new(K.MutationKind, P.Collection, K.SecondId, K.Revision)], DurabilityProfile.ProcessDurable);
        await NativeLocalChildLifetimeAssertions.EqualAsync(expected, receipt);
        var images = new Dictionary<string, (long Position, string[] Records)>(StringComparer.Ordinal);
        foreach (var voter in K.Voters)
        {
            var node = fixture.Node(voter);
            await node.Journal.Consensus.ReadBarrierAsync(token);
            await NativeLocalChildLifetimeAssertions.RowsAsync(node, token);
            images.Add(voter, ReplicaBarrierDeadlineAssertions.Image(node));
        }
        var other = fixture.Node(K.Voters.First(voter => voter != leader.Journal.Configuration.LocalId));
        return new(leader, other, physical, operation, receipt, images);
    }

    private static async Task<OperationResult> SubmitAsync<T>(RequestCqrsCohortNativeNode node, OperationKind kind,
        Guid id, T body, CancellationToken token)
    {
        var operation = node.Journal.Database.CreateNativeOperation(kind, id, P.Root,
            TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(body));
        return await node.Journal.Consensus.SubmitAsync(operation, token);
    }

    private static async Task<RequestCqrsCohortNativeNode> LeaderAsync(
        RequestCqrsCohortRuntimeFixture fixture, CancellationToken token)
    {
        while (true)
        {
            foreach (var voter in K.Voters)
            {
                var node = fixture.Node(voter);
                if (await node.Journal.Consensus.IsLeaderAsync(token))
                {
                    return node;
                }
            }
            await Task.Delay(ElectionPoll, TimeProvider.System, token);
        }
    }
}
