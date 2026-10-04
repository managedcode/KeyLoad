using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class Neo4jHarnessSchemaFixture : IAsyncDisposable
{
    private const string CleanupFailureMessage = "Neo4jHarnessFixtureCleanupFailed";
    private readonly Neo4jHarnessQueryClient client;
    private bool ownsConstraint;
    private bool ownsMarker;

    private Neo4jHarnessSchemaFixture(Uri endpoint, string password)
    {
        RunId = Guid.NewGuid().ToString("N");
        MarkerLabel = "HarnessMarker_" + Guid.NewGuid().ToString("N");
        TargetLabel = "Benchmark_" + RunId;
        ConstraintName = TargetLabel + "_id";
        MarkerId = "marker_" + RunId;
        client = new(endpoint, password);
    }

    public string RunId { get; }
    public string MarkerLabel { get; }
    public string TargetLabel { get; }
    public string ConstraintName { get; }
    public string MarkerId { get; }
    public string ConstraintAcknowledgementJson { get; private set; } = string.Empty;

    public static async Task<Neo4jHarnessSchemaFixture> CreateAsync(Uri endpoint, string password, CancellationToken cancellationToken)
    {
        var fixture = new Neo4jHarnessSchemaFixture(endpoint, password);
        var failures = new List<Exception>();
        await Neo4jHarnessFailureCollector.AttemptAsync(() => fixture.InitializeAsync(cancellationToken), failures);
        if (failures.Count > 0)
        {
            await Neo4jHarnessFailureCollector.AttemptAsync(() => fixture.DisposeAsync().AsTask(), failures);
            throw new AggregateException("Neo4jHarnessFixtureSetupFailed", failures);
        }

        return fixture;
    }

    public Task<Neo4jHarnessResponse> ProbeDuplicateConstraintAsync(CancellationToken cancellationToken) =>
        client.SendAsync($"CREATE CONSTRAINT {ConstraintName} FOR (n:{TargetLabel}) REQUIRE n.id IS UNIQUE", null, cancellationToken);

    public async Task<bool> MarkerExistsAsync(CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync($"MATCH (n:{MarkerLabel} {{id:$id}}) RETURN count(n)", new { id = MarkerId }, cancellationToken);
        RequireAccepted(response);
        Neo4jQueryResponse.ValidateErrors(response.Document.RootElement);
        return response.Document.RootElement.GetProperty(Neo4jHarnessConstants.DataProperty)
            .GetProperty(Neo4jHarnessConstants.ValuesProperty)[0][0].GetInt32() == 1;
    }

    public async Task<bool> ConstraintExistsAsync(CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync("SHOW CONSTRAINTS YIELD name WHERE name=$name RETURN name", new { name = ConstraintName }, cancellationToken);
        RequireAccepted(response);
        Neo4jQueryResponse.ValidateErrors(response.Document.RootElement);
        return response.Document.RootElement.GetProperty(Neo4jHarnessConstants.DataProperty)
            .GetProperty(Neo4jHarnessConstants.ValuesProperty).GetArrayLength() == 1;
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        try
        {
            if (ownsMarker)
            {
                await Neo4jHarnessFailureCollector.AttemptBoundedAsync(DeleteMarkerAsync, failures);
            }

            if (ownsConstraint)
            {
                await Neo4jHarnessFailureCollector.AttemptBoundedAsync(DropConstraintAsync, failures);
            }
        }
        finally
        {
            ownsMarker = false;
            ownsConstraint = false;
            await Neo4jHarnessFailureCollector.AttemptAsync(() =>
            {
                client.Dispose();
                return Task.CompletedTask;
            }, failures);
        }

        Neo4jHarnessFailureCollector.ThrowIfAny(CleanupFailureMessage, failures);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using (var constraint = await client.SendAsync(
            $"CREATE CONSTRAINT {ConstraintName} FOR (n:{MarkerLabel}) REQUIRE n.id IS UNIQUE", null, cancellationToken))
        {
            if (constraint.StatusCode != 202)
            {
                throw new ComparisonFailureException(Neo4jHarnessConstants.InvalidQueryResponse);
            }

            Neo4jQueryResponse.ValidateConstraintCreation(constraint.Document.RootElement);
            ConstraintAcknowledgementJson = constraint.Document.RootElement.GetRawText();
            ownsConstraint = true;
        }

        using var marker = await client.SendAsync($"CREATE (n:{MarkerLabel} {{id:$id}}) RETURN n.id", new { id = MarkerId }, cancellationToken);
        if (marker.StatusCode != 202)
        {
            throw new ComparisonFailureException(Neo4jHarnessConstants.InvalidQueryResponse);
        }

        Neo4jQueryResponse.ValidateErrors(marker.Document.RootElement);
        ownsMarker = true;
    }

    private async Task DeleteMarkerAsync(CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(Neo4jHarnessStatements.DeleteMarker(MarkerLabel), new { id = MarkerId }, cancellationToken);
        RequireAccepted(response);
        Neo4jQueryResponse.ValidateErrors(response.Document.RootElement);
    }

    private async Task DropConstraintAsync(CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(Neo4jHarnessStatements.DropConstraint(ConstraintName), null, cancellationToken);
        RequireAccepted(response);
        Neo4jQueryResponse.ValidateConstraintCreation(response.Document.RootElement);
    }

    private static void RequireAccepted(Neo4jHarnessResponse response)
    {
        if (response.StatusCode != 202)
        {
            throw new ComparisonFailureException(Neo4jHarnessConstants.InvalidQueryResponse);
        }
    }
}
