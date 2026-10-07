using System.Text.Json;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>Checks exact two-source metadata on every page returned by an RF3 SQL join caller.</summary>
internal static class RelationalSqlRf3JoinPageAssertions
{
    private const int SourceCount = 2;
    private const int LeftSourceIndex = 0;
    private const int RightSourceIndex = 1;
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string CustomerId = "customer_id";

    internal static async Task EquivalentAsync(QueryPage expected, QueryPage actual)
    {
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await VerifySourcesAsync(actual);
    }

    internal static async Task SameRowsAsync(QueryPage expected, QueryPage actual)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(expected.CutPosition);
        await SqlRf3Protocol.EqualAsync(expected with { CutPosition = actual.CutPosition }, actual);
        await VerifySourcesAsync(actual);
    }

    internal static async Task RevisionsAsync(QueryPage page, long expectedLeftRevision, long expectedRightRevision)
    {
        await VerifySourcesAsync(page);
        foreach (var row in page.Rows)
        {
            var sources = row.Sources!.Value;
            await Assert.That(sources[LeftSourceIndex].Revision).IsEqualTo(expectedLeftRevision);
            await Assert.That(sources[RightSourceIndex].Revision).IsEqualTo(expectedRightRevision);
        }
    }

    internal static async Task VerifySourcesAsync(QueryPage page)
    {
        foreach (var row in page.Rows)
        {
            await Assert.That(row.Sources.HasValue).IsTrue();
            var sources = row.Sources!.Value;
            await Assert.That(sources.Length).IsEqualTo(SourceCount);
            await Assert.That(sources[LeftSourceIndex].Alias).IsEqualTo(LeftAlias);
            await Assert.That(sources[LeftSourceIndex].EntityId).IsEqualTo(row.EntityId);
            await Assert.That(sources[LeftSourceIndex].Revision).IsEqualTo(row.Revision);
            await Assert.That(sources[RightSourceIndex].Alias).IsEqualTo(RightAlias);
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(sources[RightSourceIndex].EntityId)
                .IsEqualTo(json.RootElement.GetProperty(CustomerId).GetString());
            await Assert.That(sources[RightSourceIndex].Revision).IsGreaterThan(0);
        }
    }
}
