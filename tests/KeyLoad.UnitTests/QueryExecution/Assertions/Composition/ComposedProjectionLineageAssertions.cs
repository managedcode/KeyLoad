using System.Text.Json;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ComposedProjectionLineageAssertions
{
    private const string MessageField = "message_id";
    private const string ReferenceField = "reference_id";
    private const string NameField = "customer_name";
    private const string ScoreField = "score";
    private const int Fields = 4;
    private const int Score = 7;

    internal static async Task PageAsync(QueryPage page, long position, bool masked, bool repaired)
    {
        await Assert.That(page.CutPosition).IsEqualTo(position);
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
            await Assert.That(row.Redacted).IsEqualTo(masked);
            await Assert.That(row.RedactedFields!.Value).IsEquivalentTo(
                masked ? new[] { "q.payload/to", "d.name" } : [], global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
            using var json = JsonDocument.Parse(row.Json);
            await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(Fields);
            await Assert.That(json.RootElement.GetProperty(MessageField).GetString()).IsEqualTo(item.Message);
            await Assert.That(json.RootElement.GetProperty(ReferenceField).GetString()).IsEqualTo(masked ? null : item.Target);
            await Assert.That(json.RootElement.GetProperty(NameField).GetString()).IsEqualTo(masked ? null : item.Name);
            await Assert.That(json.RootElement.GetProperty(ScoreField).GetInt32()).IsEqualTo(Score);
        }
    }
}
