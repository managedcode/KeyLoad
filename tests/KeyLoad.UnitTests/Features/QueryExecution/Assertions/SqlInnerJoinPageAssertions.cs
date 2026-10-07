using System.Text.Json;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlInnerJoinPageAssertions
{
    private const string LeftAlias = "l";
    private const string RightAlias = "r";
    private const string LeftPrimaryKey = "order_id";
    private const string CustomerNameProjectionAlias = "customer_name";
    private const string TotalField = "total";

    internal static async Task AssertRowAsync(QueryRow row, string leftId, string name, int total,
        string rightId)
    {
        await Assert.That(row.EntityId).IsEqualTo(leftId);
        await Assert.That(row.Sources).IsNotNull();
        var sources = row.Sources!.Value;
        await Assert.That(sources.Length).IsEqualTo(2);
        await Assert.That(sources[0].Alias).IsEqualTo(LeftAlias);
        await Assert.That(sources[0].EntityId).IsEqualTo(leftId);
        await Assert.That(sources[0].Revision).IsEqualTo(1);
        await Assert.That(sources[1].Alias).IsEqualTo(RightAlias);
        await Assert.That(sources[1].EntityId).IsEqualTo(rightId);
        await Assert.That(sources[1].Revision).IsEqualTo(1);
        using var json = JsonDocument.Parse(row.Json);
        await Assert.That(json.RootElement.GetProperty(LeftPrimaryKey).GetString()).IsEqualTo(leftId);
        await Assert.That(json.RootElement.GetProperty(CustomerNameProjectionAlias).GetString()).IsEqualTo(name);
        await Assert.That(json.RootElement.GetProperty(TotalField).GetInt32()).IsEqualTo(total);
        await Assert.That(row.Redacted).IsFalse();
    }
}
