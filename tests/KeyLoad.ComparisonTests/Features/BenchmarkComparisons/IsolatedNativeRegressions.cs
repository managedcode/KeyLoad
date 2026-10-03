using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeRegressions
{
    private const string KeyLoad = "KeyLoad";
    private const string Postgres = "PostgreSQL + pgvector";
    private const string Neo4j = "Neo4j";
    private const string Admin = "admin-key";
    private const string Neo4jPassword = "neo4j-password";
    private const string Neo4jNode = "neo4j";
    private const string Http = "http";

    internal static async Task VerifyAsync(DistributedApplication app, ComparisonWorkerSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection.Scenario != Scenario.PointRead)
        {
            return;
        }
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources;
        if (selection.Target == KeyLoad)
        {
            var admin = await ParameterAsync(resources, Admin, cancellationToken);
            await KeyLoadStreamPublicRegression.VerifyAsync(app, admin, cancellationToken, selection.NodeCount);
            await IsolatedKeyLoadPublicRegression.VerifyAsync(app, selection.NodeCount, cancellationToken);
            await IsolatedKeyLoadFaultRegression.VerifyAsync(app, selection.NodeCount,
                IsolatedNativeReportAssertions.EvidenceDirectory(), cancellationToken);
        }
        else if (selection.Target == Postgres && selection.NodeCount == 1)
        {
            var database = resources.OfType<PostgresServerResource>().Single();
            var connection = await app.GetConnectionStringAsync(database.Name, cancellationToken)
                ?? throw new InvalidOperationException("The native PostgreSQL connection is missing.");
            await PostgresSchemaRegression.VerifyAsync(connection, cancellationToken);
        }
        else if (selection.Target == Neo4j && selection.NodeCount == 1)
        {
            var password = await ParameterAsync(resources, Neo4jPassword, cancellationToken);
            var endpoint = app.GetEndpoint(Neo4jNode, Http);
            await Neo4jHarnessRegression.VerifyAsync(endpoint.AbsoluteUri, password, IsolatedNeo4jResources.ImageReference, cancellationToken);
            await Neo4jHarnessMismatchRegression.VerifyAsync(endpoint, password, IsolatedNeo4jResources.ImageReference, cancellationToken);
        }
    }

    private static async Task<string> ParameterAsync(IEnumerable<IResource> resources, string name, CancellationToken token)
        => await resources.OfType<ParameterResource>().Single(item => item.Name == name).GetValueAsync(token)
            ?? throw new InvalidOperationException("The native regression credential is missing.");
}
