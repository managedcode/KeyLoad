namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-SQLC-003P: same-instance native writers and aliased public DTO graphs retain every value.</summary>
internal sealed class McpNativePayloadOracleTests
{
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
}
