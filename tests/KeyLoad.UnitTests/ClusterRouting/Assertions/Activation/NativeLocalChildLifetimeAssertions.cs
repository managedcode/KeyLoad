using KeyLoad.Clustering;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;
using P = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(expected).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(actual))).IsTrue();

    internal static async Task RowsAsync(NativeLocalChildLifetimeSeeded state, CancellationToken token)
    {
        var first = NativeLocalChildLifetimeReferences.Expected(state, K.FirstId);
        var second = NativeLocalChildLifetimeReferences.Expected(state, K.SecondId);
        await EqualAsync(new DocumentResult(first, K.Revision, K.FirstJson, false, []),
            state.Leader.Database.Database.GetDocument(P.Root, first, cancellationToken: token)
                ?? throw new InvalidOperationException(K.Missing));
        await EqualAsync(new DocumentResult(second, K.Revision, K.SecondJson, false, []),
            state.Leader.Database.Database.GetDocument(P.Root, second, cancellationToken: token)
                ?? throw new InvalidOperationException(K.Missing));
    }

    internal static async Task QueryAsync(NativeLocalChildLifetimeSeeded state,
        NativeLocalChildLifetimeInput input, PartitionQueryLeafResultV1 actual)
    {
        var expected = new PartitionQueryCandidateV1[]
        {
            new(K.Version, NativeLocalChildLifetimeReferences.Expected(state, K.FirstId),
                new(K.FirstId, K.Revision, K.FirstJson, false, []), []),
            new(K.Version, NativeLocalChildLifetimeReferences.Expected(state, K.SecondId),
                new(K.SecondId, K.Revision, K.SecondJson, false, []), [])
        };
        var rows = actual.Candidates.OrderBy(row => row.Reference.Id, StringComparer.Ordinal).ToArray();
        await Assert.That(rows.Length).IsEqualTo(expected.Length);
        for (var index = K.Empty; index < expected.Length; index++)
        {
            await EqualAsync(expected[index], rows[index]);
        }
        await Assert.That(actual.Version).IsEqualTo(K.Version);
        await Assert.That(actual.Partition).IsEqualTo(P.Partition);
        await Assert.That(actual.NodeId).IsEqualTo(state.Leader.Database.Database.Store.Identity.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(state.PhysicalOwner.Incarnation);
        await Assert.That(actual.ReadGeneration).IsEqualTo(state.Leader.Database.Database.Store.Identity.ReadGeneration);
        await Assert.That(actual.CutPosition).IsEqualTo(state.Images[state.Leader].Position);
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
        await Assert.That<object>(reservation.Settlement).IsSameReferenceAs(reservation.Child.Settlement);
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
