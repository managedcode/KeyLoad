using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterReplication;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(expected).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(actual))).IsTrue();

    internal static async Task RowsAsync(RequestCqrsCohortNativeNode node, CancellationToken token)
    {
        var first = new EntityRef(P.Partition, P.Collection, K.FirstId);
        var second = new EntityRef(P.Partition, P.Collection, K.SecondId);
        await EqualAsync(new DocumentResult(first, K.Revision, K.FirstJson, false, []),
            node.Journal.Database.GetDocument(P.Root, first, cancellationToken: token));
        await EqualAsync(new DocumentResult(second, K.Revision, K.SecondJson, false, []),
            node.Journal.Database.GetDocument(P.Root, second, cancellationToken: token));
    }

    internal static async Task QueryAsync(NativeLocalChildLifetimeSeeded state,
        NativeLocalChildLifetimeInput input, PartitionQueryLeafResultV1 actual)
    {
        var expected = new PartitionQueryCandidateV1[]
        {
            new(K.Version, new(P.Partition, P.Collection, K.FirstId),
                new(K.FirstId, K.Revision, K.FirstJson, false, []), []),
            new(K.Version, new(P.Partition, P.Collection, K.SecondId),
                new(K.SecondId, K.Revision, K.SecondJson, false, []), [])
        };
        var rows = actual.Candidates.OrderBy(row => row.Reference.Id, StringComparer.Ordinal).ToArray();
        await EqualAsync(expected, rows);
        await Assert.That(actual.Version).IsEqualTo(K.Version);
        await Assert.That(actual.Partition).IsEqualTo(P.Partition);
        await Assert.That(actual.NodeId).IsEqualTo(state.Leader.Journal.Database.Store.Identity.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(state.PhysicalOwner.Incarnation);
        await Assert.That(actual.ReadGeneration).IsEqualTo(state.Leader.Journal.Database.Store.Identity.ReadGeneration);
        await Assert.That(actual.CutPosition).IsEqualTo(state.Images[state.Leader.Journal.Configuration.LocalId].Position);
        await Assert.That(actual.PolicyEpoch).IsEqualTo(input.Request.Principal.PolicyEpoch);
        await Assert.That(actual.SchemaVersion).IsEqualTo(input.Request.Definition.SchemaVersion);
        await Assert.That(actual.ReadBytes > K.Empty).IsTrue();
        await Assert.That(actual.ReadBytes <= input.Request.Original.Plan.MaxReadBytes).IsTrue();
    }

    internal static async Task RetentionAsync(NativeLocalChildLifetimeOperation operation,
        ResourceLifetimeV1 lifetime)
    {
        await Assert.That(operation.Ledger.RetainedEntries).IsEqualTo(K.LifetimeCount);
        await Assert.That(operation.Ledger.RetainedEncodedBytes).IsEqualTo(NativeSerialization.Measure(lifetime));
        await Assert.That(operation.Parent.Dependencies.SetEquals([lifetime])).IsTrue();
    }

    internal static async Task CopiedAsync(NativeRequestWorkChildReservation reservation,
        NativeLocalChildLifetimeOperation operation, ResourceLifetimeV1 lifetime)
    {
        await Assert.That(reservation.Parent).IsSameReferenceAs(operation.Parent);
        await Assert.That(reservation.Child.ResultRetention).IsSameReferenceAs(operation.Ledger);
        await Assert.That(reservation.Child.Dependencies.SetEquals([lifetime])).IsTrue();
        await Assert.That(reservation.Settlement).IsSameReferenceAs(reservation.Child.Settlement);
    }

    internal static async Task RefusedAsync(Func<NativeRequestWorkLease> action)
    {
        NativeRequestWorkLease? unexpected = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => unexpected = action())
                ?? throw new InvalidOperationException(K.Missing);
            await Assert.That(error.Code).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(error.Message).IsEqualTo(NativeRequestWorkLimits.LeaseIdentityMessage);
        }, failures);
        if (unexpected is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var outcome = await unexpected.CompleteAsync(failures.FirstOrDefault(), null);
                if (outcome.CleanupOrJoinFailure is { } cleanup)
                {
                    throw cleanup;
                }
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
