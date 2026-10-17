namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-SQLC-003P: same-instance native writers and aliased public DTO graphs retain every value.</summary>
internal sealed class McpNativePayloadOracleTests
{
    private const string ExpectedPartitionKey = "mcp-partition";
    [Test]
    public async Task AcSqlc003PDocumentsCommitSameTypedInstanceHasArrayAndStreamByteParity()
    {
        var item = McpCanonicalTestData.Commands()[0];
        var decoded = await McpNativePayloadAssertions.DecodeCanonical(item);
        await McpNativePayloadAssertions.AssertNativeWritersAgree(item, decoded);
    }

    [Test]
    public async Task AcSqlc003PAliasedTraverseRequestPreservesFullTypedPayloadAndWriterParity()
    {
        var item = McpCanonicalTestData.Reads().Single(value =>
            value.Name == McpCatalogExpectations.GraphTraverse);
        var decoded = await McpNativePayloadAssertions.DecodeCanonical(item);
        await McpNativePayloadAssertions.AssertNativeWritersAgree(item, decoded);
    }

    [Test]
    public async Task AcPmap003BindAndReadPreserveTypedPublicAndNativeBodies()
    {
        foreach (var item in McpCanonicalTestData.Commands().Where(value =>
            value.Name == McpCatalogExpectations.AdminPartitionPlacementBind))
        {
            var decoded = await McpNativePayloadAssertions.DecodeCanonical(item);
            await McpNativePayloadAssertions.AssertNativeWritersAgree(item, decoded);
        }
        var read = McpCanonicalTestData.Reads().Single(value =>
            value.Name == McpCatalogExpectations.AdminPartitionPlacementRead);
        var readDecoded = await McpNativePayloadAssertions.DecodeCanonical(read);
        await McpNativePayloadAssertions.AssertNativeWritersAgree(read, readDecoded);
    }

    [Test]
    public async Task AcMcp001BothShortestPathRequestsPreserveTypedPublicAndNativeBodies()
    {
        foreach (var item in McpCanonicalTestData.Reads().Where(value =>
            value.Name is McpCatalogExpectations.GraphShortestPath or McpCatalogExpectations.QueryGraphPath))
        {
            var decoded = await McpNativePayloadAssertions.DecodeCanonical(item);
            await McpNativePayloadAssertions.AssertNativeWritersAgree(item, decoded);
        }
    }

    [Test]
    public async Task MultiLanePublicJsonDescriptorRetainsLiteralParentAndLeafNativePayloads()
    {
        var item = McpCanonicalTestData.Commands().Single(value =>
            value.Name == "keyload_messages_receive_across_lanes");
        var decoded = await McpNativePayloadAssertions.DecodeCanonical(item);
        await McpNativePayloadAssertions.AssertNativeWritersAgree(item, decoded);
        var request = (MultiLaneReceiveRequest)decoded;
        await Assert.That(request.RequestId).IsEqualTo(Guid.Parse("ee5cf37e-0e81-4a94-8725-021e30f7b721"));
        var leaf = await Assert.That(request.Requests).HasSingleItem();
        await Assert.That(leaf.RequestId).IsEqualTo(Guid.Parse("00000000-0000-0000-0000-000000000002"));
        await Assert.That(leaf.Lane).IsEqualTo(new QueueLaneRef(
            new("mcp-tenant", "mcp-database", "mcp-domain", ExpectedPartitionKey), "mcp-records"));
        await Assert.That(leaf.MaxMessages).IsEqualTo(1);
        await Assert.That(leaf.MaxBytes).IsEqualTo(1_048_576);
        await Assert.That(leaf.LeaseSeconds).IsEqualTo(30);
    }
}
