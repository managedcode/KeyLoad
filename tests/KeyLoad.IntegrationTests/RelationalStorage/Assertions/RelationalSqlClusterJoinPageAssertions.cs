using System.Text.Json;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>Checks exact two-source metadata on every page returned by an Orleans SQL join caller.</summary>
internal static class RelationalSqlClusterJoinPageAssertions
{
    private const int SourceCount = 2;
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string CustomerId = "customer_id";

    internal static async Task EquivalentAsync(QueryPage expected, QueryPage actual)
    {
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await VerifySourcesAsync(actual);
    }

    internal static async Task SameRowsAsync(QueryPage expected, QueryPage actual)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(expected.CutPosition);
        await SqlClusterProtocol.EqualAsync(expected with
        {
            CutPosition = actual.CutPosition
        }, actual);
        await VerifySourcesAsync(actual);
    }

    internal static async Task RevisionsAsync(QueryPage page, long expectedLeftRevision, long expectedRightRevision)
    {
        await VerifySourcesAsync(page);
        foreach (var row in page.Rows)
        {
            var sources = row.Sources!.Value;
            await Assert.That(sources[0].Revision).IsEqualTo(expectedLeftRevision);
            await Assert.That(sources[1].Revision).IsEqualTo(expectedRightRevision);
        }
    }

    internal static async Task VerifySourcesAsync(QueryPage page)
    {
        foreach (var row in page.Rows)
        {
            await Assert.That(row.Sources.HasValue).IsTrue();
            var sources = row.Sources!.Value;
            await Assert.That(sources.Length).IsEqualTo(SourceCount);
            await Assert.That(sources[0].Alias).IsEqualTo(LeftAlias);
            await Assert.That(sources[0].EntityId).IsEqualTo(row.EntityId);
            await Assert.That(sources[0].Revision).IsEqualTo(row.Revision);
            await Assert.That(sources[1].Alias).IsEqualTo(RightAlias);
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(sources[1].EntityId)
                .IsEqualTo(json.RootElement.GetProperty(CustomerId).GetString());
            await Assert.That(sources[1].Revision).IsGreaterThan(0);
        }
    }
}
