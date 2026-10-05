using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCPGW-002: the published gateway factory searches canonical operation metadata.</summary>
internal sealed class McpGatewayGraphCatalogTests
{
    private const string QueryPartitionName = "keyload_query_partitions";
    private const string SqlCategory = "sql";
    private const string OperationAlias = "keyload query partitions";

    [Test]
    public async Task ExactOperationSearchReturnsTheCanonicalToolAndSchema()
    {
        await using var host = McpGatewayCatalogTestHost.Create();
        await host.Owner.InitializeAsync(CancellationToken.None);

        var result = await host.Owner.SearchAsync(QueryPartitionName, 3, CancellationToken.None);
        var match = result.Matches.Single(item => item.ToolId == QueryPartitionName);
        var operation = McpOperationCatalog.Entries.Single(item => item.Name == QueryPartitionName);

        await Assert.That(result.Diagnostics.Count).IsEqualTo(0);
        await Assert.That(match.ToolName).IsEqualTo(operation.Name);
        await Assert.That(match.Description).IsEqualTo(operation.Description);
        await Assert.That(match.InputSchema!.Value.GetRawText()).IsEqualTo(operation.InputSchema.GetRawText());
        await Assert.That(match.Categories).Contains(SqlCategory);
    }

    [Test]
    public async Task GraphSearchStaysWithinRequestedLimitAndCanonicalInventory()
    {
        await using var host = McpGatewayCatalogTestHost.Create();
        await host.Owner.InitializeAsync(CancellationToken.None);

        var result = await host.Owner.SearchAsync("partitioned sql query", 4, CancellationToken.None);
        var knownNames = McpOperationCatalog.Entries.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);

        await Assert.That(result.Matches.Count).IsLessThanOrEqualTo(4);
        await Assert.That(result.Matches.Count).IsGreaterThan(0);
        await Assert.That(result.Matches.All(item => knownNames.Contains(item.ToolId))).IsTrue();
    }

    [Test]
    public async Task RouteUsesFiniteFeatureAndOperationAliases()
    {
        var entries = McpGatewayCatalogValidation.CreateEntries(McpOperationCatalog.Entries);
        var query = entries.Single(item => item.Operation.Name == QueryPartitionName);
        await Assert.That(query.SearchHints.Categories).Contains(SqlCategory);
        await Assert.That(query.SearchHints.Aliases).Contains(OperationAlias);
        await Assert.That(query.SearchHints.Aliases!.Count).IsLessThanOrEqualTo(4);

        await using var host = McpGatewayCatalogTestHost.Create();
        await host.Owner.InitializeAsync(CancellationToken.None);
        var routed = await host.Owner.RouteAsync("route a partitioned query", 2, 2, true, CancellationToken.None);
        var canonicalNames = McpOperationCatalog.Entries.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        await Assert.That(routed.SuggestedMatches.Count).IsLessThanOrEqualTo(4);
        await Assert.That(routed.SuggestedMatches.All(item => canonicalNames.Contains(item.ToolId))).IsTrue();
        await Assert.That(routed.Diagnostics.Count).IsEqualTo(0);
    }
}
