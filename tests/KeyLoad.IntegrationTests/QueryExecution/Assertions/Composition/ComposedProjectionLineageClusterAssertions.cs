using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ComposedProjectionLineageClusterAssertions
{
    private const string MessageField = "message_id";
    private const string ReferenceField = "reference_id";
    private const string NameField = "customer_name";
    private const string ScoreField = "score";
    private const int Fields = 4;
    private const int Score = 7;

    internal static async Task PageAsync(QueryPage page, bool masked, bool repaired, bool scoreMasked = false)
    {
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(0L);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo("bounded-queue-entity-reference-inner-join");
        var expected = new List<(string Message, string Target, string Name)> { ("a", "c1", "Ada") };
        if (repaired)
        {
            expected.Add(("b", "m", "Max"));
        }
        await Assert.That(page.Rows.Length).IsEqualTo(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var row = page.Rows[index];
            var item = expected[index];
            await Assert.That(row.EntityId).IsEqualTo(item.Message);
            await Assert.That(row.Revision).IsEqualTo(1L);
            await Assert.That(row.Sources.HasValue).IsTrue();
            await Assert.That(row.RedactedFields.HasValue).IsTrue();
            await Assert.That(row.Sources!.Value).IsEquivalentTo(
                new QueryRowSource[] { new("q", item.Message, 1), new("d", item.Target, 1) }, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
            var redactedFields = new List<string>();
            if (masked)
            {
                redactedFields.Add("q.payload/to");
                redactedFields.Add("d.name");
            }
            if (scoreMasked)
            {
                redactedFields.Add("q.payload/score");
            }
            await Assert.That(row.Redacted).IsEqualTo(masked || scoreMasked);
            await Assert.That(row.RedactedFields!.Value).IsEquivalentTo(
                redactedFields, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(Fields);
            await Assert.That(json.RootElement.GetProperty(MessageField).GetString()).IsEqualTo(item.Message);
            await Assert.That(json.RootElement.GetProperty(ReferenceField).GetString()).IsEqualTo(masked ? null : item.Target);
            await Assert.That(json.RootElement.GetProperty(NameField).GetString()).IsEqualTo(masked ? null : item.Name);
            if (scoreMasked)
            {
                await Assert.That(json.RootElement.GetProperty(ScoreField).ValueKind).IsEqualTo(JsonValueKind.Null);
            }
            else
            {
                await Assert.That(json.RootElement.GetProperty(ScoreField).GetInt32()).IsEqualTo(Score);
            }
        }
    }
}
