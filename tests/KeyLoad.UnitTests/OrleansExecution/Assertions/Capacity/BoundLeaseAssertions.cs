using KeyLoad.Query.QueryExecution;
using K = KeyLoad.UnitTests.OrleansExecution.BoundLeaseProtocol;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class BoundLeaseAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(OrleansSerialization.Serialize(expected).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(actual))).IsTrue();

    internal static async Task RowsAsync(BoundLeaseSeeded state, CancellationToken token)
    {
        var first = BoundLeaseReferences.Expected(state, K.FirstId);
        var second = BoundLeaseReferences.Expected(state, K.SecondId);
        await EqualAsync(new DocumentResult(first, K.Revision, K.FirstJson, false, []),
            state.Leader.Database.Database.GetDocument(K.Root, first, cancellationToken: token)
                ?? throw new InvalidOperationException(K.Missing));
        await EqualAsync(new DocumentResult(second, K.Revision, K.SecondJson, false, []),
            state.Leader.Database.Database.GetDocument(K.Root, second, cancellationToken: token)
                ?? throw new InvalidOperationException(K.Missing));
    }

    internal static async Task QueryAsync(BoundLeaseSeeded state,
        PartitionQueryOwnedLeafRequest input, PartitionQueryLeafResult actual)
    {
        var expected = new PartitionQueryCandidate[]
        {
            new(K.Version, BoundLeaseReferences.Expected(state, K.FirstId),
                new(K.FirstId, K.Revision, K.FirstJson, false, []), []),
            new(K.Version, BoundLeaseReferences.Expected(state, K.SecondId),
                new(K.SecondId, K.Revision, K.SecondJson, false, []), [])
        };
        var rows = actual.Candidates.OrderBy(row => row.Reference.Id, StringComparer.Ordinal).ToArray();
        await Assert.That(rows.Length).IsEqualTo(expected.Length);
        for (var index = K.Empty; index < expected.Length; index++)
        {
            await EqualAsync(expected[index], rows[index]);
        }
        await Assert.That(actual.Version).IsEqualTo(K.Version);
        await Assert.That(actual.Partition).IsEqualTo(K.Partition);
        await Assert.That(actual.NodeId).IsEqualTo(state.Leader.Database.Database.Store.Identity.NodeId);
        await Assert.That(actual.Incarnation).IsEqualTo(state.PhysicalOwner.Incarnation);
        await Assert.That(actual.ReadGeneration).IsEqualTo(state.Leader.Database.Database.Store.Identity.ReadGeneration);
        await Assert.That(actual.CutPosition).IsEqualTo(state.Images[state.Leader].Position);
        await Assert.That(actual.PolicyEpoch).IsEqualTo(input.Principal.PolicyEpoch);
        await Assert.That(actual.SchemaVersion).IsEqualTo(input.Definition.SchemaVersion);
        await Assert.That(actual.ReadBytes > K.Empty).IsTrue();
        await Assert.That(actual.ReadBytes <= input.Plan.MaxReadBytes).IsTrue();
    }

    internal static async Task RetentionAsync(BoundLeaseOperation operation, ResourceLifetime lifetime)
    {
        await Assert.That(operation.Ledger.RetainedEntries).IsEqualTo(K.LifetimeCount);
        await Assert.That(operation.Ledger.RetainedEncodedBytes).IsEqualTo(OrleansSerialization.Measure(lifetime));
        await Assert.That(operation.Parent.Dependencies.SetEquals([lifetime])).IsTrue();
    }
}
