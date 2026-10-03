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
}
