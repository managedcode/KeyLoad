namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedNativeDispatcher
{
    private const string KeyLoad = "KeyLoad";
    private const string Postgres = "PostgreSQL + pgvector";
    private const string Qdrant = "Qdrant";
    private const string Rabbit = "RabbitMQ";
    private const string Redis = "Redis";
    private const string Neo4j = "Neo4j";
    private const string Mongo = "MongoDB";
    private const string OpenSearch = "OpenSearch";
    private const string Kurrent = "KurrentDB";
    private const string SurrealDb = "SurrealDB";
    private const string HelixDb = "HelixDB";

    internal static void Add(IsolatedResourceContext context)
    {
        switch (context.Selection.Target)
        {
            case KeyLoad:
                IsolatedKeyLoadResources.Add(context);
                break;
            case Postgres:
                IsolatedPostgresResources.Add(context);
                break;
            case Qdrant:
                IsolatedQdrantResources.Add(context);
                break;
            case Rabbit:
                IsolatedRabbitResources.Add(context);
                break;
            case Redis:
                IsolatedRedisResources.Add(context);
                break;
            case Neo4j:
                IsolatedNeo4jResources.Add(context);
                break;
            case Mongo:
                IsolatedMongoResources.Add(context);
                break;
            case OpenSearch:
                IsolatedOpenSearchResources.Add(context);
                break;
            case Kurrent:
                IsolatedKurrentResources.Add(context);
                break;
            case SurrealDb:
                IsolatedSurrealDbResources.Add(context);
                break;
            case HelixDb:
                IsolatedHelixDbResources.Add(context);
                break;
            default:
                throw new InvalidOperationException("IsolatedComparisonSelectionInvalid");
        }
    }
}
