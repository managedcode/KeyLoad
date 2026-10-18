using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeCheckss
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

    internal static async Task VerifyAsync(DistributedApplication app, BenchmarkSessionSelection selection, string evidence,
        CancellationToken cancellationToken)
    {
        if (selection.Target == Kurrent)
        {
            await IsolatedKurrentCleanupChecks.VerifyAsync(app, selection.NodeCount, cancellationToken);
            await IsolatedKurrentOwnershipChecks.VerifyAsync(app, selection.NodeCount, cancellationToken);
            await IsolatedKurrentVolumeChecks.VerifyAsync(app, selection.NodeCount, cancellationToken);
        }
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources;
        switch (selection.Target)
        {
            case KeyLoad:
                await VerifyKeyLoadAsync(app, resources, selection.NodeCount, evidence, cancellationToken);
                break;
            case Postgres:
                await VerifyPostgresAsync(app, resources, selection.NodeCount, cancellationToken);
                break;
            case Redis:
                await RedisNativeReadinessChecks.VerifyAsync(app, selection.NodeCount, cancellationToken);
                await NativeCorpusPagingChecks.VerifyRedisAsync(app, selection.NodeCount, cancellationToken);
                break;
            case OpenSearch:
                await OpenSearchNativeVectorChecks.VerifyAsync(app, selection.NodeCount, cancellationToken);
                break;
            case Mongo:
                var mongoPassword = await ParameterAsync(resources, MongoPassword, cancellationToken);
                await MongoNativeAuthenticationChecks.VerifyAsync(app, selection.NodeCount, mongoPassword, cancellationToken);
                await MongoNativeReadinessChecks.VerifyAsync(app, selection.NodeCount, mongoPassword, cancellationToken);
                break;
            case Neo4j when selection.NodeCount == 1:
                var password = await ParameterAsync(resources, Neo4jPassword, cancellationToken);
                var endpoint = app.GetEndpoint(Neo4jNode, Http);
                await Neo4jHarnessChecks.VerifyAsync(endpoint.AbsoluteUri, password, IsolatedNeo4jResources.ImageReference, cancellationToken);
                await Neo4jHarnessMismatchChecks.VerifyAsync(endpoint, password, IsolatedNeo4jResources.ImageReference, cancellationToken);
                break;
        }
    }

    private static async Task VerifyKeyLoadAsync(DistributedApplication app, IEnumerable<IResource> resources,
        int nodeCount, string evidence, CancellationToken token)
    {
        var admin = await ParameterAsync(resources, Admin, token);
        await KeyLoadStreamPublicChecks.VerifyAsync(app, admin, token, nodeCount);
        await IsolatedKeyLoadPublicChecks.VerifyAsync(app, nodeCount, token);
        await NativeCorpusPagingChecks.VerifyKeyLoadAsync(app, admin, nodeCount, token);
        if (nodeCount == 3)
        {
            await TimeSeries.NativeConfiguredTimeSeriesReadChecks.VerifyAsync(app, admin, token);
        }
        await IsolatedKeyLoadFaultChecks.VerifyAsync(app, nodeCount,
            evidence, token);
    }

    private static async Task VerifyPostgresAsync(DistributedApplication app, IEnumerable<IResource> resources,
        int nodeCount, CancellationToken token)
    {
        var database = resources.OfType<PostgresServerResource>().Single();
        var connection = await app.GetConnectionStringAsync(database.Name, token)
            ?? throw new InvalidOperationException("The native PostgreSQL connection is missing.");
        await IsolatedPostgresSlotChecks.VerifyAsync(connection, nodeCount, token);
        if (nodeCount == 1)
        {
            await PostgresSchemaChecks.VerifyAsync(connection, token);
        }
    }

    private static async Task<string> ParameterAsync(IEnumerable<IResource> resources, string name, CancellationToken token)
        => await resources.OfType<ParameterResource>().Single(item => item.Name == name).GetValueAsync(token)
            ?? throw new InvalidOperationException("The native checks credential is missing.");
}
