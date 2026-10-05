using System.Reflection;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphCrossPartitionNativeMetadataTests
{
    private static readonly (Type Type, string Alias, string[] Members, uint[] Ids)[] Contracts =
    [
        (typeof(GraphEdgeOwnerVersionV1), "keyload.contract.graph-edge-owner-version.v1",
            ["Version", "Revision", "Deleted"], [0, 1, 2]),
        (typeof(GraphCrossPartitionDeliveryIntentV1), "keyload.contract.graph-cross-partition-delivery-intent.v1",
            ["Version", "SourcePartition", "Graph", "EdgeId", "Destination", "Revision", "Deleted", "Edge",
                "OriginalPrincipalId", "OriginalPolicyEpoch", "Fingerprint"], [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10]),
        (typeof(ApplyCrossPartitionReverseEdge), "keyload.contract.apply-cross-partition-reverse-edge.v1",
            ["SourcePartition", "Graph", "EdgeId", "Destination", "ExpectedRevision"], [0, 1, 2, 3, 4]),
        (typeof(CompleteCrossPartitionReverseEdge), "keyload.contract.graph-reverse-edge-completion.v1",
            ["SourcePartition", "Graph", "EdgeId", "Destination", "ExpectedRevision"], [0, 1, 2, 3, 4]),
        (typeof(GraphCrossPartitionReceiverStateV1), "keyload.contract.graph-cross-partition-receiver-state.v1",
            ["Version", "SourcePartition", "Graph", "EdgeId", "Destination", "Revision", "Deleted", "Fingerprint"],
            [0, 1, 2, 3, 4, 5, 6, 7]),
        (typeof(ReadIncomingGraphEdgesRequestV1), "keyload.contract.graph-incoming-edges-request.v1",
            ["Version", "Target", "Graph", "Limit"], [0, 1, 2, 3]),
        (typeof(GraphIncomingEdgeRowV1), "keyload.contract.graph-incoming-edge-row.v1",
            ["Edge", "DeliveredRevision"], [0, 1]),
        (typeof(GraphIncomingEdgesPageV1), "keyload.contract.graph-incoming-edges-page.v1",
            ["Version", "Rows", "CutPosition", "Projection"], [0, 1, 2, 3]),
        (typeof(GraphCrossPartitionCapacityV1), "keyload.contract.graph-cross-partition-capacity.v1",
            ["Version", "Direction", "RecordCount", "EncodedBytes"], [0, 1, 2, 3]),
        (typeof(GraphCrossPartitionFingerprintV1), "keyload.contract.graph-cross-partition-fingerprint.v1",
            ["Version", "SourcePartition", "Graph", "EdgeId", "Destination", "Revision", "Deleted", "Edge"],
            [0, 1, 2, 3, 4, 5, 6, 7])
    ];

    [Test]
    public async Task EveryGeneratedRecordAliasAndDeclaredFieldIdMatchesItsIndependentContract()
    {
        foreach (var contract in Contracts)
        {
            await AssertAliasAsync(contract.Type, contract.Alias);
            var properties = contract.Type.GetProperties(BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(property => property.GetCustomAttribute<Orleans.IdAttribute>() is not null)
                .OrderBy(property => property.GetCustomAttribute<Orleans.IdAttribute>()!.Id).ToArray();
            await Assert.That(properties.Select(property => property.Name).SequenceEqual(contract.Members)).IsTrue();
            var ids = properties.Select(property => property.GetCustomAttribute<Orleans.IdAttribute>()!.Id).ToArray();
            await Assert.That(ids.SequenceEqual(contract.Ids)).IsTrue();
            await Assert.That(ids.Distinct().Count()).IsEqualTo(ids.Length);
        }
    }

    [Test]
    public async Task CapacityDirectionAliasValuesAndFieldIdsRemainStable()
    {
        var type = typeof(GraphCrossPartitionCapacityDirection);
        await AssertAliasAsync(type, "keyload.contract.graph-cross-partition-capacity-direction.v1");
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.GetCustomAttribute<Orleans.IdAttribute>() is not null)
            .OrderBy(field => field.GetCustomAttribute<Orleans.IdAttribute>()!.Id).ToArray();
        await Assert.That(fields.Select(field => field.Name).SequenceEqual(["PendingIntents", "ReceiverStates"])).IsTrue();
        await Assert.That(fields.Select(field => field.GetCustomAttribute<Orleans.IdAttribute>()!.Id)
            .SequenceEqual([0U, 1U])).IsTrue();
        await Assert.That((int)GraphCrossPartitionCapacityDirection.PendingIntents).IsEqualTo(0);
        await Assert.That((int)GraphCrossPartitionCapacityDirection.ReceiverStates).IsEqualTo(1);
    }

    private static async Task AssertAliasAsync(Type type, string expected)
    {
        var alias = type.CustomAttributes.Single(attribute => attribute.AttributeType == typeof(Orleans.AliasAttribute))
            .ConstructorArguments.Single().Value as string;
        await Assert.That(alias).IsEqualTo(expected);
    }
}
