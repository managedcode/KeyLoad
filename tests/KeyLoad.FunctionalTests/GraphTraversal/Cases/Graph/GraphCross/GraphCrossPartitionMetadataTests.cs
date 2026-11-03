using System.Reflection;

namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed class GraphCrossPartitionMetadataTests
{
    private static readonly (Type Type, string Alias, string[] Members, uint[] Ids)[] Contracts =
    [
        (typeof(GraphEdgeOwnerVersion), "keyload.contract.graph-edge-owner-version",
            ["Revision", "Deleted"], [1, 2]),
        (typeof(GraphCrossPartitionDeliveryIntent), "keyload.contract.graph-cross-partition-delivery-intent",
            ["SourcePartition", "Graph", "EdgeId", "Destination", "Revision", "Deleted", "Edge",
                "OriginalPrincipalId", "OriginalPolicyEpoch", "Fingerprint"], [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]),
        (typeof(ApplyCrossPartitionReverseEdge), "keyload.contract.apply-cross-partition-reverse-edge",
            ["SourcePartition", "Graph", "EdgeId", "Destination", "ExpectedRevision"], [0, 1, 2, 3, 4]),
        (typeof(CompleteCrossPartitionReverseEdge), "keyload.contract.graph-reverse-edge-completion",
            ["SourcePartition", "Graph", "EdgeId", "Destination", "ExpectedRevision"], [0, 1, 2, 3, 4]),
        (typeof(GraphCrossPartitionReceiverState), "keyload.contract.graph-cross-partition-receiver-state",
            ["SourcePartition", "Graph", "EdgeId", "Destination", "Revision", "Deleted", "Fingerprint"],
            [1, 2, 3, 4, 5, 6, 7]),
        (typeof(ReadIncomingGraphEdgesRequest), "keyload.contract.graph-incoming-edges-request",
            ["Target", "Graph", "Limit"], [1, 2, 3]),
        (typeof(GraphIncomingEdgeRow), "keyload.contract.graph-incoming-edge-row",
            ["Edge", "DeliveredRevision"], [0, 1]),
        (typeof(GraphIncomingEdgesPage), "keyload.contract.graph-incoming-edges-page",
            ["Rows", "CutPosition", "Projection"], [1, 2, 3]),
        (typeof(GraphCrossPartitionCapacity), "keyload.contract.graph-cross-partition-capacity",
            ["Direction", "RecordCount", "EncodedBytes"], [1, 2, 3]),
        (typeof(GraphCrossPartitionFingerprint), "keyload.contract.graph-cross-partition-fingerprint",
            ["SourcePartition", "Graph", "EdgeId", "Destination", "Revision", "Deleted", "Edge"],
            [1, 2, 3, 4, 5, 6, 7])
    ];

    [Test]
    public async Task EveryGeneratedRecordAliasAndDeclaredFieldIdMatchesItsIndependentContract()
    {
        foreach (var contract in Contracts)
        {
            await AssertAliasAsync(contract.Type, contract.Alias);
            var properties = contract.Type.GetProperties(BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(property => property.GetCustomAttribute<global::Orleans.IdAttribute>() is not null)
                .OrderBy(property => property.GetCustomAttribute<global::Orleans.IdAttribute>()!.Id).ToArray();
            await Assert.That(properties.Select(property => property.Name).SequenceEqual(contract.Members)).IsTrue();
            var ids = properties.Select(property => property.GetCustomAttribute<global::Orleans.IdAttribute>()!.Id).ToArray();
            await Assert.That(ids.SequenceEqual(contract.Ids)).IsTrue();
            await Assert.That(ids.Distinct().Count()).IsEqualTo(ids.Length);
        }
    }

    [Test]
    public async Task CapacityDirectionAliasValuesAndFieldIdsRemainStable()
    {
        var type = typeof(GraphCrossPartitionCapacityDirection);
        await AssertAliasAsync(type, "keyload.contract.graph-cross-partition-capacity-direction");
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
        await Assert.That(fields.Select(field => field.Name).SequenceEqual(["PendingIntents", "ReceiverStates"])).IsTrue();
        await Assert.That(fields.All(field => field.GetCustomAttribute<global::Orleans.IdAttribute>() is null)).IsTrue();
        var values = Enum.GetValues(type).Cast<GraphCrossPartitionCapacityDirection>()
            .Select(value => (int)value).Order().ToArray();
        await Assert.That(values.SequenceEqual([0, 1])).IsTrue();
    }

    private static async Task AssertAliasAsync(Type type, string expected)
    {
        var alias = type.CustomAttributes.Single(attribute => attribute.AttributeType == typeof(global::Orleans.AliasAttribute))
            .ConstructorArguments.Single().Value as string;
        await Assert.That(alias).IsEqualTo(expected);
    }
}
