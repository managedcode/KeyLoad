using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class Neo4jHarnessStoredWrites
{
    public static async Task VerifyBeforeDisposalAsync(Neo4jHarnessQueryClient client, string label, BenchmarkDataset dataset,
        CancellationToken cancellationToken)
    {
        var expected = ExpectedWriteDocuments(dataset);
        using var response = await client.SendAsync(
            $"MATCH (n:{label}) WHERE n.id IN $ids RETURN n.id,n.json ORDER BY n.id",
            new { ids = expected.Select(document => document.Id).ToArray() }, cancellationToken);
        RequireAccepted(response);
        Neo4jQueryResponse.ValidateErrors(response.Document.RootElement);
        var actual = ReadDocuments(response.Document.RootElement);
        await Assert.That(expected.Length == Neo4jHarnessConstants.WriteDocumentCount
            && actual.Count == Neo4jHarnessConstants.WriteDocumentCount).IsTrue();
        await Assert.That(expected.All(document => actual.TryGetValue(document.Id, out var json) && json == document.Json)).IsTrue();
        await Assert.That(await CountLabelAsync(client, label, cancellationToken) == Neo4jHarnessConstants.TotalNeo4jDocumentCount).IsTrue();
    }

    public static async Task VerifyRemovedAfterDisposalAsync(Neo4jHarnessQueryClient client, string label, string constraintName,
        CancellationToken cancellationToken)
    {
        await Assert.That(await CountLabelAsync(client, label, cancellationToken) == 0).IsTrue();
        using var response = await client.SendAsync(Neo4jHarnessStatements.FindConstraint(),
            new { name = constraintName }, cancellationToken);
        RequireAccepted(response);
        Neo4jQueryResponse.ValidateErrors(response.Document.RootElement);
        await Assert.That(response.Document.RootElement.GetProperty(Neo4jHarnessConstants.DataProperty)
            .GetProperty(Neo4jHarnessConstants.ValuesProperty).GetArrayLength() == 0).IsTrue();
    }

    private static BenchmarkDocument[] ExpectedWriteDocuments(BenchmarkDataset dataset) =>
        Enumerable.Range(0, Neo4jHarnessConstants.SmallRepetitions)
            .SelectMany(repetition => Enumerable.Range(0, dataset.Options.Warmup)
                .Select(operation => dataset.Input(Scenario.DocumentWrite, repetition, operation, true))
                .Concat(Enumerable.Range(0, dataset.Options.Operations)
                    .Select(operation => dataset.Input(Scenario.DocumentWrite, repetition, operation, false))))
            .ToArray();

    private static Dictionary<string, string> ReadDocuments(JsonElement root)
    {
        var rows = root.GetProperty(Neo4jHarnessConstants.DataProperty)
            .GetProperty(Neo4jHarnessConstants.ValuesProperty);
        return rows.EnumerateArray().ToDictionary(row => row[0].GetString()!, row => row[1].GetString()!, StringComparer.Ordinal);
    }

    private static async Task<int> CountLabelAsync(Neo4jHarnessQueryClient client, string label, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(Neo4jHarnessStatements.CountNodes(label), null, cancellationToken);
        RequireAccepted(response);
        Neo4jQueryResponse.ValidateErrors(response.Document.RootElement);
        return response.Document.RootElement.GetProperty(Neo4jHarnessConstants.DataProperty)
            .GetProperty(Neo4jHarnessConstants.ValuesProperty)[0][0].GetInt32();
    }

    private static void RequireAccepted(Neo4jHarnessResponse response)
    {
        if (response.StatusCode != 202)
        {
            throw new ComparisonFailureException("Neo4j:InvalidQueryResponse");
        }
    }
}
