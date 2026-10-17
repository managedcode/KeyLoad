using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterReplication;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeReferences
{
    internal static EntityRef Expected(RequestCqrsCohortNativeNode node, string id)
        => new(P.Partition, P.Collection, id)
        {
            Lifetime = new(ExpectedLifetime(node.BaselineResourceCreation
                ?? throw new InvalidOperationException(K.Missing)).Incarnation.Value)
        };

    internal static async Task CatalogsAsync(RequestCqrsCohortRuntimeFixture fixture, CancellationToken token)
    {
        var original = fixture.Node(K.Voters.First()).BaselineResourceCreation
            ?? throw new InvalidOperationException(K.Missing);
        var expected = ExpectedLifetime(original);
        foreach (var voter in K.Voters)
        {
            token.ThrowIfCancellationRequested();
            var node = fixture.Node(voter);
            await Assert.That(node.BaselineResourceCreation).IsSameReferenceAs(original);
            var database = node.Journal.Database;
            var actual = database.Store.Read(view => ResourceLifetimeCatalog.Read(view,
                P.Partition.TenantId, P.Partition.DatabaseId, P.Collection,
                database.Resource(view, P.Partition, P.Collection)))
                ?? throw new InvalidOperationException(K.Missing);
            await NativeLocalChildLifetimeAssertions.EqualAsync(expected, actual);
        }
    }

    private static ResourceLifetimeV1 ExpectedLifetime(ReplicatedOperation original)
    {
        var request = NativeCommandPayload.Read<ConfigureResourceRequest>(original);
        return ResourceLifetimeIdentity.Create(original, request.TenantId, request.DatabaseId, request.Definition);
    }
}
