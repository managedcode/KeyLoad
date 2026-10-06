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
    private const string Kurrent = "KurrentDB";
    private const string Postgres = "PostgreSQL + pgvector";
    private const string Redis = "Redis";
    private const string Mongo = "MongoDB";
    private const string MongoPassword = "isolated-mongo-password";
    private const string Neo4j = "Neo4j";
    private const string OpenSearch = "OpenSearch";
    private const string Admin = "admin-key";
    private const string Neo4jPassword = "neo4j-password";
    private const string Neo4jNode = "neo4j";
    private const string Http = "http";

    internal static async Task VerifyAsync(DistributedApplication app, ComparisonWorkerSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection.Target == Kurrent && selection.Scenario == Scenario.StreamAppend)
        {
            await IsolatedKurrentCleanupRegression.VerifyAsync(app, selection.NodeCount, cancellationToken);
            await IsolatedKurrentOwnershipRegression.VerifyAsync(app, selection.NodeCount, cancellationToken);
            await IsolatedKurrentVolumeRegression.VerifyAsync(app, selection.NodeCount, cancellationToken);
        }
        if (selection.Scenario != Scenario.PointRead)
        {
            return;
        }
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources;
        switch (selection.Target)
        {
            case KeyLoad:
                await VerifyKeyLoadAsync(app, resources, selection.NodeCount, cancellationToken);
                break;
            case Postgres:
                await VerifyPostgresAsync(app, resources, selection.NodeCount, cancellationToken);
                break;
            case Redis:
                await RedisNativeReadinessRegression.VerifyAsync(app, selection.NodeCount, cancellationToken);
                await NativeCorpusPagingRegression.VerifyRedisAsync(app, selection.NodeCount, cancellationToken);
                break;
            case OpenSearch:
                await OpenSearchNativeVectorRegression.VerifyAsync(app, selection.NodeCount, cancellationToken);
                break;
            case Mongo:
                var mongoPassword = await ParameterAsync(resources, MongoPassword, cancellationToken);
                await MongoNativeAuthenticationRegression.VerifyAsync(app, selection.NodeCount, mongoPassword, cancellationToken);
                await MongoNativeReadinessRegression.VerifyAsync(app, selection.NodeCount, mongoPassword, cancellationToken);
                break;
            case Neo4j when selection.NodeCount == 1:
                var password = await ParameterAsync(resources, Neo4jPassword, cancellationToken);
                var endpoint = app.GetEndpoint(Neo4jNode, Http);
                await Neo4jHarnessRegression.VerifyAsync(endpoint.AbsoluteUri, password, IsolatedNeo4jResources.ImageReference, cancellationToken);
                await Neo4jHarnessMismatchRegression.VerifyAsync(endpoint, password, IsolatedNeo4jResources.ImageReference, cancellationToken);
                break;
        }
    }

    private static async Task VerifyKeyLoadAsync(DistributedApplication app, IEnumerable<IResource> resources,
        int nodeCount, CancellationToken token)
    {
        var admin = await ParameterAsync(resources, Admin, token);
        await KeyLoadStreamPublicRegression.VerifyAsync(app, admin, token, nodeCount);
        await IsolatedKeyLoadPublicRegression.VerifyAsync(app, nodeCount, token);
        await NativeCorpusPagingRegression.VerifyKeyLoadAsync(app, admin, nodeCount, token);
        if (nodeCount == 3)
        {
            await TimeSeries.NativeConfiguredTimeSeriesReadRegression.VerifyAsync(app, admin, token);
        }
        await IsolatedKeyLoadFaultRegression.VerifyAsync(app, nodeCount,
            IsolatedNativeReportAssertions.EvidenceDirectory(), token);
    }

    private static async Task VerifyPostgresAsync(DistributedApplication app, IEnumerable<IResource> resources,
        int nodeCount, CancellationToken token)
    {
        var database = resources.OfType<PostgresServerResource>().Single();
        var connection = await app.GetConnectionStringAsync(database.Name, token)
            ?? throw new InvalidOperationException("The native PostgreSQL connection is missing.");
        await IsolatedPostgresSlotRegression.VerifyAsync(connection, nodeCount, token);
        if (nodeCount == 1)
        {
            await PostgresSchemaRegression.VerifyAsync(connection, token);
        }
    }

    private static async Task<string> ParameterAsync(IEnumerable<IResource> resources, string name, CancellationToken token)
        => await resources.OfType<ParameterResource>().Single(item => item.Name == name).GetValueAsync(token)
            ?? throw new InvalidOperationException("The native regression credential is missing.");
}
