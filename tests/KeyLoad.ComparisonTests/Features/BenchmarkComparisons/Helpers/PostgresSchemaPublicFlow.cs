using System.Globalization;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Npgsql;
using TUnit.Assertions.Enums;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class PostgresSchemaPublicFlow
{
    private const string TextType = "text";
    private const string JsonbType = "jsonb";
    private const string UuidType = "uuid";
    private const string BigintType = "bigint";
    internal static async Task VerifyAsync(string connectionString, CancellationToken cancellationToken)
    {
        foreach (var dimensions in new[] { 2, 1_024 })
        {
            var options = PostgresSchemaSupport.Options(dimensions);
            var dataset = new BenchmarkDataset(options);
            await VerifyTargetAsync(connectionString, dimensions, dataset, cancellationToken);
        }
    }

    private static async Task VerifyTargetAsync(string connectionString, int dimensions, BenchmarkDataset dataset,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("D");
        var target = new PostgresTarget(connectionString, runId, "comparison-test-image");
        try
        {
            await target.InitializeAsync(dataset, cancellationToken);
            await VerifyMetadataAsync(connectionString, PostgresSchemaSupport.Schema(runId), dimensions, cancellationToken);
            await using var session = await target.OpenSessionAsync(cancellationToken);
            await VerifyOperationsAsync(session, dataset, cancellationToken);
        }
        finally
        {
            await target.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        }
    }

    private static async Task VerifyMetadataAsync(string connectionString, string schema, int dimensions,
        CancellationToken cancellationToken)
    {
        await using var connection = await PostgresSchemaSupport.OpenAsync(connectionString, cancellationToken);
        await VerifyDocumentMetadataAsync(connection, schema, dimensions, cancellationToken);
        await VerifyQueueMetadataAsync(connection, schema, cancellationToken);
        await VerifyGraphMetadataAsync(connection, schema, cancellationToken);
        await VerifyEventMetadataAsync(connection, schema, cancellationToken);
        await Assert.That(await PostgresSchemaSupport.VectorExtensionExistsAsync(connection, cancellationToken)).IsTrue();
    }

    private static async Task VerifyDocumentMetadataAsync(NpgsqlConnection connection, string schema, int dimensions,
        CancellationToken cancellationToken)
    {
        var columns = await PostgresSchemaSupport.ColumnsAsync(connection, schema, PostgresSchemaSupport.DocumentsTable, cancellationToken);
        await Assert.That(columns[PostgresSchemaSupport.IdColumn].Type).IsEqualTo(TextType);
        await Assert.That(columns[PostgresSchemaSupport.IdColumn].NotNull).IsTrue();
        await Assert.That(columns[PostgresSchemaSupport.BodyColumn].Type).IsEqualTo(JsonbType);
        await Assert.That(columns[PostgresSchemaSupport.BodyColumn].NotNull).IsTrue();
        await Assert.That(columns[PostgresSchemaSupport.EmbeddingColumn].Type)
            .IsEqualTo("vector(" + dimensions.ToString(CultureInfo.InvariantCulture) + ")");
        await Assert.That(columns[PostgresSchemaSupport.EmbeddingColumn].NotNull).IsFalse();
        await Assert.That(columns.Count).IsEqualTo(3);
        await Assert.That(await PostgresSchemaSupport.PrimaryKeyAsync(connection, schema, PostgresSchemaSupport.DocumentsTable, cancellationToken))
            .IsEquivalentTo(new[] { PostgresSchemaSupport.IdColumn }, CollectionOrdering.Matching);
        await AssertKeyCollationAsync(connection, schema, PostgresSchemaSupport.DocumentsTable,
            PostgresSchemaSupport.IdColumn, cancellationToken);
    }

    private static async Task VerifyQueueMetadataAsync(NpgsqlConnection connection, string schema,
        CancellationToken cancellationToken)
    {
        var queue = await PostgresSchemaSupport.ColumnsAsync(connection, schema, PostgresSchemaSupport.QueueTable, cancellationToken);
        await Assert.That(queue.Count).IsEqualTo(6);
        await Assert.That(queue[PostgresSchemaSupport.IdColumn].Type).IsEqualTo(TextType);
        await Assert.That(queue[PostgresSchemaSupport.BodyColumn].Type).IsEqualTo(JsonbType);
        await Assert.That(queue[PostgresSchemaSupport.BodyColumn].NotNull).IsTrue();
        await Assert.That(queue[PostgresSchemaSupport.StateColumn].Type).IsEqualTo(TextType);
        await Assert.That(queue[PostgresSchemaSupport.StateColumn].Default).IsEqualTo("'ready'::text");
        await Assert.That(queue[PostgresSchemaSupport.StateColumn].NotNull).IsTrue();
        await Assert.That(queue[PostgresSchemaSupport.AttemptsColumn].Default).IsEqualTo("0");
        await Assert.That(queue[PostgresSchemaSupport.AttemptsColumn].NotNull).IsTrue();
        await Assert.That(queue[PostgresSchemaSupport.LeaseOwnerColumn].Type).IsEqualTo(UuidType);
        await Assert.That(queue[PostgresSchemaSupport.LeaseUntilColumn].Type).IsEqualTo("timestamp with time zone");
        await Assert.That(queue[PostgresSchemaSupport.AttemptsColumn].Type).IsEqualTo("integer");
        await Assert.That(await PostgresSchemaSupport.PrimaryKeyAsync(connection, schema, PostgresSchemaSupport.QueueTable, cancellationToken))
            .IsEquivalentTo(new[] { PostgresSchemaSupport.IdColumn }, CollectionOrdering.Matching);
        await AssertKeyCollationAsync(connection, schema, PostgresSchemaSupport.QueueTable,
            PostgresSchemaSupport.IdColumn, cancellationToken);
        await Assert.That(await PostgresSchemaSupport.IndexDefinitionAsync(connection, schema, PostgresSchemaSupport.QueueReadyIndex, cancellationToken))
            .Contains("(state, id)");
    }

    private static async Task VerifyGraphMetadataAsync(NpgsqlConnection connection, string schema,
        CancellationToken cancellationToken)
    {
        var edges = await PostgresSchemaSupport.ColumnsAsync(connection, schema, PostgresSchemaSupport.EdgesTable, cancellationToken);
        await Assert.That(edges.Count).IsEqualTo(2);
        await Assert.That(edges[PostgresSchemaSupport.SourceColumn].Type).IsEqualTo(TextType);
        await Assert.That(edges[PostgresSchemaSupport.SourceColumn].NotNull).IsTrue();
        await Assert.That(edges[PostgresSchemaSupport.TargetColumn].Type).IsEqualTo(TextType);
        await Assert.That(edges[PostgresSchemaSupport.TargetColumn].NotNull).IsTrue();
        await Assert.That(await PostgresSchemaSupport.PrimaryKeyAsync(connection, schema, PostgresSchemaSupport.EdgesTable, cancellationToken))
            .IsEquivalentTo(new[] { PostgresSchemaSupport.SourceColumn, PostgresSchemaSupport.TargetColumn }, CollectionOrdering.Matching);
        await AssertKeyCollationAsync(connection, schema, PostgresSchemaSupport.EdgesTable,
            PostgresSchemaSupport.SourceColumn, cancellationToken);
        await AssertKeyCollationAsync(connection, schema, PostgresSchemaSupport.EdgesTable,
            PostgresSchemaSupport.TargetColumn, cancellationToken);
    }

    private static async Task VerifyEventMetadataAsync(NpgsqlConnection connection, string schema,
        CancellationToken cancellationToken)
    {
        var events = await PostgresSchemaSupport.ColumnsAsync(connection, schema, PostgresSchemaSupport.EventsTable, cancellationToken);
        await Assert.That(events.Count).IsEqualTo(4);
        await Assert.That(events[PostgresSchemaSupport.StreamIdColumn].Type).IsEqualTo(TextType);
        await Assert.That(events[PostgresSchemaSupport.StreamIdColumn].NotNull).IsTrue();
        await Assert.That(events[PostgresSchemaSupport.EventIdColumn].Type).IsEqualTo(UuidType);
        await Assert.That(events[PostgresSchemaSupport.EventIdColumn].NotNull).IsTrue();
        await Assert.That(events[PostgresSchemaSupport.RevisionColumn].Type).IsEqualTo(BigintType);
        await Assert.That(events[PostgresSchemaSupport.RevisionColumn].NotNull).IsTrue();
        await Assert.That(events[PostgresSchemaSupport.BodyColumn].Type).IsEqualTo(JsonbType);
        await Assert.That(events[PostgresSchemaSupport.BodyColumn].NotNull).IsTrue();
        await Assert.That(await PostgresSchemaSupport.PrimaryKeyAsync(connection, schema, PostgresSchemaSupport.EventsTable, cancellationToken))
            .IsEquivalentTo(new[] { PostgresSchemaSupport.StreamIdColumn }, CollectionOrdering.Matching);
        await AssertKeyCollationAsync(connection, schema, PostgresSchemaSupport.EventsTable,
            PostgresSchemaSupport.StreamIdColumn, cancellationToken);
        await Assert.That(await PostgresSchemaSupport.EventRevisionConstraintExistsAsync(connection, schema, cancellationToken)).IsTrue();
    }

    private static async Task AssertKeyCollationAsync(NpgsqlConnection connection, string schema, string table,
        string column, CancellationToken cancellationToken)
    {
        var collation = await PostgresSchemaSupport.CollationAsync(connection, schema, table, column, cancellationToken);
        await Assert.That(collation).IsEqualTo(PostgresSchemaSupport.ExpectedKeyCollation);
    }

    private static async Task VerifyOperationsAsync(IComparisonSession session, BenchmarkDataset dataset,
        CancellationToken cancellationToken)
    {
        var document = dataset.Documents[0];
        await Assert.That(BenchmarkDataset.SameDocument(await session.ReadAsync(document, cancellationToken), document)).IsTrue();
        var vector = await session.ExecuteAsync(Scenario.VectorExact, document, cancellationToken);
        await Assert.That(vector.Neighbors!.Value.Select(item => item.Id))
            .IsEquivalentTo(dataset.ExactNeighbors(document).Select(item => item.Id), CollectionOrdering.Matching);
        var neighbors = await session.ExecuteAsync(Scenario.GraphNeighbors, document, cancellationToken);
        await Assert.That(neighbors.Vertices!.Value).IsEquivalentTo(dataset.Edges
            .Where(edge => edge.From == document.Id).Select(edge => edge.To).Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        var traversal = await session.ExecuteAsync(Scenario.GraphTraverse, document, cancellationToken);
        await Assert.That(traversal.Vertices!.Value).IsEquivalentTo(dataset.Reachable(document, dataset.Options.GraphDepth), CollectionOrdering.Matching);
        var queued = dataset.CreateDocument(dataset.Options.Documents + 1);
        var queue = await session.ExecuteAsync(Scenario.QueueCycle, queued, cancellationToken);
        await Assert.That(BenchmarkDataset.SameJson(queue.Message!.Json, queued.Json)).IsTrue();
        var eventInput = dataset.CreateDocument(dataset.Options.Documents + 2);
        await session.ExecuteAsync(Scenario.StreamAppend, eventInput, cancellationToken);
        await Assert.That(BenchmarkDataset.SameEvent(await session.ReadEventAsync(eventInput, cancellationToken), eventInput)).IsTrue();
        await PostgresStreamPublicRegression.VerifyAsync(session, dataset, cancellationToken);
        var written = dataset.CreateDocument(dataset.Options.Documents + 3);
        await session.ExecuteAsync(Scenario.DocumentWrite, written, cancellationToken);
        await Assert.That(BenchmarkDataset.SameDocument(await session.ReadAsync(written, cancellationToken), written)).IsTrue();
    }
}
